// 3D match view: a WebGL renderer for the same spectator frames the 2D map draws. Fed with setMap()
// and setState() exactly like MapView, so it works for live matches and replays alike.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { SMAAPass } from 'three/addons/postprocessing/SMAAPass.js';
import { GTAOPass } from 'three/addons/postprocessing/GTAOPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { CSS2DRenderer, CSS2DObject } from 'three/addons/renderers/CSS2DRenderer.js';
import { Sky } from 'three/addons/objects/Sky.js';
import { Terrain } from './terrain.js';
import { buildModels, entityMaterial } from './models.js';
import { Effects } from './effects.js';
import { CameraRig } from './camera.js';
import { clamp } from './noise.js';
import { hqName } from '../lore.js';

/** Bright, colour-blind-friendly player colours (the site's dark palette): they read well in daylight. */
const TEAM = ['#e69f00', '#56b4e9', '#2fc497', '#e68fc3', '#ff6b5e', '#b39ddb', '#d4d45a', '#4dd0e1',
  '#bcaaa4', '#f48fb1', '#9fa8da', '#aed581', '#ffab40', '#80cbc4', '#ce93d8', '#b0bec5'].map((c) => new THREE.Color(c));
const teamOf = (slot) => TEAM[((slot % TEAM.length) + TEAM.length) % TEAM.length];
const teamRGB = (slot) => { const c = teamOf(slot); return [c.r, c.g, c.b]; };

const QUALITY = {
  low: { dpr: 1, shadow: 1024, post: false, ao: false },
  medium: { dpr: 1.5, shadow: 2048, post: true, ao: false },
  high: { dpr: 2, shadow: 4096, post: true, ao: true },
};
const SUN_DIR = new THREE.Vector3().setFromSphericalCoords(1, THREE.MathUtils.degToRad(90 - 32), THREE.MathUtils.degToRad(215));
const NO_CUT = 99;
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

/** An instanced mesh with the per-instance attributes the entity material reads, growing as needed. */
class Instances {
  constructor(scene, geo, mat, depthMat, cap = 64) {
    this.scene = scene; this.geo = geo; this.mat = mat; this.depthMat = depthMat;
    this.alloc(cap);
  }
  alloc(cap) {
    if (this.mesh) { this.scene.remove(this.mesh); this.mesh.dispose(); }
    this.cap = cap;
    const geo = this.geo.clone();
    this.team = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(THREE.DynamicDrawUsage);
    this.flash = new THREE.InstancedBufferAttribute(new Float32Array(cap), 1).setUsage(THREE.DynamicDrawUsage);
    this.cut = new THREE.InstancedBufferAttribute(new Float32Array(cap).fill(NO_CUT), 1).setUsage(THREE.DynamicDrawUsage);
    this.walk = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('iTeam', this.team);
    geo.setAttribute('iFlash', this.flash);
    geo.setAttribute('iCut', this.cut);
    geo.setAttribute('iWalk', this.walk);
    this.mesh = new THREE.InstancedMesh(geo, this.mat, cap);
    this.mesh.customDepthMaterial = this.depthMat;
    this.mesh.castShadow = true;
    this.mesh.receiveShadow = true;
    this.mesh.frustumCulled = false;
    this.mesh.count = 0;
    this.scene.add(this.mesh);
    this.n = 0;
  }
  begin() { this.n = 0; }
  push(matrix, colour, flash = 0, cut = NO_CUT, walk = null) {
    if (this.n >= this.cap) this.alloc(this.cap * 2);
    const i = this.n++;
    this.mesh.setMatrixAt(i, matrix);
    this.team.setXYZ(i, colour.r, colour.g, colour.b);
    this.flash.setX(i, flash);
    this.cut.setX(i, cut);
    if (walk) this.walk.setXYZ(i, walk[0], walk[1], walk[2]);
    else this.walk.setXYZ(i, 0, 0, 0);
  }
  end() {
    this.mesh.count = this.n;
    this.mesh.instanceMatrix.needsUpdate = true;
    this.team.needsUpdate = true;
    this.flash.needsUpdate = true;
    this.cut.needsUpdate = true;
    this.walk.needsUpdate = true;
  }
  dispose() { this.scene.remove(this.mesh); this.mesh.geometry.dispose(); this.mesh.dispose(); }
}

/** Shadow pass companion to entityMaterial: construction sites cast only the part already built. */
function entityDepthMaterial() {
  const m = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
  m.onBeforeCompile = (sh) => {
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\nattribute float iCut; varying float vCut; varying float vLocalY;')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\nvCut = iCut; vLocalY = position.y;');
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nvarying float vCut; varying float vLocalY;')
      .replace('#include <clipping_planes_fragment>', '#include <clipping_planes_fragment>\nif (vLocalY > vCut) discard;');
  };
  m.customProgramCacheKey = () => 'netrts-entity-depth';
  return m;
}

/** Health bars: camera-facing quads, one instance per damaged unit or building. */
function healthBars(cap) {
  const geo = new THREE.InstancedBufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array([-0.5, -0.5, 0, 0.5, -0.5, 0, 0.5, 0.5, 0, -0.5, 0.5, 0]), 3));
  geo.setAttribute('uv', new THREE.BufferAttribute(new Float32Array([0, 0, 1, 0, 1, 1, 0, 1]), 2));
  geo.setIndex([0, 1, 2, 0, 2, 3]);
  const pos = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(THREE.DynamicDrawUsage);
  const data = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(THREE.DynamicDrawUsage);
  geo.setAttribute('iPos', pos);
  geo.setAttribute('iData', data);   // fraction, width, opacity
  geo.instanceCount = 0;
  const mat = new THREE.ShaderMaterial({
    vertexShader: `
      attribute vec3 iPos; attribute vec3 iData;
      varying vec2 vUv; varying vec3 vData;
      void main() {
        vUv = uv; vData = iData;
        vec3 right = vec3(viewMatrix[0][0], viewMatrix[1][0], viewMatrix[2][0]);
        vec3 up = vec3(viewMatrix[0][1], viewMatrix[1][1], viewMatrix[2][1]);
        vec3 p = iPos + right * position.x * iData.y + up * position.y * 0.045;
        gl_Position = projectionMatrix * viewMatrix * vec4(p, 1.0);
      }`,
    fragmentShader: `
      varying vec2 vUv; varying vec3 vData;
      void main() {
        float px = 0.06;
        vec2 e = min(vUv, 1.0 - vUv);
        if (e.y < 0.18 || e.x < px * 0.12) { gl_FragColor = vec4(0.05, 0.06, 0.07, 0.85 * vData.z); return; }
        float f = vData.x;
        vec3 good = vec3(0.37, 0.86, 0.55), mid = vec3(1.0, 0.8, 0.25), bad = vec3(1.0, 0.3, 0.25);
        vec3 c = f > 0.5 ? mix(mid, good, (f - 0.5) * 2.0) : mix(bad, mid, f * 2.0);
        gl_FragColor = vUv.x <= f ? vec4(c * 1.4, vData.z) : vec4(0.12, 0.13, 0.15, 0.7 * vData.z);
      }`,
    transparent: true,
    depthWrite: false,
  });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.frustumCulled = false;
  mesh.renderOrder = 10;
  return { mesh, pos, data, cap, n: 0 };
}

export class View3D {
  constructor(container, { badger = false, names = () => new Map() } = {}) {
    this.container = container;
    this.badger = badger;
    this.names = names;
    this.map = null;
    this.state = null;
    this.vision = null;
    this.units = new Map();      // id -> animated unit
    this.buildings = new Map();  // id -> building record
    this.corpses = [];
    this.oreMax = new Map();
    this.lastSet = 0;
    this.interval = 300;
    this.hotspots = [];          // recent explosions, for the director
    this.grid = false;
    this.alive = true;
    this.onHoverTile = null;     // (tile | null, {x, y}) => void
    this.onModeChange = null;
    this.onFollowChange = null;  // (unit | null) => void

    let quality = 'medium';
    try { quality = localStorage.getItem('netrts.3d.quality') || quality; } catch { /* storage blocked */ }
    this.quality = QUALITY[quality] ? quality : 'medium';
    this.autoQuality = { frames: 0, time: 0, done: !!this.storedQuality() };

    const renderer = new THREE.WebGLRenderer({ antialias: false, powerPreference: 'high-performance', alpha: false });
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 0.95;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.domElement.className = 'map3d-canvas';
    renderer.domElement.tabIndex = 0;
    renderer.domElement.setAttribute('role', 'img');
    renderer.domElement.setAttribute('aria-label', '3D battlefield. Drag to pan, right-drag to turn, scroll to zoom, click a unit to follow it.');
    container.appendChild(renderer.domElement);
    this.renderer = renderer;

    this.labels = new CSS2DRenderer();
    this.labels.domElement.className = 'map3d-labels';
    container.appendChild(this.labels.domElement);

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(42, 16 / 9, 0.08, 900);
    this.clock = new THREE.Clock();
    this.models = buildModels(badger);
    this.mat = entityMaterial();
    this.depthMat = entityDepthMaterial();
    this.setupSky();

    const loop = () => {
      if (!this.alive) return;
      this.raf = requestAnimationFrame(loop);
      this.frame();
    };
    this.raf = requestAnimationFrame(loop);
    this.ro = new ResizeObserver(() => this.resize());
    this.ro.observe(container);
  }

  storedQuality() { try { return localStorage.getItem('netrts.3d.quality'); } catch { return null; } }

  // ---------------------------------------------------------------- scene

  setupSky() {
    const scene = this.scene;
    const sky = new Sky();
    sky.scale.setScalar(4000);
    const u = sky.material.uniforms;
    u.turbidity.value = 5.5;
    u.rayleigh.value = 1.4;
    u.mieCoefficient.value = 0.004;
    u.mieDirectionalG.value = 0.82;
    u.sunPosition.value.copy(SUN_DIR);
    scene.add(sky);
    this.sky = sky;

    // Image-based lighting from the same sky, so every surface picks up its colour.
    const pmrem = new THREE.PMREMGenerator(this.renderer);
    const envScene = new THREE.Scene();
    const envSky = new Sky();
    envSky.scale.setScalar(100);
    Object.entries(u).forEach(([k, v]) => { envSky.material.uniforms[k].value = v.value?.clone ? v.value.clone() : v.value; });
    envScene.add(envSky);
    this.envRT = pmrem.fromScene(envScene, 0, 0.1, 200);
    scene.environment = this.envRT.texture;
    scene.environmentIntensity = 0.55;
    pmrem.dispose();
    envSky.material.dispose();

    scene.fog = new THREE.Fog('#bccbd8', 60, 240);

    const sun = new THREE.DirectionalLight('#ffe0b5', 2.7);
    sun.castShadow = true;
    sun.shadow.bias = -0.0003;
    sun.shadow.normalBias = 0.025;
    scene.add(sun, sun.target);
    this.sun = sun;
    scene.add(new THREE.HemisphereLight('#b9d3ff', '#6a5a40', 0.55));
  }

  setMap(map) {
    if (this.map && this.map.width === map.width && this.map.height === map.height
      && this.map.terrain.join('') === map.terrain.join('')) return;
    this.map = map;
    this.terrain?.dispose();
    if (this.terrain) this.scene.remove(this.terrain.group);
    this.terrain = new Terrain(map);
    this.terrain.uniforms.uGrid.value = this.grid ? 1 : 0;
    this.scene.add(this.terrain.group);
    const span = Math.max(map.width, map.height);
    this.scene.fog.near = span * 1.3;
    this.scene.fog.far = span * 4.6;
    this.effects?.dispose();
    this.effects = new Effects(this.scene, this.terrain);
    this.rig?.dispose();
    this.rig = new CameraRig(this.camera, this.renderer.domElement, this.terrain);
    this.rig.onModeChange = (m) => { if (m !== 'follow') this.setFollowed(null); this.onModeChange?.(m); };
    this.rig.onPick = (x, y) => this.pick(x, y);
    this.rig.onHover = (x, y) => this.hover(x, y);
    this.buildLayers();
    this.units.clear();
    this.buildings.clear();
    this.corpses.length = 0;
    this.applyQuality();
    this.resize();
  }

  buildLayers() {
    for (const l of Object.values(this.layers || {})) l.dispose?.();
    for (const l of this.bLayers?.values() || []) l.dispose();
    this.bLayers = new Map();    // building model key -> Instances, made when a model first appears
    if (this.rings) { this.scene.remove(this.rings); this.rings.dispose(); }
    if (this.bars) this.scene.remove(this.bars.mesh);
    for (const l of this.labelObjs?.values() || []) { l.removeFromParent(); }
    const L = {};
    for (const [type, def] of Object.entries(this.models.units)) L['u' + type] = new Instances(this.scene, def.geo, this.mat, this.depthMat, 64);
    L.deposit = new Instances(this.scene, this.models.deposit, this.mat, this.depthMat, 32);
    L.nugget = new Instances(this.scene, this.models.nugget, this.mat, this.depthMat, 32);
    L.scaffold = new Instances(this.scene, this.models.scaffold, this.mat, this.depthMat, 8);
    for (const k of ['nugget', 'deposit']) L[k].mesh.castShadow = k === 'deposit';
    this.layers = L;

    // Team rings under every unit.
    const ringGeo = new THREE.RingGeometry(0.15, 0.2, 24).rotateX(-Math.PI / 2);
    this.rings = new THREE.InstancedMesh(ringGeo, new THREE.MeshBasicMaterial({
      transparent: true, opacity: 0.8, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -4, toneMapped: false,
    }), 1024);
    this.rings.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(1024 * 3), 3);
    this.rings.frustumCulled = false;
    this.rings.count = 0;
    this.rings.renderOrder = 1;
    this.scene.add(this.rings);

    // The followed unit's marker.
    if (!this.marker) {
      this.marker = new THREE.Mesh(new THREE.RingGeometry(0.26, 0.32, 40).rotateX(-Math.PI / 2),
        new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, depthWrite: false, toneMapped: false }));
      this.marker.renderOrder = 2;
      this.marker.visible = false;
      this.scene.add(this.marker);
    }

    this.bars = healthBars(1024);
    this.scene.add(this.bars.mesh);
    this.labelObjs = new Map();
  }

  // ---------------------------------------------------------------- quality

  setQuality(q) {
    if (!QUALITY[q]) return;
    this.quality = q;
    this.autoQuality.done = true;
    try { localStorage.setItem('netrts.3d.quality', q); } catch { /* storage blocked */ }
    this.applyQuality();
  }

  applyQuality() {
    const Q = QUALITY[this.quality];
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, Q.dpr));
    this.sun.shadow.mapSize.set(Q.shadow, Q.shadow);
    if (this.sun.shadow.map) { this.sun.shadow.map.dispose(); this.sun.shadow.map = null; }
    this.composer?.dispose();
    this.composer = null;
    if (Q.post) {
      const size = this.renderer.getDrawingBufferSize(new THREE.Vector2());
      const rt = new THREE.WebGLRenderTarget(size.x, size.y, { type: THREE.HalfFloatType, samples: 0 });
      const c = new EffectComposer(this.renderer, rt);
      c.addPass(new RenderPass(this.scene, this.camera));
      if (Q.ao) {
        const ao = new GTAOPass(this.scene, this.camera, size.x, size.y);
        ao.output = GTAOPass.OUTPUT.Default;
        ao.blendIntensity = 0.85;
        ao.updateGtaoMaterial({ radius: 0.35, distanceExponent: 1.6, thickness: 1, scale: 1 });
        c.addPass(ao);
      }
      this.bloom = new UnrealBloomPass(new THREE.Vector2(size.x, size.y), 0.55, 0.5, 1.25);
      c.addPass(this.bloom);
      c.addPass(new ShaderPass(gradeShader()));
      c.addPass(new OutputPass());
      c.addPass(new SMAAPass(size.x, size.y));
      this.composer = c;
    }
    this.resize(true);
  }

  resize(force = false) {
    const w = Math.max(1, this.container.clientWidth), h = Math.max(1, this.container.clientHeight);
    if (!force && w === this.w && h === this.h) return;
    this.w = w; this.h = h;
    this.renderer.setSize(w, h, false);
    this.renderer.domElement.style.width = `${w}px`;
    this.renderer.domElement.style.height = `${h}px`;
    this.labels.setSize(w, h);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    const size = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    this.composer?.setSize(w, h);
    this.composer?.setPixelRatio?.(this.renderer.getPixelRatio());
    this.effects?.resize(size.y, this.camera.fov);
  }

  // ---------------------------------------------------------------- state

  setVision(slot) {
    this.vision = slot;
    this.applyVision();
  }

  applyVision() {
    if (!this.terrain) return;
    const p = this.vision == null ? null : this.state?.players.find((pl) => pl.slot === this.vision);
    this.terrain.setVisibility(p?.visibility);
    this.visRows = p?.visibility?.length ? p.visibility : null;
  }

  setState(s) {
    if (!this.terrain) return;
    const prev = this.state;
    const now = performance.now();
    const step = prev && s.tick === prev.tick + 1;
    const same = prev && s.tick === prev.tick;
    if (step) {
      // Animate over however long ticks are really taking (live pace or replay speed).
      const gap = now - this.lastSet;
      this.interval = clamp(this.interval * 0.6 + gap * 0.4, 30, 1500);
    } else if (!same) {
      this.effects.clear();
      this.corpses.length = 0;
    }
    this.lastSet = now;
    this.state = s;
    const fx = step && !reducedMotion();
    const dur = reducedMotion() ? 0 : this.interval * (step ? 1.0 : 0);

    for (const r of s.resources) this.oreMax.set(r.id, Math.max(this.oreMax.get(r.id) || 0, r.remaining, 500));

    // Where everything stands at the end of this tick.
    const where = new Map();
    for (const b of s.buildings) where.set(b.id, { x: b.x + 0.5, z: b.y + 0.5, building: b });
    for (const r of s.resources) where.set(r.id, { x: r.x + 0.5, z: r.y + 0.5, resource: r });
    const groups = new Map();
    for (const u of s.units) {
      const k = u.y * 4096 + u.x;
      if (!groups.has(k)) groups.set(k, []);
      groups.get(k).push(u);
    }
    for (const list of groups.values()) {
      const n = list.length;
      list.sort((a, b) => a.id - b.id).forEach((u, i) => {
        let ox = 0.5, oz = 0.5;
        if (n > 1) {
          const cols = n <= 4 ? 2 : 3;
          const slot = i % (cols * cols);
          ox = (slot % cols + 0.5) / cols;
          oz = (Math.floor(slot / cols) + 0.5) / cols;
        }
        where.set(u.id, { x: u.x + ox, z: u.y + oz, unit: u, scale: n > 4 ? 0.78 : 1 });
      });
    }

    // Units: move, appear, take hits, fall.
    const seen = new Set();
    const hurt = [];
    for (const u of s.units) {
      seen.add(u.id);
      const w = where.get(u.id);
      let a = this.units.get(u.id);
      if (!a) {
        a = { id: u.id, fx: w.x, fz: w.z, tx: w.x, tz: w.z, t0: now, dur: 0, yaw: (u.owner * 1.7) % (Math.PI * 2), hitT: -1e9, strikeT: -1e9, born: now };
        this.units.set(u.id, a);
        if (fx) this.effects.spawn(this.ground(w.x, w.z), teamRGB(u.owner));
      } else {
        const p = this.unitPos(a, now);
        a.fx = p.x; a.fz = p.z; a.tx = w.x; a.tz = w.z; a.t0 = now; a.dur = dur;
        if (!step && !same) { a.fx = w.x; a.fz = w.z; }
        if (fx && u.hp < a.u.hp) hurt.push(u);
      }
      a.u = u;
      a.scale = w.scale;
    }
    for (const [id, a] of this.units) {
      if (seen.has(id)) continue;
      this.units.delete(id);
      if (fx) {
        const p = this.unitPos(a, now);
        this.effects.death(this.ground(p.x, p.z), teamRGB(a.u.owner));
        this.corpses.push({ a, x: p.x, z: p.z, t0: now });
        if (this.followed === id) this.followedLost = { x: p.x, z: p.z, t: now };
      }
    }

    // Who hit whom: archers loose arrows, melee units strike, towers fire on what nothing else is hitting.
    const attackers = new Map();
    for (const u of s.units) {
      if (u.activity !== 'Attacking' || u.targetId == null) continue;
      if (!attackers.has(u.targetId)) attackers.set(u.targetId, []);
      attackers.get(u.targetId).push(u);
    }
    const strike = (targetId, targetPos, height) => {
      const list = attackers.get(targetId) || [];
      for (const att of list) {
        const aa = this.units.get(att.id);
        if (!aa) continue;
        aa.strikeT = now;
        if (att.type === 'Archer') {
          const from = this.ground(aa.tx, aa.tz).add(new THREE.Vector3(0, 0.32, 0));
          this.effects.arrow(from, targetPos.clone().add(new THREE.Vector3(0, height * 0.5, 0)), Math.min(0.6, this.interval * 0.0009 + 0.12));
        } else {
          const tp = targetPos.clone().add(new THREE.Vector3(0, height * 0.45, 0));
          this.effects.spark(tp.lerp(this.ground(aa.tx, aa.tz).add(new THREE.Vector3(0, 0.25, 0)), 0.35), 7);
        }
      }
      return list.length;
    };
    for (const u of hurt) {
      const a = this.units.get(u.id);
      a.hitT = now + (this.anyArcher(attackers.get(u.id)) ? Math.min(600, this.interval * 0.9 + 120) : 0);
      const tp = this.ground(a.tx, a.tz);
      if (!strike(u.id, tp, 0.45)) this.towerFire(s, u, tp);
    }

    // Buildings: rise, finish, take hits, burn, explode.
    const bSeen = new Set();
    for (const b of s.buildings) {
      bSeen.add(b.id);
      const rec = this.buildings.get(b.id);
      const p = this.ground(b.x + 0.5, b.y + 0.5);
      const def = this.models.building(b.type, b.owner);
      if (!rec) {
        if (fx) this.effects.dust(p, 14, [0.62, 0.55, 0.44], 0.9);
        this.buildings.set(b.id, { b, hitT: -1e9, cut: b.completed ? NO_CUT : def.height * Math.max(0.04, b.constructionPercent / 100) });
        continue;
      }
      if (fx && b.completed && !rec.b.completed) this.effects.completed(p, teamRGB(b.owner), def.height);
      if (fx && !b.completed && b.constructionPercent > rec.b.constructionPercent && Math.random() < 0.5) this.effects.dust(p, 2, [0.7, 0.62, 0.5], 0.5);
      if (fx && b.hp < rec.b.hp && b.completed) {
        rec.hitT = now;
        strike(b.id, p, def.height);
      }
      rec.b = b;
    }
    for (const [id, rec] of this.buildings) {
      if (bSeen.has(id)) continue;
      this.buildings.delete(id);
      if (fx) {
        const p = this.ground(rec.b.x + 0.5, rec.b.y + 0.5);
        const big = rec.b.type === 'CommandCenter' || rec.b.completed;
        this.effects.explosion(p, teamRGB(rec.b.owner), big);
        this.hotspots.push({ x: p.x, z: p.z, t: now, size: big ? 12 : 6 });
      }
    }
    if (fx) {
      for (const r of prev?.resources || []) {
        if (!s.resources.some((x) => x.id === r.id)) this.effects.dust(this.ground(r.x + 0.5, r.y + 0.5), 10, [0.75, 0.62, 0.3], 0.6);
      }
    }
    this.applyVision();
    this.updateLabels();
  }

  anyArcher(list) { return (list || []).some((u) => u.type === 'Archer'); }

  /** Guard towers: the frames don't say what a tower shot, so fire at a hurt enemy in range. */
  towerFire(s, u, tp) {
    let best = null, bd = 1e9;
    for (const b of s.buildings) {
      if (b.type !== 'GuardTower' || !b.completed || b.owner === u.owner) continue;
      const d = Math.max(Math.abs(b.x - u.x), Math.abs(b.y - u.y));
      if (d <= 5 && d < bd) { best = b; bd = d; }
    }
    if (!best) return;
    const top = this.ground(best.x + 0.5, best.y + 0.5).add(new THREE.Vector3(0, this.models.building('GuardTower', best.owner).height - 0.1, 0));
    this.effects.bolt(top, tp.clone().add(new THREE.Vector3(0, 0.25, 0)), Math.min(0.45, this.interval * 0.0007 + 0.1));
  }

  ground(x, z) { return new THREE.Vector3(x, this.terrain.heightAt(x, z), z); }

  unitPos(a, now) {
    if (!a.dur) return { x: a.tx, z: a.tz, t: 1 };
    const t = Math.min(1, (now - a.t0) / a.dur);
    return { x: a.fx + (a.tx - a.fx) * t, z: a.fz + (a.tz - a.fz) * t, t };
  }

  // ---------------------------------------------------------------- per frame

  frame() {
    if (!this.terrain) {
      this.renderer.setClearColor('#9fb3c2');
      this.renderer.clear();
      return;
    }
    const dt = Math.min(0.1, this.clock.getDelta());
    const now = performance.now();
    this.rig.update(dt, this.effects.shake);
    this.updateUnits(now, dt);
    this.updateBuildings(now, dt);
    this.effects.update(dt);
    this.updateSun();
    this.terrain.uniforms.uTime.value += dt;
    if (this.composer) this.composer.render(dt);
    else this.renderer.render(this.scene, this.camera);
    this.labels.render(this.scene, this.camera);
    this.checkQuality(dt);
  }

  /** Shadows follow the camera, sized to what's on screen. */
  updateSun() {
    const t = this.rig.mode === 'fly' ? this.rig.fly.pos.clone().addScaledVector(this.rig.forward(), 10) : this.rig.cur.target;
    const reach = clamp((this.rig.mode === 'fly' ? 18 : this.rig.cur.dist) * 0.9, 6, 70);
    const c = this.sun.shadow.camera;
    if (c.right !== reach) {
      c.left = -reach; c.right = reach; c.top = reach; c.bottom = -reach;
      c.near = 1; c.far = 300;
      c.updateProjectionMatrix();
    }
    // Snap to shadow texels so edges don't crawl as the camera moves.
    const texel = (2 * reach) / this.sun.shadow.mapSize.x;
    const tx = Math.round(t.x / texel) * texel, tz = Math.round(t.z / texel) * texel;
    this.sun.target.position.set(tx, 0, tz);
    this.sun.position.set(tx + SUN_DIR.x * 120, SUN_DIR.y * 120, tz + SUN_DIR.z * 120);
  }

  visibleTo(x, y) {
    if (!this.visRows) return true;
    return this.visRows[y]?.[x] !== '0';
  }

  updateUnits(now, dt) {
    const L = this.layers;
    for (const type of Object.keys(this.models.units)) L['u' + type].begin();
    L.nugget.begin();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(0, 0, 0, 'YXZ'), v = new THREE.Vector3(), sc = new THREE.Vector3();
    const ringCol = new THREE.Color();
    let ri = 0;
    const bars = this.bars;
    bars.n = 0;
    const camPos = this.camera.position;
    const k = 1 - Math.exp(-12 * dt);
    const positions = new Map();
    for (const a of this.units.values()) positions.set(a.id, this.unitPos(a, now));
    this.positions = positions;

    for (const a of this.units.values()) {
      const u = a.u;
      if (this.vision != null && u.owner !== this.vision && !this.visibleTo(u.x, u.y)) continue;
      const p = positions.get(a.id);
      const moving = p.t < 1 && Math.hypot(a.tx - a.fx, a.tz - a.fz) > 0.01;
      // Face where we're going, or what we're hitting.
      let want = a.yaw;
      if (moving) want = Math.atan2(a.tx - a.fx, a.tz - a.fz);
      else if (u.targetId != null && (u.activity === 'Attacking' || u.activity === 'Gathering' || u.activity === 'Building')) {
        const tp = positions.get(u.targetId);
        const tb = this.buildings.get(u.targetId)?.b;
        const tr = this.state.resources.find((r) => r.id === u.targetId);
        const tx = tp ? tp.x : tb ? tb.x + 0.5 : tr ? tr.x + 0.5 : null;
        const tz = tp ? tp.z : tb ? tb.y + 0.5 : tr ? tr.y + 0.5 : null;
        if (tx != null && Math.hypot(tx - p.x, tz - p.z) > 0.05) want = Math.atan2(tx - p.x, tz - p.z);
      }
      a.yaw += Math.atan2(Math.sin(want - a.yaw), Math.cos(want - a.yaw)) * k;

      const phase = now * 0.001 + a.id * 0.37;
      let y = this.terrain.heightAt(p.x, p.z), pitch = 0, roll = 0, fwd = 0, squash = 1;
      if (moving) {
        // Bob twice per stride, in step with the legs.
        const w = a.walk ?? 0;
        y += Math.abs(Math.cos(w)) * (u.type === 'Scout' ? 0.045 : 0.03);
        pitch = u.type === 'Scout' ? Math.sin(w * 2) * 0.08 : 0.08;
        roll = Math.sin(w) * 0.04;
      } else if (u.activity === 'Gathering' || u.activity === 'Building') {
        const dig = Math.sin(phase * Math.PI * 2 * 1.6);
        pitch = 0.18 + dig * 0.22;
        y += Math.max(0, -dig) * 0.015;
      } else {
        squash = 1 + Math.sin(phase * 2.4) * 0.015;
      }
      const since = now - a.strikeT;
      if (since >= 0 && since < 320) {
        const s = Math.sin((since / 320) * Math.PI);
        if (u.type === 'Archer') { fwd = -s * 0.03; pitch -= s * 0.1; }
        else { fwd = s * 0.11; pitch += s * 0.3; }
      }
      const flash = Math.max(0, 1 - Math.abs(now - a.hitT) / 160);
      const born = Math.min(1, (now - a.born) / 350);
      const s = (this.models.units[u.type].scale || 1) * a.scale * (0.4 + 0.6 * born);
      v.set(p.x + Math.sin(a.yaw) * fwd, y, p.z + Math.cos(a.yaw) * fwd);
      e.set(pitch, a.yaw, roll);
      sc.set(s, s * squash, s);
      m.compose(v, q.setFromEuler(e), sc);
      // Limbs: stride when moving, swing tools while working, hack away in a fight.
      const working = !moving && (u.activity === 'Gathering' || u.activity === 'Building');
      const fighting = since >= 0 && since < 320 && u.type !== 'Archer';
      const legGoal = moving ? (u.type === 'Scout' ? 0.75 : 0.7) : 0;
      const armGoal = moving ? 0.55 : working ? 0.85 : fighting ? 1.1 : 0;
      a.leg = (a.leg ?? 0) + (legGoal - (a.leg ?? 0)) * k;
      a.arm = (a.arm ?? 0) + (armGoal - (a.arm ?? 0)) * k;
      a.walk = (a.walk ?? a.id) + dt * (moving ? (u.type === 'Scout' ? 15 : 11) : working ? 8 : fighting ? 18 : 4);
      L['u' + u.type].push(m, teamOf(u.owner), flash, NO_CUT, [a.walk, a.leg, a.arm]);
      if (u.carrying > 0) {
        const back = 0.11 * s;
        v.set(p.x - Math.sin(a.yaw) * back, y + 0.33 * s, p.z - Math.cos(a.yaw) * back);
        m.compose(v, q, sc.set(s, s, s));
        L.nugget.push(m, teamOf(u.owner));
      }
      if (ri < this.rings.instanceMatrix.count) {
        m.compose(v.set(p.x, this.terrain.heightAt(p.x, p.z) + 0.02, p.z), q.identity(), sc.set(s, 1, s));
        this.rings.setMatrixAt(ri, m);
        this.rings.setColorAt(ri, ringCol.copy(teamOf(u.owner)));
        ri++;
      }
      if (u.hp < u.maxHp && bars.n < bars.cap) {
        const d = camPos.distanceTo(v.set(p.x, y, p.z));
        const op = clamp(1.6 - d / 22, 0, 1);
        if (op > 0) {
          bars.pos.setXYZ(bars.n, p.x, y + this.models.units[u.type].height * s + 0.1, p.z);
          bars.data.setXYZ(bars.n, u.hp / u.maxHp, 0.34, op);
          bars.n++;
        }
      }
      // Gathering throws up a little dirt now and then.
      if (u.activity === 'Gathering' && !moving && Math.random() < dt * 1.5) {
        this.effects.dust({ x: p.x + Math.sin(a.yaw) * 0.25, y, z: p.z + Math.cos(a.yaw) * 0.25 }, 1, [0.6, 0.5, 0.35], 0.25);
      }
    }

    // The fallen topple over and sink away.
    this.corpses = this.corpses.filter((c) => now - c.t0 < 2600);
    for (const c of this.corpses) {
      const t = (now - c.t0) / 1000;
      const fall = Math.min(1, t / 0.35);
      const y = this.terrain.heightAt(c.x, c.z) - Math.max(0, t - 1.2) * 0.25;
      e.set(-fall * Math.PI / 2 * 0.95, c.a.yaw, 0);
      const s = (this.models.units[c.a.u.type].scale || 1) * (c.a.scale || 1);
      m.compose(v.set(c.x, y, c.z), q.setFromEuler(e), sc.set(s, s, s));
      L['u' + c.a.u.type].push(m, teamOf(c.a.u.owner).clone().multiplyScalar(0.5));
    }
    for (const type of Object.keys(this.models.units)) L['u' + type].end();
    L.nugget.end();
    this.rings.count = ri;
    this.rings.instanceMatrix.needsUpdate = true;
    if (this.rings.instanceColor) this.rings.instanceColor.needsUpdate = true;

    // The followed unit.
    const f = this.followed != null ? positions.get(this.followed) : null;
    if (f) {
      this.marker.visible = true;
      this.marker.position.set(f.x, this.terrain.heightAt(f.x, f.z) + 0.03, f.z);
      const pulse = 1 + Math.sin(now * 0.006) * 0.08;
      this.marker.scale.set(pulse, 1, pulse);
      this.marker.material.color.copy(teamOf(this.units.get(this.followed).u.owner)).lerp(new THREE.Color('#ffffff'), 0.5);
      this.onFollowChange?.(this.units.get(this.followed).u);
    } else {
      this.marker.visible = false;
    }
  }

  buildingLayer(def) {
    let l = this.bLayers.get(def.key);
    if (!l) {
      l = new Instances(this.scene, def.geo, this.mat, this.depthMat, 8);
      this.bLayers.set(def.key, l);
    }
    return l;
  }

  updateBuildings(now, dt) {
    const L = this.layers;
    for (const l of this.bLayers.values()) l.begin();
    L.deposit.begin();
    L.scaffold.begin();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), v = new THREE.Vector3(), sc = new THREE.Vector3(1, 1, 1);
    const white = new THREE.Color('#ffffff');
    const bars = this.bars;
    const camPos = this.camera.position;
    const k = 1 - Math.exp(-6 * dt);
    for (const rec of this.buildings.values()) {
      const b = rec.b;
      const def = this.models.building(b.type, b.owner);
      const x = b.x + 0.5, z = b.y + 0.5;
      const y = this.terrain.heightAt(x, z);
      // Buildings face the middle of the map, so their fronts are on show.
      const yaw = Math.round(Math.atan2(this.terrain.W / 2 - x, this.terrain.H / 2 - z) / (Math.PI / 2)) * (Math.PI / 2);
      q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), yaw);
      const goal = b.completed ? NO_CUT : def.height * Math.max(0.04, b.constructionPercent / 100);
      rec.cut = goal === NO_CUT || rec.cut === NO_CUT ? goal : rec.cut + (goal - rec.cut) * k;
      const flash = Math.max(0, 1 - (now - rec.hitT) / 120) * 0.22;
      m.compose(v.set(x, y, z), q, sc.set(1, 1, 1));
      this.buildingLayer(def).push(m, teamOf(b.owner), flash, rec.cut);
      if (!b.completed) L.scaffold.push(m, white);
      if (b.completed && b.hp < b.maxHp) {
        const hurt = 1 - b.hp / b.maxHp;
        if (hurt > 0.35) this.effects.burning(v.set(x, y + def.height * 0.4, z), clamp((hurt - 0.35) / 0.5, 0, 1), dt);
        const d = camPos.distanceTo(v.set(x, y, z));
        const op = clamp(1.8 - d / 30, 0, 1);
        if (op > 0 && bars.n < bars.cap) {
          bars.pos.setXYZ(bars.n, x, y + def.height + 0.18, z);
          bars.data.setXYZ(bars.n, b.hp / b.maxHp, 0.7, op);
          bars.n++;
        }
      }
    }
    for (const r of this.state?.resources || []) {
      if (r.remaining <= 0) continue;
      const frac = Math.min(1, r.remaining / (this.oreMax.get(r.id) || r.remaining));
      const s = 0.55 + 0.6 * frac;
      const x = r.x + 0.5, z = r.y + 0.5;
      q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), r.id * 1.3);
      m.compose(v.set(x, this.terrain.heightAt(x, z), z), q, sc.set(s, s, s));
      L.deposit.push(m, white);
    }
    for (const l of this.bLayers.values()) l.end();
    L.deposit.end();
    L.scaffold.end();
    bars.mesh.geometry.instanceCount = bars.n;
    bars.pos.needsUpdate = true;
    bars.data.needsUpdate = true;
  }

  /** Player names float over their headquarters. */
  updateLabels() {
    const names = this.names();
    const want = new Map();
    for (const b of this.state?.buildings || []) {
      if (b.type !== 'CommandCenter' || want.has(b.owner)) continue;
      want.set(b.owner, b);
    }
    for (const [slot, obj] of this.labelObjs) {
      if (!want.has(slot)) { obj.removeFromParent(); this.labelObjs.delete(slot); }
    }
    for (const [slot, b] of want) {
      let obj = this.labelObjs.get(slot);
      if (!obj) {
        const el = document.createElement('div');
        el.className = 'map3d-label';
        el.style.setProperty('--pc', `#${teamOf(slot).getHexString(THREE.SRGBColorSpace)}`);
        obj = new CSS2DObject(el);
        this.scene.add(obj);
        this.labelObjs.set(slot, obj);
      }
      const who = names.get(slot) ?? `Player ${slot + 1}`;
      const where = this.badger ? hqName(slot) : '';
      if (obj.element.dataset.text !== who + where) {
        obj.element.dataset.text = who + where;
        obj.element.replaceChildren(document.createTextNode(who));
        if (where) {
          const sub = document.createElement('small');
          sub.textContent = where;
          obj.element.append(sub);
        }
      }
      const x = b.x + 0.5, z = b.y + 0.5;
      obj.position.set(x, this.terrain.heightAt(x, z) + this.models.building('CommandCenter', slot).height + 0.45, z);
    }
  }

  // ---------------------------------------------------------------- camera helpers for the page

  /** Centre of the biggest fight right now, or a fresh explosion. */
  hotspot() {
    const now = performance.now();
    this.hotspots = this.hotspots.filter((h) => now - h.t < 4000);
    if (this.hotspots.length) return this.hotspots[this.hotspots.length - 1];
    const s = this.state;
    if (!s) return null;
    if (this.hotCache?.tick === s.tick) return this.hotCache.spot;
    const spot = this.crowd(s);
    this.hotCache = { tick: s.tick, spot };
    return spot;
  }

  crowd(s) {
    // Fights first; otherwise the busiest crowd, armies counting more than workers.
    const fighting = s.units.filter((u) => u.activity === 'Attacking');
    const pool = fighting.length ? fighting : s.units;
    if (!pool.length) return null;
    const weight = (u) => (fighting.length ? 1 : u.type === 'Worker' ? 1 : u.type === 'Scout' ? 1.5 : 3);
    let best = null, bestW = 0;
    for (const u of pool) {
      let w = 0;
      for (const o of pool) if (Math.max(Math.abs(o.x - u.x), Math.abs(o.y - u.y)) <= 5) w += weight(o);
      if (w > bestW) { bestW = w; best = u; }
    }
    const group = pool.filter((o) => Math.max(Math.abs(o.x - best.x), Math.abs(o.y - best.y)) <= 5);
    return {
      x: group.reduce((t, u) => t + u.x, 0) / group.length + 0.5,
      z: group.reduce((t, u) => t + u.y, 0) / group.length + 0.5,
      size: Math.max(4, group.length),
    };
  }

  setMode(mode) {
    if (!this.rig) return;
    if (mode === 'director') this.rig.startDirector(() => this.hotspot());
    else this.rig.setMode(mode);
  }

  get mode() { return this.rig?.mode ?? 'orbit'; }

  overview() { this.rig?.overview(); }
  zoom(f) { this.rig?.zoom(f); }

  setGrid(on) {
    this.grid = on;
    if (this.terrain) this.terrain.uniforms.uGrid.value = on ? 1 : 0;
  }

  setFollowed(id) {
    this.followed = id;
    if (id == null) this.onFollowChange?.(null);
  }

  /** Click: follow the unit under the cursor, or look at the building there. */
  pick(clientX, clientY) {
    if (!this.positions) return;
    const r = this.renderer.domElement.getBoundingClientRect();
    const v = new THREE.Vector3();
    let best = null, bd = 26;
    for (const [id, p] of this.positions) {
      const a = this.units.get(id);
      v.set(p.x, this.terrain.heightAt(p.x, p.z) + 0.22, p.z).project(this.camera);
      if (v.z > 1) continue;
      const sx = r.left + (v.x + 1) / 2 * r.width, sy = r.top + (1 - v.y) / 2 * r.height;
      const d = Math.hypot(sx - clientX, sy - clientY);
      if (d < bd && (this.vision == null || a.u.owner === this.vision || this.visibleTo(a.u.x, a.u.y))) { bd = d; best = id; }
    }
    if (best != null) {
      this.setFollowed(best);
      this.rig.follow(() => {
        const p = this.positions?.get(this.followed);
        if (p) return new THREE.Vector3(p.x, this.terrain.heightAt(p.x, p.z) + 0.2, p.z);
        // The unit fell: linger where it went down, then let go.
        const lost = this.followedLost;
        if (lost && performance.now() - lost.t < 2500) return new THREE.Vector3(lost.x, this.terrain.heightAt(lost.x, lost.z), lost.z);
        return null;
      });
      return;
    }
    const hit = this.rig.groundAt(clientX, clientY);
    if (hit) {
      const b = this.state?.buildings.find((x) => x.x === Math.floor(hit.x) && x.y === Math.floor(hit.z));
      if (b) this.rig.focus(b.x + 0.5, b.y + 0.5, 5);
    }
  }

  hover(clientX, clientY) {
    if (!this.onHoverTile) return;
    if (clientX == null) { this.onHoverTile(null); return; }
    const hit = this.rig.groundAt(clientX, clientY);
    const r = this.renderer.domElement.getBoundingClientRect();
    if (!hit || !this.map) { this.onHoverTile(null); return; }
    const x = Math.floor(hit.x), y = Math.floor(hit.z);
    if (x < 0 || y < 0 || x >= this.map.width || y >= this.map.height) { this.onHoverTile(null); return; }
    this.onHoverTile({ x, y }, { x: clientX - r.left, y: clientY - r.top });
  }

  /** Drop to a cheaper setting if the first few seconds run slowly, unless the viewer picked one. */
  checkQuality(dt) {
    const a = this.autoQuality;
    if (a.done || document.hidden) return;
    a.frames++;
    a.time += dt;
    if (a.time < 4) return;
    const fps = a.frames / a.time;
    a.done = true;
    if (fps < 28 && this.quality !== 'low') {
      this.quality = this.quality === 'high' ? 'medium' : 'low';
      this.applyQuality();
      this.onQualityChange?.(this.quality);
    }
  }

  destroy() {
    this.alive = false;
    cancelAnimationFrame(this.raf);
    this.ro.disconnect();
    this.rig?.dispose();
    this.effects?.dispose();
    for (const l of Object.values(this.layers || {})) l.dispose();
    for (const l of this.bLayers?.values() || []) l.dispose();
    this.terrain?.dispose();
    this.composer?.dispose();
    this.envRT?.dispose();
    this.renderer.dispose();
    this.renderer.forceContextLoss?.();
    this.renderer.domElement.remove();
    this.labels.domElement.remove();
  }
}

/** Gentle grade: a warm lift in the highlights and a soft vignette. */
function gradeShader() {
  return {
    uniforms: { tDiffuse: { value: null } },
    vertexShader: 'varying vec2 vUv; void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
    fragmentShader: `
      uniform sampler2D tDiffuse; varying vec2 vUv;
      void main() {
        vec4 c = texture2D(tDiffuse, vUv);
        float l = dot(c.rgb, vec3(0.2126, 0.7152, 0.0722));
        c.rgb = mix(c.rgb, c.rgb * vec3(1.04, 1.0, 0.94), smoothstep(0.2, 1.2, l));
        c.rgb = mix(vec3(l), c.rgb, 1.08);
        vec2 d = vUv - 0.5;
        c.rgb *= 1.0 - dot(d, d) * 0.55;
        gl_FragColor = c;
      }`,
  };
}

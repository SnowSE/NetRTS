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
import { Commentary } from './commentary.js';

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
const SHIELD_BLUE = new THREE.Color('#2f6bff');
const TOWER_RANGE = 5;   // GameRules: guard towers reach 5 tiles in every direction, diagonals included
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const smooth = (t) => t * t * (3 - 2 * t);
// Siege engines (models.js): the trebuchet's arm swings this far over (radians) to let its stone go, from
// rest at this angle about its axle; the arm's tip is this far out. The Snow Mobile's cannon fires from
// its muzzle (in model units, facing +z).
const ARM_UP = 2.5, ARM_REST = -2.44, ARM_AXLE = 0.36, ARM_TIP = 0.43;
const MUZZLE = { y: 0.635, z: 0.304 };
const SIEGE_COOLDOWN = 8;   // GameRules: a trebuchet fires every 8 ticks

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
    this.draw = new THREE.InstancedBufferAttribute(new Float32Array(cap * 2), 2).setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('iTeam', this.team);
    geo.setAttribute('iFlash', this.flash);
    geo.setAttribute('iCut', this.cut);
    geo.setAttribute('iWalk', this.walk);
    geo.setAttribute('iDraw', this.draw);
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
  push(matrix, colour, flash = 0, cut = NO_CUT, walk = null, draw = null) {
    if (this.n >= this.cap) this.alloc(this.cap * 2);
    const i = this.n++;
    this.mesh.setMatrixAt(i, matrix);
    this.team.setXYZ(i, colour.r, colour.g, colour.b);
    this.flash.setX(i, flash);
    this.cut.setX(i, cut);
    if (walk) this.walk.setXYZ(i, walk[0], walk[1], walk[2]);
    else this.walk.setXYZ(i, 0, 0, 0);
    this.draw.setXY(i, draw ? draw[0] : 0, draw ? draw[1] : 0);
  }
  end() {
    this.mesh.count = this.n;
    this.mesh.instanceMatrix.needsUpdate = true;
    this.team.needsUpdate = true;
    this.flash.needsUpdate = true;
    this.cut.needsUpdate = true;
    this.walk.needsUpdate = true;
    this.draw.needsUpdate = true;
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
  /**
   * container: the element to fill. Options: badger (Snow College theme), names (slot -> name).
   * Manual mode, for frame-exact rendering (the promo video): pass manual: true and the canvas to draw
   * on; nothing animates by itself, the caller sets the clock (setClock), the camera, and calls
   * renderFrame(dt) for each frame.
   */
  constructor(container, { badger = false, names = () => new Map(), manual = false, canvas = null, labels = true, quality: fixedQuality = null, audio = null } = {}) {
    this.container = container;
    this.badger = badger;
    this.names = names;
    this.manual = manual;
    this.clockMs = 0;
    this.gameMs = performance.now(); // the picture's clock: follows the wall clock, but stops while paused
    this.holdAt = null;          // paused: the clock runs on to here (the newest frame on screen), then stops
    this.range = null;           // the hovered guard tower's range outline: { id, group }
    this.unitScale = 1;          // promo close-ups draw units larger...
    // Buildings stand about twice a worker's height. Wider than this would spill into the next tile, where
    // units stand.
    this.buildingScale = 1.35;
    this.relief = 1;             // plateau height (set before setMap)
    this.timeScale = 1;          // match seconds per real second (the promo plays matches slowed down)
    this.arrowScale = 1;         // flying arrows, drawn bigger in the promo so they read on video
    this.highlight = null;       // Set of unit ids whose rings pulse
    this.map = null;
    this.state = null;
    this.vision = null;
    this.units = new Map();      // id -> animated unit
    this.buildings = new Map();  // id -> building record
    this.corpses = [];
    this.shells = new Map();     // "attackerId:firedTick" -> a siege shot the view is flying (updateShells)
    this.oreMax = new Map();
    this.lastSet = 0;
    this.interval = 300;
    this.lagS = 300;             // how far behind the newest frame the picture runs, eased
    this.queue = [];             // effects waiting for the picture to catch up
    this.hotspots = [];          // recent explosions, for the director
    this.grid = false;
    this.alive = true;
    this.onHoverTile = null;     // (tile | null, {x, y}) => void
    this.onModeChange = null;
    this.onFollowChange = null;  // (unit | null) => void

    let quality = 'medium';
    try { quality = localStorage.getItem('netrts.3d.quality') || quality; } catch { /* storage blocked */ }
    if (fixedQuality) quality = fixedQuality;
    this.quality = QUALITY[quality] ? quality : 'medium';
    this.autoQuality = { frames: 0, time: 0, done: manual || !!fixedQuality || !!this.storedQuality() };

    const renderer = new THREE.WebGLRenderer({ canvas: canvas ?? undefined, antialias: false, powerPreference: 'high-performance', alpha: false, preserveDrawingBuffer: manual });
    // With no saved choice, a weak computer starts at low quality (no post-processing to compile or
    // run); if it turns out to cope, checkQuality steps it up.
    this.autoQuality.guessed = !manual && !fixedQuality && !this.storedQuality() && QUALITY[this.quality] && guessQuality(renderer) === 'low';
    if (this.autoQuality.guessed) this.quality = 'low';
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 0.95;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.domElement.className = 'map3d-canvas';
    renderer.domElement.tabIndex = 0;
    renderer.domElement.setAttribute('role', 'img');
    renderer.domElement.setAttribute('aria-label', '3D battlefield. Drag to pan, right-drag to turn, scroll to zoom, click a unit to follow it.');
    if (!canvas) container.appendChild(renderer.domElement);
    this.renderer = renderer;

    this.labels = new CSS2DRenderer();
    this.labels.domElement.className = 'map3d-labels';
    this.labels.domElement.hidden = !labels;

    // Sound (the spectate page's GameAudio): effects placed in the scene, and the players' voices,
    // with captions of what they say.
    this.audio = audio;
    this.captions = document.createElement('div');
    this.captions.className = 'map3d-captions';
    this.captions.setAttribute('aria-live', 'polite');
    container.appendChild(this.captions);
    this.commentary = audio ? new Commentary(audio, { badger, names, onCaption: (slot, name, text, secs) => this.caption(slot, name, text, secs) }) : null;
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
    if (!manual) this.raf = requestAnimationFrame(loop);
    this.ro = new ResizeObserver(() => this.resize());
    this.ro.observe(canvas ?? container);
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
    this.showRange(null);
    this.map = map;
    this.terrain?.dispose();
    if (this.terrain) this.scene.remove(this.terrain.group);
    this.terrain = new Terrain(map, { relief: this.relief });
    this.terrain.uniforms.uGrid.value = this.grid ? 1 : 0;
    this.scene.add(this.terrain.group);
    const span = Math.max(map.width, map.height);
    this.scene.fog.near = span * 1.3;
    this.scene.fog.far = span * 4.6;
    this.effects?.dispose();
    this.effects = new Effects(this.scene, this.terrain);
    this.effects.onSound = (kind, p) => this.sound(kind, p);
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
    // Compile the scene's shaders in the background (where the browser can), so the page stays
    // responsive; nothing is drawn until they're ready, which would otherwise compile them on the spot.
    this.ready = false;
    const done = () => { if (this.terrain) { this.ready = true; this.onReady?.(); } };
    if (this.manual) done();
    else this.renderer.compileAsync(this.scene, this.camera).then(done, done);
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
    L.shield = new Instances(this.scene, this.models.shield, this.mat, this.depthMat, 16);
    L.scaffold = new Instances(this.scene, this.models.scaffold, this.mat, this.depthMat, 8);
    L.shell = new Instances(this.scene, this.models.shell, this.mat, this.depthMat, 16);
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
    // Manual mode draws at the canvas's own size and leaves its styling to the page.
    const fixed = this.manual && this.renderer.domElement.width > 1;
    const w = fixed ? this.renderer.domElement.width : Math.max(1, this.container.clientWidth);
    const h = fixed ? this.renderer.domElement.height : Math.max(1, this.container.clientHeight);
    if (!force && w === this.w && h === this.h) return;
    this.w = w; this.h = h;
    this.renderer.setSize(w, h, false);
    if (!fixed) {
      this.renderer.domElement.style.width = `${w}px`;
      this.renderer.domElement.style.height = `${h}px`;
    }
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

  /**
   * The view draws a little behind the newest frame (about two ticks), so every unit always has its next
   * position in hand and can glide there on a smooth curve instead of lurching when a tick lands early
   * or stopping when one is late. Effects are delayed by the same amount so they line up.
   */
  get lag() { return reducedMotion() ? 0 : this.lagS; }

  /** Run fn when the picture catches up with the frame arriving now (plus extra ms). */
  later(fn, extra = 0) { this.queue.push({ t: this.now() + this.lag + extra, fn }); }

  setState(s) {
    if (!this.terrain) return;
    const prev = this.state;
    const now = this.now();
    const step = prev && s.tick === prev.tick + 1;
    const same = prev && s.tick === prev.tick;
    const jump = !step && !same;
    if (step && this.holdAt == null) {
      // Pace the animation to however long ticks are really taking (live pace or replay speed).
      const gap = now - this.lastSet;
      this.interval = clamp(this.interval * 0.7 + gap * 0.3, 30, 1500);
      // The slowest recent gap, fading: the picture must stay behind even the late ticks.
      this.gapMax = Math.max(Math.min(gap, 1600), (this.gapMax ?? gap) * 0.96);
    } else if (jump) {
      this.effects.clear();
      this.corpses.length = 0;
      this.queue.length = 0;
      this.shells.clear();
    }
    if (this.range && !s.buildings.some((b) => b.id === this.range.id)) this.showRange(null);
    this.lastSet = now;
    if (this.holdAt != null) this.holdAt = now + this.lag;   // paused: play this frame out, then stop
    this.state = s;
    const fx = step && !reducedMotion();
    const q = (fn, extra = 0) => { if (fx) this.later(fn, extra); };
    const lag = this.lag;

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

    // Units: record where each one will be, appear, take hits, fall.
    const seen = new Set();
    const hurt = [];
    for (const u of s.units) {
      seen.add(u.id);
      const w = where.get(u.id);
      let a = this.units.get(u.id);
      if (!a) {
        a = { id: u.id, path: [], yaw: (u.owner * 1.7) % (Math.PI * 2), hitT: -1e9, strikeT: -1e9, bornT: step ? now : -Infinity };
        this.units.set(u.id, a);
        q(() => this.effects.spawn(this.ground(w.x, w.z), teamRGB(u.owner)));
      } else if (fx && u.hp < a.u.hp) {
        hurt.push(u);
      }
      if (jump || !a.path.length) a.path = [{ t: now, x: w.x, z: w.z }];
      else if (same) Object.assign(a.path[a.path.length - 1], { x: w.x, z: w.z });
      else {
        // Keep plenty: the stretch being drawn is a few ticks back and needs the points either side.
        a.path.push({ t: now, x: w.x, z: w.z });
        if (a.path.length > 16) a.path.shift();
      }
      a.u = u;
      a.scale = w.scale;
      a.tx = w.x; a.tz = w.z;
    }
    for (const [id, a] of this.units) {
      if (seen.has(id) || a.deathT != null) continue;
      if (jump) { this.units.delete(id); continue; }
      // It falls when the picture reaches this tick (see updateUnits).
      a.deathT = now;
      a.deathFx = fx;
    }

    // Siege shots: the view flies each one itself (updateShells), from when the picture reaches the tick
    // it was fired until the frame it has come down in. A shot is known by who fired it and when, so it's
    // only launched once however many frames it shows up in.
    const shelled = new Set();   // tiles a shot came down on in this frame
    for (const sh of s.shells || []) {
      const key = `${sh.attackerId}:${sh.firedTick}`;
      const due = now + lag + Math.max(0, sh.landsTick - s.tick) * this.interval;
      let r = this.shells.get(key);
      if (!r) {
        r = { sh, t0: now + lag - Math.max(0, s.tick - sh.firedTick) * this.interval, t1: due };
        this.shells.set(key, r);
        const a = this.units.get(sh.attackerId);
        if (a && (!a.fireT || r.t0 > a.fireT)) { a.fireT = r.t0; a.aim = { x: sh.x + 0.5, z: sh.y + 0.5, until: due }; }
      }
      if (!r.pinned) r.t1 = Math.max(r.t0 + 120, due);
    }
    for (const [key, r] of this.shells) {
      if (r.landed && r.sh.landsTick < s.tick - 1) { this.shells.delete(key); continue; }
      if (r.pinned || r.sh.landsTick > s.tick) continue;
      // It comes down in this frame: land it as the picture gets here, with the hits it does.
      r.pinned = true;
      r.t1 = Math.max(r.t0 + 120, now + lag);
      shelled.add(r.sh.y * 4096 + r.sh.x);
    }

    // Who hit whom: archers loose arrows, melee units strike, towers fire on what nothing else is hitting.
    const attackers = new Map();
    for (const u of s.units) {
      if (u.activity !== 'Attacking' || u.targetId == null) continue;
      if (!attackers.has(u.targetId)) attackers.set(u.targetId, []);
      attackers.get(u.targetId).push(u);
    }
    const flight = Math.min(0.6, this.interval * 0.0009 + 0.12);
    // Fighters swing on their own rhythm (see updateUnits); here we only need to know whether units
    // were behind a hit, or whether it came from a tower.
    const strike = (targetId) => (attackers.get(targetId) || []).length;

    // What each fighter is hitting: its target, or, attack-moving, the nearest enemy within reach.
    for (const u of s.units) {
      const a = this.units.get(u.id);
      if (u.activity !== 'Attacking') { a.tgt = null; continue; }
      const t = u.targetId != null ? where.get(u.targetId) : null;
      if (t && !t.resource) {
        a.tgt = t.unit ? { id: u.targetId, unit: true }
          : { id: u.targetId, x: t.x, z: t.z, h: this.models.building(t.building.type, t.building.owner).height * this.buildingScale };
        continue;
      }
      // Frames don't always name the target while a unit is attack-moving, and a crowd stacked on one
      // tile beside a building fights from a little further back, so look a tile beyond reach.
      const reach = (u.type === 'Archer' ? 4 : u.type === 'Trebuchet' ? 7 : 1) + 1;
      let best = null, bd = Infinity;
      for (const o of s.units) {
        if (o.owner === u.owner) continue;
        const d = Math.max(Math.abs(o.x - u.x), Math.abs(o.y - u.y));
        if (d <= reach && d < bd) { bd = d; best = { id: o.id, unit: true }; }
      }
      for (const b of s.buildings) {
        if (b.owner === u.owner) continue;
        const d = Math.max(Math.abs(b.x - u.x), Math.abs(b.y - u.y));
        if (d <= reach && d < bd) {
          bd = d;
          best = { id: b.id, x: b.x + 0.5, z: b.y + 0.5, h: this.models.building(b.type, b.owner).height * this.buildingScale };
        }
      }
      a.tgt = best;
    }
    for (const u of hurt) {
      const a = this.units.get(u.id);
      if (shelled.has(u.y * 4096 + u.x)) {
        // Caught under a siege shot: hit as it lands (updateShells), not by whoever is targeting it.
        a.hitT = now + lag + 30;
        a.hitFrom = null;
        continue;
      }
      const archer = this.anyArcher(attackers.get(u.id));
      a.hitT = now + lag + (archer ? 140 + flight * 1000 : 150);
      a.hitFrom = attackers.get(u.id)?.[0];
      const tp = this.ground(a.tx, a.tz);
      if (!strike(u.id, tp, 0.45)) this.towerFire(s, u, tp, q);
    }

    // Buildings: rise, finish, take hits, burn, explode.
    const bSeen = new Set();
    for (const b of s.buildings) {
      bSeen.add(b.id);
      const rec = this.buildings.get(b.id);
      const p = this.ground(b.x + 0.5, b.y + 0.5);
      const def = this.models.building(b.type, b.owner);
      if (!rec) {
        q(() => this.effects.dust(p, 14, [0.62, 0.55, 0.44], 0.9));
        this.buildings.set(b.id, { b, hitT: -1e9, bornT: step ? now : -Infinity, cut: b.completed ? NO_CUT : def.height * Math.max(0.04, b.constructionPercent / 100) });
        continue;
      }
      if (b.completed && !rec.b.completed) q(() => this.effects.completed(p, teamRGB(b.owner), def.height * this.buildingScale));
      if (!b.completed && b.constructionPercent > rec.b.constructionPercent) {
        rec.workT = now + lag;
        if (Math.random() < 0.5) q(() => this.effects.dust(p, 2, [0.7, 0.62, 0.5], 0.5));
      }
      if (fx && b.hp < rec.b.hp && b.completed) {
        rec.hitT = now + lag + (shelled.has(b.y * 4096 + b.x) ? 30 : 150);
        strike(b.id, p, def.height);
      }
      rec.b = b;
    }
    for (const [id, rec] of this.buildings) {
      if (bSeen.has(id) || rec.deathT != null) continue;
      if (jump) { this.buildings.delete(id); continue; }
      rec.deathT = now;
      rec.deathFx = fx;
    }
    for (const r of prev?.resources || []) {
      if (!s.resources.some((x) => x.id === r.id)) q(() => this.effects.dust(this.ground(r.x + 0.5, r.y + 0.5), 10, [0.75, 0.62, 0.3], 0.6));
    }
    this.applyVision();
    this.updateLabels();
    if (this.commentary) {
      // A live stream may skip a tick or two; a seek in a replay starts the commentary afresh.
      const close = prev && s.tick > prev.tick && s.tick - prev.tick <= 5;
      if (!close && !same) this.commentary.reset();
      this.commentary.update(close ? prev : null, s, (fn) => this.later(fn));
    }
  }

  /**
   * A sound for something happening at p: only if it's in view, quieter the further away it is, and
   * panned to where it is on screen.
   */
  sound(kind, p, vol = 1) {
    if (!this.audio?.live) return;
    const v = this._snd ??= new THREE.Vector3();
    v.set(p.x, p.y, p.z).project(this.camera);
    if (v.z > 1 || Math.abs(v.x) > 1.05 || Math.abs(v.y) > 1.05) return;
    const d = this.camera.position.distanceTo(v.set(p.x, p.y, p.z));
    const near = 7;   // tiles: anything closer plays at full volume
    this.audio.sfx(kind, vol * Math.pow(near / Math.max(d, near), 1.3), clamp(v.copy(p).project(this.camera).x * 0.75, -1, 1));
  }

  /** Show who said what, in their colour, for as long as they're talking (and a moment more). */
  caption(slot, name, text, secs) {
    if (!this.audio?.settings.captions) return;
    const line = document.createElement('p');
    line.className = 'cap';
    const who = document.createElement('b');
    who.textContent = `${name}: `;
    who.style.color = `#${teamOf(slot).getHexString()}`;
    line.append(who, document.createTextNode(text));
    this.captions.appendChild(line);
    while (this.captions.children.length > 3) this.captions.firstChild.remove();
    setTimeout(() => line.remove(), (secs + 2) * 1000);
  }

  anyArcher(list) { return (list || []).some((u) => u.type === 'Archer'); }

  /** Guard towers: the frames don't say what a tower shot, so fire at a hurt enemy in range. */
  towerFire(s, u, tp, q) {
    let best = null, bd = 1e9;
    for (const b of s.buildings) {
      if (b.type !== 'GuardTower' || !b.completed || b.owner === u.owner) continue;
      const d = Math.max(Math.abs(b.x - u.x), Math.abs(b.y - u.y));
      if (d <= TOWER_RANGE && d < bd) { best = b; bd = d; }
    }
    if (!best) return;
    const top = this.ground(best.x + 0.5, best.y + 0.5).add(new THREE.Vector3(0, this.models.building('GuardTower', best.owner).height * this.buildingScale - 0.1, 0));
    q(() => this.effects.bolt(top, tp.clone().add(new THREE.Vector3(0, 0.25, 0)), Math.min(0.45, this.interval * 0.0007 + 0.1)));
  }

  ground(x, z) { return new THREE.Vector3(x, this.terrain.heightAt(x, z), z); }

  /**
   * Where a unit is at render time rt: a Hermite curve through its recent positions with Catmull-Rom
   * tangents, so it eases into a move, flows through turns and eases out at the end. Also returns the
   * velocity, in tiles per millisecond.
   */
  unitPos(a, rt) {
    const P = a.path, n = P.length;
    const last = P[n - 1];
    if (n === 1 || rt >= last.t) return { x: last.x, z: last.z, vx: 0, vz: 0 };
    if (rt <= P[0].t) return { x: P[0].x, z: P[0].z, vx: 0, vz: 0 };
    let i = n - 2;
    while (i > 0 && P[i].t > rt) i--;
    const p0 = P[i - 1] || P[i], p1 = P[i], p2 = P[i + 1], p3 = P[i + 2] || p2;
    const span = Math.max(1, p2.t - p1.t), u = (rt - p1.t) / span;
    const d01 = p0 === p1 ? span : Math.max(1, p1.t - p0.t), d23 = p3 === p2 ? span : Math.max(1, p3.t - p2.t);
    const k1 = span / (span + d01), k2 = span / (span + d23);
    const m1x = (p2.x - p0.x) * k1, m1z = (p2.z - p0.z) * k1;
    const m2x = (p3.x - p1.x) * k2, m2z = (p3.z - p1.z) * k2;
    const u2 = u * u, u3 = u2 * u;
    const h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
    const d00 = 6 * u2 - 6 * u, d10 = 3 * u2 - 4 * u + 1, d01b = -6 * u2 + 6 * u, d11 = 3 * u2 - 2 * u;
    return {
      x: h00 * p1.x + h10 * m1x + h01 * p2.x + h11 * m2x,
      z: h00 * p1.z + h10 * m1z + h01 * p2.z + h11 * m2z,
      vx: (d00 * p1.x + d10 * m1x + d01b * p2.x + d11 * m2x) / span,
      vz: (d00 * p1.z + d10 * m1z + d01b * p2.z + d11 * m2z) / span,
    };
  }

  // ---------------------------------------------------------------- per frame

  /** The view's clock in milliseconds: the wall clock, or whatever the caller set in manual mode. */
  now() { return this.manual ? this.clockMs : this.gameMs; }

  /**
   * Pause the picture with the match (or the replay): units finish moving to the newest frame, then
   * stand still, and effects freeze mid-air. A frame that arrives while paused (stepping) plays out,
   * then the picture stops again. The camera keeps working.
   */
  setPaused(on) {
    if (on === (this.holdAt != null)) return;
    if (on) this.holdAt = this.lastSet + this.lag;
    else {
      this.holdAt = null;
      // Time stood still for the picture, so don't count the pause as a slow tick.
      this.lastSet = this.gameMs;
    }
  }

  setClock(ms) { this.clockMs = ms; }

  frame() {
    if (!this.terrain || !this.ready) {
      this.renderer.setClearColor('#9fb3c2');
      this.renderer.clear();
      return;
    }
    const dt = Math.min(0.1, this.clock.getDelta());
    let g = this.gameMs + dt * 1000;
    if (this.holdAt != null) g = Math.max(this.gameMs, Math.min(g, this.holdAt));
    const gameDt = (g - this.gameMs) / 1000;
    this.gameMs = g;
    this.renderFrame(dt, gameDt);
  }

  /**
   * Draw one frame, dt seconds after the last; gameDt is how far the picture's clock moved (less than
   * dt while paused). In manual mode the caller has placed the camera.
   */
  renderFrame(dt, gameDt = dt) {
    const now = this.now();
    // Run two ticks behind (a little more if ticks arrive unevenly), so the curve through a unit's path
    // already knows the position after the stretch it's drawing and never bends mid-stride when a tick
    // lands. Ease toward it so speed changes don't jolt the picture.
    const lagGoal = clamp(Math.max(this.interval * 2.05, (this.gapMax ?? this.interval) * 1.05 + this.interval), 60, 2600);
    this.lagS = this.manual ? lagGoal : this.lagS + (lagGoal - this.lagS) * (1 - Math.exp(-2 * gameDt));
    if (this.queue.length) {
      const due = this.queue.filter((e) => e.t <= now);
      if (due.length) {
        this.queue = this.queue.filter((e) => e.t > now);
        for (const e of due) e.fn();
      }
    }
    if (!this.manual) this.rig.update(dt, this.effects.shake);
    this.updateUnits(now, gameDt);
    this.updateShells(now);
    this.updateBuildings(now, gameDt);
    this.effects.update(gameDt);
    this.updateSun();
    this.terrain.uniforms.uTime.value += dt;
    if (this.composer) this.composer.render(dt);
    else this.renderer.render(this.scene, this.camera);
    this.labels.render(this.scene, this.camera);
    this.checkQuality(dt);
  }

  /** Shadows follow the camera, sized to what's on screen. */
  updateSun() {
    let t, reach;
    if (this.manual && this.lookAt) {
      // Manual mode: centre the shadows on what the caller's camera is looking at.
      t = this.lookAt;
      reach = clamp(this.camera.position.distanceTo(this.lookAt) * 0.9, 6, 70);
    } else {
      t = this.rig.mode === 'fly' ? this.rig.fly.pos.clone().addScaledVector(this.rig.forward(), 10) : this.rig.cur.target;
      reach = clamp((this.rig.mode === 'fly' ? 18 : this.rig.cur.dist) * 0.9, 6, 70);
    }
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
    L.shield.begin();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(0, 0, 0, 'YXZ'), v = new THREE.Vector3(), sc = new THREE.Vector3();
    const ringCol = new THREE.Color();
    let ri = 0;
    const bars = this.bars;
    bars.n = 0;
    const camPos = this.camera.position;
    const k = 1 - Math.exp(-12 * dt);
    const rt = now - this.lag;   // the moment the picture is showing
    const positions = new Map();
    for (const [id, a] of this.units) {
      // A unit missing from the newest frame falls once the picture reaches that frame.
      if (a.deathT != null && rt >= a.deathT) {
        this.units.delete(id);
        const p = this.unitPos(a, rt);
        if (a.deathFx) {
          if (this.models.units[a.u.type].vehicle) this.effects.explosion(this.ground(p.x, p.z), teamRGB(a.u.owner), false);
          else this.effects.death(this.ground(p.x, p.z), teamRGB(a.u.owner));
          const from = a.hitFrom && positions.get(a.hitFrom.id);
          this.corpses.push({ a, x: p.x, z: p.z, t0: now, away: from ? Math.atan2(p.x - from.x, p.z - from.z) : a.yaw + Math.PI });
        }
        if (this.followed === id) this.followedLost = { x: p.x, z: p.z, t: now };
        continue;
      }
      positions.set(id, this.unitPos(a, rt));
    }
    this.positions = positions;

    for (const a of this.units.values()) {
      const u = a.u;
      if (rt < a.bornT) continue;   // trained, but the picture hasn't got there yet
      if (this.vision != null && u.owner !== this.vision && !this.visibleTo(u.x, u.y)) continue;
      const p = positions.get(a.id);
      const speed = Math.hypot(p.vx, p.vz) * 1000 * this.timeScale;   // tiles per second, as seen on screen
      const moving = speed > 0.35;
      const working = !moving && (u.activity === 'Gathering' || u.activity === 'Building');
      const carrying = u.carrying > 0;
      const vehicle = this.models.units[u.type].vehicle;

      // What a fighter is hitting, where it is right now (it may be moving too).
      let tp = null;
      if (u.activity === 'Attacking' && a.tgt) {
        const tu = a.tgt.unit && positions.get(a.tgt.id);
        tp = tu ? { x: tu.x, z: tu.z, h: 0.45 * this.unitScale } : { x: a.tgt.x, z: a.tgt.z, h: a.tgt.h };
      }

      // Face where we're going, or what we're working on or hitting; glance about when idle.
      let want = a.yaw;
      if (moving) want = Math.atan2(p.vx, p.vz);
      else if (vehicle && a.aim && now < a.aim.until + 400) {
        // A siege engine turns to face the tile it's about to shell.
        if (Math.hypot(a.aim.x - p.x, a.aim.z - p.z) > 0.05) want = Math.atan2(a.aim.x - p.x, a.aim.z - p.z);
      } else if (tp) {
        if (Math.hypot(tp.x - p.x, tp.z - p.z) > 0.05) want = Math.atan2(tp.x - p.x, tp.z - p.z);
      } else if (u.targetId != null && working) {
        const tb = this.buildings.get(u.targetId)?.b;
        const tr = this.state.resources.find((r) => r.id === u.targetId);
        const tx = tb ? tb.x + 0.5 : tr ? tr.x + 0.5 : null;
        const tz = tb ? tb.y + 0.5 : tr ? tr.y + 0.5 : null;
        if (tx != null && Math.hypot(tx - p.x, tz - p.z) > 0.05) want = Math.atan2(tx - p.x, tz - p.z);
      }
      const turn = Math.atan2(Math.sin(want - a.yaw), Math.cos(want - a.yaw));
      a.yaw += turn * k;
      const idle = !moving && !working && u.activity !== 'Attacking';
      a.idleT = idle ? (a.idleT ?? 0) + dt : 0;
      const look = a.idleT > 1.5 ? Math.sin(now * 0.0006 + a.id * 2.1) * 0.5 * Math.min(1, (a.idleT - 1.5) / 1.5) : 0;

      const phase = now * 0.001 + a.id * 0.37;
      let y = this.terrain.heightAt(p.x, p.z), pitch = 0, roll = 0, fwd = 0, side = 0, squash = 1;
      let legGoal = 0, armGoal = 0, rate = 4, chop = -1;   // chop: 0..1 arms raised overhead, -1 = not chopping
      let pull = 0;                                        // archer: how far the bowstring is drawn
      const fighting = u.activity === 'Attacking' && !moving && tp;

      if (vehicle) {
        // Siege engines roll rather than walk: no stride, only a low rumble and a lean into turns on the
        // move, and a lurch when they fire (the shot itself is flown by updateShells).
        if (moving) {
          a.rumble = (a.rumble ?? 0) + dt * (8 + speed * 10);
          y += Math.abs(Math.sin(a.rumble)) * 0.005 * this.unitScale;
          pitch = Math.sin(a.rumble * 0.37) * 0.012;
          roll = -clamp(turn * 0.12, -0.08, 0.08);
        }
        const since = now - (a.fireT ?? -1e9);
        if (since >= 0 && since < 600) {
          const kick = Math.sin(Math.min(1, since / 120) * Math.PI / 2) * Math.exp(-since / 220);
          fwd = -kick * 0.035 * this.unitScale;
          pitch -= kick * 0.07;
        }
      } else if (moving) {
        // A proper walk: legs and arms swing in opposition, the body bobs on every step and sways at the
        // hips, leaning into the pace and banking into turns. The stride quickens with speed so feet
        // keep up with the ground; scouts' mounts gallop. Loads make for a heavier, shorter gait.
        const w = a.walk ?? 0;
        const scout = u.type === 'Scout';
        y += Math.abs(Math.sin(w)) * (scout ? 0.05 : carrying ? 0.028 : 0.04) * this.unitScale;
        squash = 1 - Math.abs(Math.cos(w)) * 0.03;
        pitch = scout ? Math.sin(w * 2) * 0.1 + 0.05 : clamp(0.06 + speed * 0.04, 0, 0.18) + (carrying ? 0.1 : 0);
        roll = Math.sin(w) * (scout ? 0.03 : 0.06) - clamp(turn * 0.25, -0.18, 0.18);
        side = Math.sin(w) * 0.008 * this.unitScale;
        legGoal = scout ? 0.9 : carrying ? 0.7 : 0.95;
        armGoal = carrying ? 0.2 : 0.75;
        rate = clamp(speed * (scout ? 5 : 7), 7, scout ? 40 : 34);
      } else if (working) {
        // Pickaxe or hammer: a slow wind-up, a fast blow, a beat of rest; dirt and chips fly on the blow.
        const period = u.activity === 'Building' ? 0.48 : 0.9;
        a.work = (a.work ?? Math.random()) + dt / period;
        const c = a.work % 1;
        if (c < 0.6) chop = smooth(c / 0.6);
        else if (c < 0.72) chop = 1 - (c - 0.6) / 0.12;
        else chop = 0;
        pitch = 0.08 + chop * -0.12 + (c >= 0.6 && c < 0.8 ? 0.22 : 0);
        y += c >= 0.7 && c < 0.78 ? -0.008 : 0;
        if (Math.floor(a.work - 0.72) > (a.lastBlow ?? -1e9)) {
          a.lastBlow = Math.floor(a.work - 0.72);
          const tip = { x: p.x + Math.sin(a.yaw) * 0.22, y: y + 0.02, z: p.z + Math.cos(a.yaw) * 0.22 };
          this.sound(u.activity === 'Gathering' ? 'mine' : 'hammer', tip, 0.5);
          if (u.activity === 'Gathering') {
            this.effects.dust(tip, 2, [0.58, 0.47, 0.33], 0.35);
            this.effects.spark(tip, 3, this.badger ? [1.6, 1.5, 1.2] : [2.2, 1.7, 0.6]);
          } else {
            this.effects.dust(tip, 1, [0.68, 0.6, 0.5], 0.3);
            if (Math.random() < 0.5) this.effects.spark(tip, 2);
          }
        }
      } else if (fighting) {
        // Fighting: a steady rhythm of blows at the unit's own attack rate (never so fast you can't
        // follow it). Melee units wind up, lunge and hack, with sparks where the blow lands; archers
        // draw, loose an arrow and recover; scouts' mounts rear while the rider cuts down.
        const archer = u.type === 'Archer';
        const cooldown = archer ? 2 : 1;
        const period = Math.max(archer ? 1.0 : 0.62, (cooldown * this.interval) / 1000);
        if (a.atk == null) { a.atk = (a.id * 0.618) % 1; a.lastAtk = Math.floor(a.atk - 0.5); }
        a.atk += dt / period;
        const c = a.atk % 1;
        // Between blows, weight shifts from foot to foot.
        side = Math.sin(phase * 3.2) * 0.01 * this.unitScale;
        roll = Math.sin(phase * 3.2) * 0.04;
        armGoal = 0.15;
        rate = 5;
        if (archer) {
          // Nock (a beat after the last shot), draw the string back to the cheek, hold, loose.
          pull = c < 0.12 ? 0 : c < 0.45 ? smooth((c - 0.12) / 0.33) : c < 0.5 ? 1 : 0;
          pitch -= pull * 0.06;
          fwd = (c > 0.5 && c < 0.65 ? -Math.sin(((c - 0.5) / 0.15) * Math.PI) * 0.02 : 0) * this.unitScale;   // recoil
        } else if (u.type === 'Scout') {
          const rear = c < 0.55 ? Math.sin((c / 0.55) * Math.PI) : 0;
          pitch -= rear * 0.38;
          fwd = rear * 0.05 * this.unitScale;
          y += rear * 0.03;
        } else {
          chop = c < 0.38 ? smooth(c / 0.38) : c < 0.5 ? 1 - (c - 0.38) / 0.12 : 0;
          const lunge = c < 0.38 ? -0.3 * smooth(c / 0.38) : c < 0.75 ? Math.sin(((c - 0.38) / 0.37) * Math.PI) : 0;
          fwd = lunge * 0.1 * this.unitScale;
          pitch += lunge * 0.3;
          squash = 1 - Math.max(0, lunge) * 0.04;
        }
        const blow = Math.floor(a.atk - 0.5);
        if (blow > a.lastAtk) {
          a.lastAtk = blow;
          if (archer) {
            // The arrow leaves from the drawn bow, out in front of the archer.
            const s = this.unitScale, sy = Math.sin(a.yaw), cy = Math.cos(a.yaw);
            const lx = -0.1 * s, lz = 0.3 * s;
            const from = new THREE.Vector3(p.x + cy * lx + sy * lz, y + 0.29 * s, p.z - sy * lx + cy * lz);
            const to = new THREE.Vector3(tp.x, this.terrain.heightAt(tp.x, tp.z) + tp.h * 0.6, tp.z);
            this.effects.arrow(from, to, clamp(Math.hypot(tp.x - p.x, tp.z - p.z) * 0.09, 0.2, 0.55), s * this.arrowScale);
          } else {
            const at = new THREE.Vector3(p.x, y + 0.25 * this.unitScale, p.z)
              .lerp(new THREE.Vector3(tp.x, this.terrain.heightAt(tp.x, tp.z) + tp.h * 0.5, tp.z), 0.6);
            if (a.tgt.unit) { this.effects.spark(at, 7, undefined, this.unitScale); this.sound('sword', at); }
            else {
              this.effects.chip(at, this.unitScale);
              const rec = this.buildings.get(a.tgt.id);
              if (rec) rec.hitT = now;   // the wall flashes and shudders
            }
          }
        }
      } else if (u.activity === 'Attacking') {
        // Waiting for a target in reach: a fighter's stance.
        side = Math.sin(phase * 3.2) * 0.012;
        roll = Math.sin(phase * 3.2) * 0.04;
        armGoal = 0.18;
        rate = 5;
        squash = 1 - Math.abs(Math.sin(phase * 3.2)) * 0.02;
      } else {
        squash = 1 + Math.sin(phase * 2.4) * 0.015;   // breathing
      }

      // Taking a hit: a flash and a stagger away from whoever landed it.
      const sinceHit = now - a.hitT;
      if (sinceHit >= 0 && sinceHit < 220) {
        const s = Math.sin((sinceHit / 220) * Math.PI);
        const from = a.hitFrom && positions.get(a.hitFrom.id);
        const away = from ? Math.atan2(p.x - from.x, p.z - from.z) : a.yaw + Math.PI;
        p.kx = Math.sin(away) * s * 0.06;
        p.kz = Math.cos(away) * s * 0.06;
        pitch -= s * 0.18;
      }
      const flash = Math.max(0, 1 - Math.abs(now - a.hitT) / 160);
      const born = a.bornT === -Infinity ? 1 : Math.min(1, (rt - a.bornT) / 350);
      const serum = u.effects?.includes('serum');
      const s = (this.models.units[u.type].scale || 1) * a.scale * this.unitScale * (0.4 + 0.6 * born) * (serum ? 1.5 : 1);
      const yaw = a.yaw + look;
      v.set(p.x + Math.sin(yaw) * fwd + Math.cos(yaw) * side + (p.kx || 0), y, p.z + Math.cos(yaw) * fwd - Math.sin(yaw) * side + (p.kz || 0));
      e.set(pitch, yaw, roll);
      sc.set(s, s * squash, s);
      m.compose(v, q.setFromEuler(e), sc);

      // Limbs: stride, sway, or both arms chopping together (a negative arm swing tells the shader so).
      a.leg = (a.leg ?? 0) + (legGoal - (a.leg ?? 0)) * k;
      // Archers in a fight raise the bow and keep it up between shots; both arms come up to aim.
      if (u.type === 'Archer') {
        a.raise = (a.raise ?? 0) + ((fighting ? 1 : 0) - (a.raise ?? 0)) * (1 - Math.exp(-8 * dt));
        if (a.raise > 0.02) chop = Math.max(chop, a.raise);
      }
      const draw = u.type === 'Archer' ? [a.raise ?? 0, pull] : vehicle ? this.siegePose(a, now) : null;
      if (chop >= 0) {
        a.arm = 0;
        const reachUp = u.type === 'Archer' ? -1.45 : -2.1;   // arms out level to aim, or overhead to chop
        L['u' + u.type].push(m, teamOf(u.owner), flash, NO_CUT, [Math.asin(clamp(chop * 2 - 1, -1, 1)), a.leg, reachUp], draw);
      } else {
        a.arm = (a.arm ?? 0) + (armGoal - (a.arm ?? 0)) * k;
        a.walk = (a.walk ?? a.id) + dt * rate;
        L['u' + u.type].push(m, teamOf(u.owner), flash, NO_CUT, [a.walk, a.leg, a.arm], draw);
      }
      if (serum) L.shield.push(m, teamOf(u.owner), flash);
      if (carrying) {
        const back = 0.11 * s;
        const bob = moving ? Math.abs(Math.sin(a.walk ?? 0)) * 0.015 : 0;
        v.set(v.x - Math.sin(yaw) * back, y + 0.33 * s + bob, v.z - Math.cos(yaw) * back);
        m.compose(v, q, sc.set(s, s, s));
        L.nugget.push(m, teamOf(u.owner));
      }
      if (ri < this.rings.instanceMatrix.count) {
        // Highlighted units (promo call-outs) get a bright, pulsing ring.
        const hl = this.highlight?.has(a.id) || serum;
        const rs = hl ? s * (1.3 + Math.sin(now * 0.008) * 0.15) : s;
        m.compose(v.set(p.x, this.terrain.heightAt(p.x, p.z) + 0.02, p.z), q.identity(), sc.set(rs, 1, rs));
        this.rings.setMatrixAt(ri, m);
        this.rings.setColorAt(ri, (serum ? ringCol.copy(SHIELD_BLUE) : ringCol.copy(teamOf(u.owner))).multiplyScalar(hl ? 1.8 : 1));
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
    }

    // The fallen topple over and sink away.
    this.corpses = this.corpses.filter((c) => now - c.t0 < 2600);
    for (const c of this.corpses) {
      const t = (now - c.t0) / 1000;
      const fall = Math.min(1, t / 0.35);
      const y = this.terrain.heightAt(c.x, c.z) - Math.max(0, t - 1.2) * 0.25;
      // Face the blow and topple away from it; a wrecked siege engine only slumps.
      e.set(-fall * (this.models.units[c.a.u.type].vehicle ? 0.25 : Math.PI / 2 * 0.95), c.away + Math.PI, 0);
      const s = (this.models.units[c.a.u.type].scale || 1) * (c.a.scale || 1);
      m.compose(v.set(c.x, y, c.z), q.setFromEuler(e), sc.set(s, s, s));
      L['u' + c.a.u.type].push(m, teamOf(c.a.u.owner).clone().multiplyScalar(0.5));
    }
    for (const type of Object.keys(this.models.units)) L['u' + type].end();
    L.nugget.end();
    L.shield.end();
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

  /**
   * A siege engine's moving parts (iDraw, see models.js) a moment after it fired at a.fireT. The
   * trebuchet's arm swings over to let the stone go right at the shot's launch, quivers at the top, then
   * is winched back down before the next shot: [arm angle, 1 while the stone is away]. The Snow Mobile's
   * cannon kicks back and runs out again: [recoil, 0].
   */
  siegePose(a, now) {
    const since = now - (a.fireT ?? -1e9);
    if (this.badger) return [since < 0 || since > 1200 ? 0 : since < 40 ? since / 40 : Math.exp(-(since - 40) / 220), 0];
    const reload = Math.max(400, SIEGE_COOLDOWN * this.interval);
    const swing = Math.min(320, reload * 0.15), hold = Math.min(450, reload * 0.2), back = reload * 0.85;
    const t = since + swing;   // the arm starts over before the stone lets go
    if (t < 0 || t >= back) return [0, 0];
    if (t < swing) return [ARM_UP * (t / swing) ** 2, 0];
    if (t < swing + hold) {
      const w = (t - swing) / hold;
      return [ARM_UP + Math.sin(w * Math.PI * 3) * 0.12 * (1 - w), 1];
    }
    return [ARM_UP * (1 - smooth((t - swing - hold) / (back - swing - hold))), 1];
  }

  /** Where a siege shot leaves from: the arm's tip or the cannon's muzzle, or its tile if the engine is gone. */
  muzzle(sh) {
    const a = this.units.get(sh.attackerId), p = a && this.positions?.get(a.id);
    if (!p) return this.ground(sh.fromX + 0.5, sh.fromY + 0.5).add(new THREE.Vector3(0, 0.6, 0));
    const s = (this.models.units[a.u.type].scale || 1) * (a.scale || 1) * this.unitScale;
    const ang = ARM_REST - ARM_UP;
    const ly = this.badger ? MUZZLE.y : ARM_AXLE + Math.sin(ang) * ARM_TIP, lz = this.badger ? MUZZLE.z : Math.cos(ang) * ARM_TIP;
    return new THREE.Vector3(p.x + Math.sin(a.yaw) * lz * s, this.terrain.heightAt(p.x, p.z) + ly * s, p.z + Math.cos(a.yaw) * lz * s);
  }

  /**
   * Siege shots in flight: each leaves its engine when the picture reaches the tick it was fired and
   * drops on its tile in a high arc, landing as the picture reaches the frame it came down in. The tile
   * it's falling on is marked in the owner's colour, so you can watch units scramble out of the way.
   */
  updateShells(now) {
    const L = this.layers.shell;
    L.begin();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), v = new THREE.Vector3(), sc = new THREE.Vector3();
    const white = new THREE.Color('#ffffff'), ringCol = new THREE.Color(), spinAxis = new THREE.Vector3(1, 0, 0.4).normalize();
    let ri = this.rings.count;
    for (const r of this.shells.values()) {
      const sh = r.sh;
      if (r.landed || now < r.t0) continue;
      if (!r.from) {
        r.from = this.muzzle(sh);
        r.to = this.ground(sh.x + 0.5, sh.y + 0.5);
        // Only make a show of the launch if the picture is there for it (not on joining mid-flight).
        if (now - r.t0 < 300) {
          const d = new THREE.Vector3(r.to.x - r.from.x, 0, r.to.z - r.from.z).normalize();
          this.effects.shellLaunch(r.from, this.badger, d.multiplyScalar(Math.cos(0.4)).setY(Math.sin(0.4)));
        }
      }
      const t = clamp((now - r.t0) / Math.max(1, r.t1 - r.t0), 0, 1);
      if (t >= 1) {
        r.landed = true;
        this.effects.shellImpact(r.to, this.badger, this.unitScale);
        this.hotspots.push({ x: r.to.x, z: r.to.z, t: now, size: 6 });
        continue;
      }
      if (this.vision != null && sh.owner !== this.vision && !this.visibleTo(sh.x, sh.y) && !this.visibleTo(sh.fromX, sh.fromY)) continue;
      const dist = Math.hypot(r.to.x - r.from.x, r.to.z - r.from.z);
      const arc = 0.9 + dist * 0.32;
      v.lerpVectors(r.from, r.to, t);
      v.y += 4 * arc * t * (1 - t);
      m.compose(v, q.setFromAxisAngle(spinAxis, t * 9 + sh.firedTick), sc.setScalar(this.unitScale * this.arrowScale));
      L.push(m, white);
      this.effects.shellTrail(v, this.badger);
      // The mark on the target tile tightens as the shot comes down.
      if (ri < this.rings.instanceMatrix.count) {
        const rs = (2.6 - 1.4 * t) * (1 + Math.sin(now * 0.02) * 0.06);
        m.compose(v.set(r.to.x, r.to.y + 0.025, r.to.z), q.identity(), sc.set(rs, 1, rs));
        this.rings.setMatrixAt(ri, m);
        this.rings.setColorAt(ri, ringCol.copy(teamOf(sh.owner)).multiplyScalar(0.6 + t));
        ri++;
      }
    }
    // Forget shots that never came down (the frames that would have landed them were skipped).
    for (const [key, r] of this.shells) if (!r.landed && now > r.t1 + 5000) this.shells.delete(key);
    L.end();
    this.rings.count = ri;
    this.rings.instanceMatrix.needsUpdate = true;
    if (this.rings.instanceColor) this.rings.instanceColor.needsUpdate = true;
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
    const rt = now - this.lag;
    for (const [id, rec] of this.buildings) {
      // Buildings appear and fall when the picture reaches the frame that says so.
      if (rec.deathT != null && rt >= rec.deathT) {
        this.buildings.delete(id);
        if (rec.deathFx) {
          const p = this.ground(rec.b.x + 0.5, rec.b.y + 0.5);
          const big = rec.b.type === 'CommandCenter' || rec.b.completed;
          this.effects.explosion(p, teamRGB(rec.b.owner), big);
          this.hotspots.push({ x: p.x, z: p.z, t: now, size: big ? 12 : 6 });
        }
        continue;
      }
      if (rt < rec.bornT) continue;
      const b = rec.b;
      const def = this.models.building(b.type, b.owner);
      const x = b.x + 0.5, z = b.y + 0.5;
      const y = this.terrain.heightAt(x, z);
      // Buildings face the middle of the map, so their fronts are on show.
      const yaw = Math.round(Math.atan2(this.terrain.W / 2 - x, this.terrain.H / 2 - z) / (Math.PI / 2)) * (Math.PI / 2);
      q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), yaw);
      const goal = b.completed ? NO_CUT : def.height * Math.max(0.04, b.constructionPercent / 100);
      rec.cut = goal === NO_CUT || rec.cut === NO_CUT ? goal : rec.cut + (goal - rec.cut) * k;
      const hit = Math.max(0, 1 - Math.abs(now - rec.hitT) / 120);
      const flash = hit * 0.07;
      // A blow makes the building shudder.
      const sh = hit * 0.012 * this.buildingScale;
      m.compose(v.set(x + Math.sin(now * 0.09) * sh, y, z + Math.cos(now * 0.11) * sh), q, sc.setScalar(this.buildingScale));
      this.buildingLayer(def).push(m, teamOf(b.owner), flash, rec.cut);
      if (this.badger && b.type === 'SiegeWorkshop' && b.completed && Math.random() < dt * 2.5) {
        // The welding shop at work: sparks spill from the open bay door.
        const lx = 0.18 * this.buildingScale, lz = 0.32 * this.buildingScale, c = Math.cos(yaw), sn = Math.sin(yaw);
        this.effects.spark(new THREE.Vector3(x + lx * c + lz * sn, y + 0.14 * this.buildingScale, z - lx * sn + lz * c), 4, [2.4, 2.3, 2.1], 0.45);
      }
      if (!b.completed) L.scaffold.push(m, white);
      if (b.completed && b.hp < b.maxHp) {
        const hurt = 1 - b.hp / b.maxHp;
        if (hurt > 0.35) this.effects.burning(v.set(x, y + def.height * this.buildingScale * 0.4, z), clamp((hurt - 0.35) / 0.5, 0, 1), dt);
        const d = camPos.distanceTo(v.set(x, y, z));
        const op = clamp(1.8 - d / 30, 0, 1);
        if (op > 0 && bars.n < bars.cap) {
          bars.pos.setXYZ(bars.n, x, y + def.height * this.buildingScale + 0.18, z);
          bars.data.setXYZ(bars.n, b.hp / b.maxHp, 0.7, op);
          bars.n++;
        }
      }
    }
    for (const r of this.state?.resources || []) {
      if (r.remaining <= 0) continue;
      const frac = Math.min(1, r.remaining / (this.oreMax.get(r.id) || r.remaining));
      const s = (0.55 + 0.6 * frac) * Math.sqrt(this.buildingScale);
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
      obj.position.set(x, this.terrain.heightAt(x, z) + this.models.building('CommandCenter', slot).height * this.buildingScale + 0.45, z);
    }
  }

  // ---------------------------------------------------------------- camera helpers for the page

  /** Centre of the biggest fight right now, or a fresh explosion. */
  hotspot() {
    const now = this.now();
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
        if (lost && this.now() - lost.t < 2500) return new THREE.Vector3(lost.x, this.terrain.heightAt(lost.x, lost.z), lost.z);
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
    const hit = clientX == null || !this.map ? null : this.rig.groundAt(clientX, clientY);
    const x = hit ? Math.floor(hit.x) : -1, y = hit ? Math.floor(hit.z) : -1;
    const inside = hit && x >= 0 && y >= 0 && x < this.map.width && y < this.map.height;
    this.showRange(clientX == null ? null : this.towerAt(clientX, clientY, inside ? { x, y } : null));
    if (!this.onHoverTile) return;
    if (!inside) { this.onHoverTile(null); return; }
    const r = this.renderer.domElement.getBoundingClientRect();
    this.onHoverTile({ x, y }, { x: clientX - r.left, y: clientY - r.top });
  }

  /** The guard tower under the cursor: on the hovered tile, or whose tower is drawn under the cursor. */
  towerAt(clientX, clientY, tile) {
    const towers = (this.state?.buildings || []).filter((b) => b.type === 'GuardTower'
      && (this.vision == null || b.owner === this.vision || this.visibleTo(b.x, b.y)));
    const onTile = tile && towers.find((b) => b.x === tile.x && b.y === tile.y);
    if (onTile) return onTile;
    const r = this.renderer.domElement.getBoundingClientRect();
    const v = new THREE.Vector3();
    let best = null, bd = 28;
    for (const b of towers) {
      const h = this.models.building('GuardTower', b.owner).height * this.buildingScale;
      for (const up of [0.3, 0.65, 0.95]) {
        v.copy(this.ground(b.x + 0.5, b.y + 0.5)).setY(this.terrain.heightAt(b.x + 0.5, b.y + 0.5) + h * up).project(this.camera);
        if (v.z > 1) continue;
        const d = Math.hypot(r.left + (v.x + 1) / 2 * r.width - clientX, r.top + (1 - v.y) / 2 * r.height - clientY);
        if (d < bd) { bd = d; best = b; }
      }
    }
    return best;
  }

  /**
   * Outline what a guard tower can hit: a square, since its reach counts diagonal steps like straight
   * ones. Draped over the ground in the owner's colour, faint, with a fainter wash inside.
   */
  showRange(b) {
    if (this.range?.id === (b?.id ?? null)) return;
    if (this.range) {
      this.scene.remove(this.range.group);
      this.range.group.traverse((o) => { o.geometry?.dispose(); o.material?.dispose(); });
      this.range = null;
    }
    if (!b || !this.terrain) return;
    const x0 = Math.max(0, b.x - TOWER_RANGE), z0 = Math.max(0, b.y - TOWER_RANGE);
    const x1 = Math.min(this.map.width, b.x + TOWER_RANGE + 1), z1 = Math.min(this.map.height, b.y + TOWER_RANGE + 1);
    const lift = 0.05;
    const col = teamOf(b.owner);
    const group = new THREE.Group();

    // The wash: a grid of quads, four to a tile, following the ground.
    const nx = (x1 - x0) * 4, nz = (z1 - z0) * 4;
    const fill = new THREE.PlaneGeometry(x1 - x0, z1 - z0, nx, nz).rotateX(-Math.PI / 2).translate((x0 + x1) / 2, 0, (z0 + z1) / 2);
    const fp = fill.attributes.position;
    for (let i = 0; i < fp.count; i++) fp.setY(i, this.terrain.heightAt(fp.getX(i), fp.getZ(i)) + lift);
    group.add(new THREE.Mesh(fill, new THREE.MeshBasicMaterial({ color: col, transparent: true, opacity: 0.07, depthWrite: false })));

    // The edge: a narrow ribbon around the square, inset so it sits inside the outermost tiles.
    const w = 0.07, pts = [];
    const side = (ax, az, bx, bz) => {
      const n = Math.max(1, Math.round(Math.hypot(bx - ax, bz - az) * 4));
      for (let i = 0; i < n; i++) pts.push([ax + (bx - ax) * i / n, az + (bz - az) * i / n]);
    };
    side(x0, z0, x1, z0); side(x1, z0, x1, z1); side(x1, z1, x0, z1); side(x0, z1, x0, z0);
    const cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
    const pos = [], idx = [];
    for (const [px, pz] of pts) {
      // Step the inner edge toward the centre along both axes, so corners stay square.
      const ix = px + Math.sign(cx - px) * w, iz = pz + Math.sign(cz - pz) * w;
      pos.push(px, this.terrain.heightAt(px, pz) + lift + 0.01, pz, ix, this.terrain.heightAt(ix, iz) + lift + 0.01, iz);
    }
    for (let i = 0; i < pts.length; i++) {
      const a = i * 2, c = ((i + 1) % pts.length) * 2;
      idx.push(a, c, a + 1, a + 1, c, c + 1);
    }
    const edge = new THREE.BufferGeometry();
    edge.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    edge.setIndex(idx);
    group.add(new THREE.Mesh(edge, new THREE.MeshBasicMaterial({ color: col, transparent: true, opacity: 0.55, depthWrite: false, side: THREE.DoubleSide })));

    group.children.forEach((m) => { m.renderOrder = 2; });
    this.scene.add(group);
    this.range = { id: b.id, group };
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
    if (a.guessed && this.quality === 'low' && fps >= 55) {
      this.quality = 'medium';
      this.applyQuality();
      this.onQualityChange?.(this.quality);
    } else if (fps < 28 && this.quality !== 'low') {
      this.quality = this.quality === 'high' ? 'medium' : 'low';
      this.applyQuality();
      this.onQualityChange?.(this.quality);
    }
  }

  destroy() {
    this.alive = false;
    this.showRange(null);
    this.captions.remove();
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

/**
 * A first guess at what this computer can manage, before a frame has been drawn: software rendering,
 * four or fewer cores, 4 GB of memory or less, older Intel graphics, or a phone or tablet start low.
 */
function guessQuality(renderer) {
  let gpu = '';
  try {
    const gl = renderer.getContext();
    const info = gl.getExtension('WEBGL_debug_renderer_info');
    gpu = String(info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER));
  } catch { /* no details */ }
  const cores = navigator.hardwareConcurrency || 4;
  const memory = navigator.deviceMemory || 8;
  const touch = window.matchMedia?.('(pointer: coarse)').matches && Math.min(screen.width, screen.height) < 900;
  if (/swiftshader|llvmpipe|softpipe|basic render/i.test(gpu)) return 'low';
  if (/intel/i.test(gpu) && !/arc|iris\(?r?\)? xe/i.test(gpu)) return 'low';
  if (cores <= 4 || memory <= 4 || touch) return 'low';
  return 'medium';
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

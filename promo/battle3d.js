// Cinematic 3D view of a recorded Badger Brawl match: the Sanpete Valley at night, low-poly badgers,
// team rings, attack bolts and floating info tags. Everything is a pure function of (match, tick, camera)
// so any frame can be rendered on demand.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { hqName, housingName, BUILDING_NAME, UPGRADE_NAME } from '/src/NetRts.Server/wwwroot/lore.js';

export const TEAM = ['#f08a3c', '#56b4e9', '#2fc497', '#e68fc3', '#ff6b5e', '#b39ddb', '#d4d45a', '#4dd0e1',
  '#bcaaa4', '#f48fb1', '#9fa8da', '#aed581', '#ffab40', '#80cbc4', '#ce93d8', '#b0bec5'];

const hash = (n) => { n = (n ^ 61) ^ (n >>> 16); n = n + (n << 3); n ^= n >>> 4; n = Math.imul(n, 0x27d4eb2d); n ^= n >>> 15; return (n >>> 0) / 4294967296; };
const lerp = (a, b, t) => a + (b - a) * t;
const UNIT_SCALE = 1.75;
const smooth = (t) => t * t * (3 - 2 * t);

/** Index a frame's entities by id (cached on the frame). */
function byId(f) {
  if (!f._u) {
    f._u = new Map(f.units.map((u) => [u.id, u]));
    f._b = new Map(f.buildings.map((b) => [b.id, b]));
  }
  return f;
}

/** Fixed per-unit jitter inside its tile so units sharing a tile don't overlap. */
const jitter = (id) => ({ x: (hash(id * 7 + 1) - 0.5) * 0.5, z: (hash(id * 13 + 5) - 0.5) * 0.5 });

export function frameAt(match, tick) {
  const fr = match.frames;
  const k = Math.max(0, Math.min(fr.length - 1, Math.floor(tick)));
  return { k, a: byId(fr[k]), b: byId(fr[Math.min(fr.length - 1, k + 1)]), f: Math.min(1, Math.max(0, tick - k)) };
}

/** Interpolated world position of a unit at a fractional tick, or null. */
export function unitPos(match, id, tick) {
  const { a, b, f } = frameAt(match, tick);
  const ua = a._u.get(id), ub = b._u.get(id);
  const u0 = ua || ub;
  if (!u0) return null;
  const u1 = ub || ua;
  const j = jitter(id);
  const t = smooth(f);
  return { x: lerp(u0.x, u1.x, t) + 0.5 + j.x, z: lerp(u0.y, u1.y, t) + 0.5 + j.z, u: f < 0.5 || !ub ? u0 : u1, dying: !ub && f > 0 };
}

export class Battle3D {
  constructor(canvas, tagLayer) {
    this.canvas = canvas;
    this.tagLayer = tagLayer;
    const r = this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
    r.setPixelRatio(1);
    r.setSize(canvas.width, canvas.height, false);
    r.shadowMap.enabled = true;
    r.shadowMap.type = THREE.PCFSoftShadowMap;
    r.toneMapping = THREE.ACESFilmicToneMapping;
    r.toneMappingExposure = 1.35;
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(38, canvas.width / canvas.height, 0.1, 600);
    const target = new THREE.WebGLRenderTarget(canvas.width, canvas.height, { samples: 4, type: THREE.HalfFloatType });
    this.composer = new EffectComposer(r, target);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.bloom = new UnrealBloomPass(new THREE.Vector2(canvas.width, canvas.height), 0.85, 0.5, 0.82);
    this.composer.addPass(this.bloom);
    this.composer.addPass(new OutputPass());
    this.buildSky();
    this.lights();
    this.worlds = new Map();
    this.units = new Map();     // id -> mesh group (pooled per world)
    this.buildings = new Map();
    this.fx = [];
    this.tags = new Map();
  }

  buildSky() {
    const c = document.createElement('canvas');
    c.width = 16; c.height = 512;
    const g = c.getContext('2d');
    const grad = g.createLinearGradient(0, 0, 0, 512);
    grad.addColorStop(0, '#02050a');
    grad.addColorStop(0.55, '#0a1626');
    grad.addColorStop(0.78, '#1c2f4a');
    grad.addColorStop(1, '#2a3550');
    g.fillStyle = grad; g.fillRect(0, 0, 16, 512);
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    this.scene.background = tex;
    this.scene.fog = new THREE.FogExp2('#16263f', 0.009);
    // Stars over Ephraim.
    const pts = [];
    for (let i = 0; i < 1600; i++) {
      const th = hash(i * 3 + 1) * Math.PI * 2, ph = 0.12 + hash(i * 5 + 2) * 1.2;
      const R = 400;
      pts.push(Math.cos(th) * Math.cos(ph) * R, Math.sin(ph) * R, Math.sin(th) * Math.cos(ph) * R);
    }
    const sg = new THREE.BufferGeometry();
    sg.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    this.stars = new THREE.Points(sg, new THREE.PointsMaterial({ color: '#cfe0ff', size: 1.3, sizeAttenuation: false, fog: false, transparent: true, opacity: 0.85 }));
    this.scene.add(this.stars);
  }

  lights() {
    this.scene.add(new THREE.HemisphereLight('#8fb0e8', '#3a2c1c', 1.9));
    const moon = this.moon = new THREE.DirectionalLight('#d6e4ff', 2.9);
    moon.castShadow = true;
    moon.shadow.mapSize.set(2048, 2048);
    moon.shadow.bias = -0.0008;
    moon.shadow.normalBias = 0.02;
    const sc = moon.shadow.camera;
    sc.left = -30; sc.right = 30; sc.top = 30; sc.bottom = -30; sc.near = 1; sc.far = 200;
    this.scene.add(moon, moon.target);
    const rim = new THREE.DirectionalLight('#ff9a52', 1.1);
    rim.position.set(-60, 25, -40);
    this.scene.add(rim);
  }

  /** Terrain, rocks, sagebrush and distant mountains for one match map. */
  world(match) {
    let w = this.worlds.get(match);
    if (w) return w;
    const { width: W, height: H, terrain } = match.map;
    const group = new THREE.Group();
    // Valley floor: a painted texture with a faint grid.
    const S = 16;
    const c = document.createElement('canvas');
    c.width = W * S; c.height = H * S;
    const g = c.getContext('2d');
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const n = hash(y * 977 + x * 31);
      g.fillStyle = `hsl(${80 + n * 30}, ${16 + n * 8}%, ${17 + n * 5}%)`;
      g.fillRect(x * S, y * S, S, S);
    }
    g.strokeStyle = 'rgba(160,200,255,0.07)';
    g.lineWidth = 1;
    for (let i = 0; i <= W; i++) { g.beginPath(); g.moveTo(i * S + 0.5, 0); g.lineTo(i * S + 0.5, H * S); g.stroke(); }
    for (let i = 0; i <= H; i++) { g.beginPath(); g.moveTo(0, i * S + 0.5); g.lineTo(W * S, i * S + 0.5); g.stroke(); }
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 8;
    const floor = new THREE.Mesh(new THREE.PlaneGeometry(W, H), new THREE.MeshStandardMaterial({ map: tex, roughness: 0.95 }));
    floor.rotation.x = -Math.PI / 2;
    floor.position.set(W / 2, 0, H / 2);
    floor.receiveShadow = true;
    group.add(floor);
    const outer = new THREE.Mesh(new THREE.PlaneGeometry(900, 900), new THREE.MeshStandardMaterial({ color: '#141b16', roughness: 1 }));
    outer.rotation.x = -Math.PI / 2;
    outer.position.set(W / 2, -0.02, H / 2);
    outer.receiveShadow = true;
    group.add(outer);

    // Wasatch Plateau sandstone outcrops.
    const rocks = [];
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (terrain[y][x] === '#') rocks.push([x, y]);
    const rockGeo = new THREE.BoxGeometry(1, 1, 1);
    const rockMat = new THREE.MeshStandardMaterial({ color: '#6e6456', roughness: 0.85, flatShading: true });
    const rock = new THREE.InstancedMesh(rockGeo, rockMat, rocks.length);
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), col = new THREE.Color();
    rocks.forEach(([x, y], i) => {
      const h = 0.7 + hash(x * 131 + y * 17) * 1.9;
      q.setFromEuler(new THREE.Euler(0, (hash(x + y * 999) - 0.5) * 0.25, 0));
      m.compose(new THREE.Vector3(x + 0.5, h / 2, y + 0.5), q, new THREE.Vector3(1.02, h, 1.02));
      rock.setMatrixAt(i, m);
      rock.setColorAt(i, col.set('#6e6456').offsetHSL(0, 0, (hash(x * 7 + y) - 0.5) * 0.12));
    });
    rock.castShadow = rock.receiveShadow = true;
    group.add(rock);

    // Sagebrush.
    const brushes = [];
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (terrain[y][x] !== '#' && hash(x * 53 + y * 7919) < 0.07) brushes.push([x, y]);
    const brush = new THREE.InstancedMesh(new THREE.IcosahedronGeometry(0.16, 0), new THREE.MeshStandardMaterial({ color: '#4f5d44', roughness: 1, flatShading: true }), brushes.length);
    brushes.forEach(([x, y], i) => {
      const s = 0.7 + hash(x + y * 3) * 0.9;
      m.compose(new THREE.Vector3(x + hash(x * 3 + y), 0.08 * s, y + hash(y * 5 + x)), q.identity(), new THREE.Vector3(s, s * 0.7, s));
      brush.setMatrixAt(i, m);
    });
    brush.castShadow = true;
    group.add(brush);

    // Distant ridges: the mountains around the Sanpete Valley.
    const ridge = new THREE.Group();
    const ridgeMat = new THREE.MeshStandardMaterial({ color: '#1a2333', roughness: 1, flatShading: true });
    for (let i = 0; i < 70; i++) {
      const th = (i / 70) * Math.PI * 2;
      const R = Math.max(W, H) * 0.9 + 40 + hash(i * 11) * 40;
      const h = 18 + hash(i * 7) * 34;
      const cone = new THREE.Mesh(new THREE.ConeGeometry(18 + hash(i) * 16, h, 5), ridgeMat);
      cone.position.set(W / 2 + Math.cos(th) * R, h / 2 - 2, H / 2 + Math.sin(th) * R);
      cone.rotation.y = hash(i * 3) * 3;
      ridge.add(cone);
    }
    group.add(ridge);

    group.visible = false;
    this.scene.add(group);
    w = { group, units: new Map(), buildings: new Map(), deposits: new Map(), bolts: [] };
    this.worlds.set(match, w);
    return w;
  }

  // ---------- meshes

  material(color, extra = {}) {
    return new THREE.MeshStandardMaterial({ color, roughness: 0.55, metalness: 0.1, flatShading: true, ...extra });
  }

  makeUnit(u) {
    const team = new THREE.Color(TEAM[u.owner % 16]);
    const g = new THREE.Group();
    const body = this.material(team);
    const dark = this.material('#2b2f36');
    const white = this.material('#c9cfd6');
    const parts = new THREE.Group();
    g.add(parts);
    const head = (y, r) => {
      const h = new THREE.Mesh(new THREE.SphereGeometry(r, 10, 8), dark);
      h.position.set(0, y, 0.02);
      const stripe = new THREE.Mesh(new THREE.BoxGeometry(r * 0.38, r * 0.3, r * 1.9), white);
      stripe.position.set(0, y + r * 0.82, 0.02);
      const snout = new THREE.Mesh(new THREE.ConeGeometry(r * 0.45, r * 0.9, 6), white);
      snout.rotation.x = Math.PI / 2;
      snout.position.set(0, y - r * 0.1, r * 1.05);
      parts.add(h, stripe, snout);
    };
    if (u.type === 'Worker') {
      const b = new THREE.Mesh(new THREE.CapsuleGeometry(0.15, 0.14, 4, 8), body);
      b.position.y = 0.26; parts.add(b);
      head(0.55, 0.12);
      const pick = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.04, 0.42), this.material('#c9b38a'));
      pick.position.set(0.2, 0.36, 0.08); pick.rotation.x = -0.6; parts.add(pick);
      const sack = new THREE.Mesh(new THREE.SphereGeometry(0.11, 8, 6), this.material('#f0b860', { emissive: '#7a4a10', emissiveIntensity: 0.7 }));
      sack.position.set(0, 0.42, -0.18); sack.name = 'sack'; parts.add(sack);
    } else if (u.type === 'Soldier') {
      const b = new THREE.Mesh(new THREE.CapsuleGeometry(0.19, 0.2, 4, 8), body);
      b.position.y = 0.32; parts.add(b);
      head(0.7, 0.14);
      const shield = new THREE.Mesh(new THREE.CylinderGeometry(0.17, 0.17, 0.05, 12), this.material(team.clone().multiplyScalar(0.6)));
      shield.rotation.z = Math.PI / 2; shield.position.set(-0.22, 0.36, 0.06); parts.add(shield);
      const sword = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.5, 0.06), this.material('#dfe6ee', { metalness: 0.8, roughness: 0.25 }));
      sword.position.set(0.22, 0.48, 0.12); sword.rotation.x = 0.5; sword.name = 'weapon'; parts.add(sword);
    } else if (u.type === 'Archer') {
      const b = new THREE.Mesh(new THREE.ConeGeometry(0.2, 0.55, 8), body);
      b.position.y = 0.28; parts.add(b);
      head(0.66, 0.12);
      const bow = new THREE.Mesh(new THREE.TorusGeometry(0.22, 0.02, 4, 12, Math.PI), this.material('#c08a4a'));
      bow.rotation.y = Math.PI / 2; bow.rotation.z = Math.PI / 2; bow.position.set(0.18, 0.42, 0.1); bow.name = 'weapon'; parts.add(bow);
    } else {
      const b = new THREE.Mesh(new THREE.OctahedronGeometry(0.2, 0), body);
      b.position.y = 0.42; b.scale.set(1, 1.3, 1); parts.add(b);
      head(0.78, 0.11);
      const tail = new THREE.Mesh(new THREE.ConeGeometry(0.06, 0.4, 5), this.material('#ffffff', { emissive: team, emissiveIntensity: 0.6 }));
      tail.rotation.x = -Math.PI / 2.4; tail.position.set(0, 0.42, -0.3); parts.add(tail);
    }
    parts.traverse((o) => { if (o.isMesh) o.castShadow = true; });
    const ring = new THREE.Mesh(new THREE.RingGeometry(0.3, 0.37, 28), new THREE.MeshBasicMaterial({ color: team, transparent: true, opacity: 0.9, fog: true }));
    ring.rotation.x = -Math.PI / 2; ring.position.y = 0.025; ring.name = 'ring';
    g.add(ring);
    g.userData = { parts, type: u.type };
    return g;
  }

  makeBuilding(b) {
    const team = new THREE.Color(TEAM[b.owner % 16]);
    const g = new THREE.Group();
    const inner = new THREE.Group();
    g.add(inner);
    const stone = this.material('#b9ab92');
    const brick = this.material('#8b5a3c');
    const roofMat = this.material(team.clone().multiplyScalar(0.75));
    const glow = (c, i = 2.2) => new THREE.MeshStandardMaterial({ color: c, emissive: c, emissiveIntensity: i });
    const box = (w, h, d, mat, x = 0, y = h / 2, z = 0) => { const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat); m.position.set(x, y, z); inner.add(m); return m; };
    const windows = (w, h, d, rows, cols, y0) => {
      const wm = glow('#ffc879', 1.6);
      for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) {
        if (hash(b.id * 31 + r * 7 + c) < 0.25) continue;
        const x = -w / 2 + (c + 0.5) * (w / cols);
        const win = new THREE.Mesh(new THREE.BoxGeometry(w / cols * 0.45, 0.1, 0.02), wm);
        win.position.set(x, y0 + r * 0.22, d / 2 + 0.005);
        inner.add(win);
        const back = win.clone(); back.position.z = -d / 2 - 0.005; inner.add(back);
      }
    };
    if (b.type === 'CommandCenter') {
      box(1.7, 1.25, 1.5, brick);
      box(1.8, 0.12, 1.6, stone, 0, 1.31);
      windows(1.7, 1.25, 1.5, 4, 5, 0.28);
      const roof = new THREE.Mesh(new THREE.ConeGeometry(1.15, 0.6, 4), roofMat);
      roof.position.y = 1.67; roof.rotation.y = Math.PI / 4; inner.add(roof);
      box(0.06, 1.1, 0.06, this.material('#d9dde3'), 0, 2.4);
      const flag = new THREE.Mesh(new THREE.BoxGeometry(0.55, 0.3, 0.02), glow(team, 0.9));
      flag.position.set(0.3, 2.78, 0); flag.name = 'flag'; inner.add(flag);
      const beacon = new THREE.Mesh(new THREE.SphereGeometry(0.07, 8, 6), glow(team, 4));
      beacon.position.y = 2.98; inner.add(beacon);
    } else if (b.type === 'Barracks') {
      box(1.35, 0.95, 1.0, this.material('#c7c0b2'));
      box(1.42, 0.1, 1.08, roofMat, 0, 1.0);
      windows(1.35, 0.95, 1.0, 3, 6, 0.22);
    } else if (b.type === 'ResourceDepot') {
      box(1.2, 0.6, 1.0, this.material('#7a5a3a'));
      const awn = box(1.3, 0.06, 0.4, roofMat, 0, 0.55, 0.62); awn.rotation.x = 0.35;
      box(0.35, 0.3, 0.35, this.material('#f0b860', { emissive: '#7a4a10', emissiveIntensity: 0.6 }), 0.45, 0.15, 0.7);
    } else if (b.type === 'TechLab') {
      box(1.1, 0.7, 1.1, this.material('#cfd6dc'));
      const dome = new THREE.Mesh(new THREE.SphereGeometry(0.42, 16, 8, 0, Math.PI * 2, 0, Math.PI / 2), glow('#5fd3ff', 1.4));
      dome.position.y = 0.7; inner.add(dome);
      box(1.16, 0.08, 1.16, roofMat, 0, 0.72);
    } else {
      const t = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.38, 2.0, 8), stone);
      t.position.y = 1.0; inner.add(t);
      box(0.75, 0.18, 0.75, roofMat, 0, 2.05);
      const lamp = new THREE.Mesh(new THREE.SphereGeometry(0.14, 10, 8), glow(team, 4));
      lamp.position.y = 2.3; inner.add(lamp);
    }
    inner.traverse((o) => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
    const pad = new THREE.Mesh(new THREE.RingGeometry(0.95, 1.05, 4, 1), new THREE.MeshBasicMaterial({ color: team, transparent: true, opacity: 0.7 }));
    pad.rotation.x = -Math.PI / 2; pad.rotation.z = Math.PI / 4; pad.position.y = 0.02;
    g.add(pad);
    g.userData = { inner };
    return g;
  }

  makeDeposit() {
    const g = new THREE.Group();
    const mound = new THREE.Mesh(new THREE.SphereGeometry(0.48, 10, 6, 0, Math.PI * 2, 0, Math.PI / 2), this.material('#5b3f22', { roughness: 1 }));
    mound.scale.y = 0.55; mound.receiveShadow = true; mound.castShadow = true;
    g.add(mound);
    const grubMat = new THREE.MeshStandardMaterial({ color: '#ffd27a', emissive: '#f0a030', emissiveIntensity: 1.8 });
    for (let i = 0; i < 7; i++) {
      const s = new THREE.Mesh(new THREE.CapsuleGeometry(0.035, 0.09, 2, 5), grubMat);
      const a = i * 2.4, r = 0.12 + (i % 3) * 0.1;
      s.position.set(Math.cos(a) * r, 0.2 + (i % 2) * 0.04, Math.sin(a) * r);
      s.rotation.set(1.2, a, 0.4);
      g.add(s);
    }
    return g;
  }

  // ---------- frame

  /**
   * opts: { match, tick, cam: {pos:[x,y,z], look:[x,y,z], fov}, tags: [{id, kind:'unit'|'building', label?, accent?}],
   *         tagScale, highlight: Set of ids (pulsing rings), dimOthers }
   */
  render(opts) {
    const { match, tick } = opts;
    for (const [m, w] of this.worlds) w.group.visible = m === match;
    const w = this.world(match);
    w.group.visible = true;
    const { a, b, f } = frameAt(match, tick);
    const t = tick;

    // Units.
    const seen = new Set();
    const pos = new Map();
    const ids = new Set([...a._u.keys(), ...(f > 0 ? b._u.keys() : [])]);
    for (const id of ids) {
      const p = unitPos(match, id, tick);
      if (!p) continue;
      const born = !a._u.has(id);
      let mesh = w.units.get(id);
      if (!mesh) { mesh = this.makeUnit(p.u); w.units.set(id, mesh); w.group.add(mesh); }
      seen.add(id);
      mesh.visible = true;
      // Facing: from movement over a couple of ticks, else toward the target.
      const ahead = unitPos(match, id, tick + 1.2) || p, behind = unitPos(match, id, tick - 0.8) || p;
      let dx = ahead.x - behind.x, dz = ahead.z - behind.z;
      const moving = Math.hypot(dx, dz) > 0.15;
      const u = p.u;
      if (!moving && u.targetId != null) {
        const tp = unitPos(match, u.targetId, tick) || (a._b.get(u.targetId) && { x: a._b.get(u.targetId).x + 0.5, z: a._b.get(u.targetId).y + 0.5 });
        if (tp) { dx = tp.x - p.x; dz = tp.z - p.z; }
      }
      if (Math.hypot(dx, dz) > 0.01) mesh.userData.yaw = Math.atan2(dx, dz);
      const yaw = mesh.userData.yaw ?? hash(id) * 6;
      const bob = moving ? Math.abs(Math.sin(t * 9 + id)) * 0.07 : 0;
      mesh.position.set(p.x, 0, p.z);
      const parts = mesh.userData.parts;
      parts.rotation.y = yaw;
      parts.position.y = bob + (u.type === 'Scout' ? 0.08 + Math.sin(t * 4 + id) * 0.04 : 0);
      const weapon = parts.getObjectByName('weapon');
      if (weapon) weapon.rotation.x = u.activity === 'Attacking' ? 0.5 + Math.sin(t * Math.PI * 2 + id) * 0.9 : 0.5;
      const sack = parts.getObjectByName('sack');
      if (sack) sack.visible = (u.carrying ?? 0) > 0;
      let s = 1;
      if (p.dying) s = Math.max(0.001, 1 - f * 1.4);
      if (born) s = Math.max(0.001, f);
      mesh.scale.setScalar(s * UNIT_SCALE);
      const ring = mesh.getObjectByName('ring');
      const hl = opts.highlight?.has(id);
      ring.scale.setScalar(hl ? 1.25 + Math.sin(t * 8) * 0.15 : 1);
      ring.material.opacity = hl ? 1 : 0.75;
      pos.set(id, { x: p.x, z: p.z, u, dying: p.dying });
      if (p.dying && f > 0.05) this.puff(w, p.x, p.z, f, TEAM[u.owner % 16], id);
    }
    for (const [id, mesh] of w.units) if (!seen.has(id)) mesh.visible = false;

    // Buildings.
    const bseen = new Set();
    for (const bd of a.buildings) {
      let mesh = w.buildings.get(bd.id);
      if (!mesh) { mesh = this.makeBuilding(bd); w.buildings.set(bd.id, mesh); w.group.add(mesh); }
      bseen.add(bd.id);
      mesh.visible = true;
      mesh.position.set(bd.x + 0.5, 0, bd.y + 0.5);
      const nb = b._b.get(bd.id);
      const pct = lerp(bd.constructionPercent ?? 100, nb?.constructionPercent ?? 100, f) / 100;
      const inner = mesh.userData.inner;
      inner.scale.set(1, Math.max(0.08, bd.completed ? 1 : pct), 1);
      const destroyed = !nb && f > 0;
      if (destroyed) { inner.scale.y *= Math.max(0.05, 1 - f); this.puff(w, bd.x + 0.5, bd.y + 0.5, f, TEAM[bd.owner % 16], bd.id, 2.2); }
      const flag = inner.getObjectByName('flag');
      if (flag) flag.rotation.y = Math.sin(t * 2.3 + bd.id) * 0.25;
    }
    for (const [id, mesh] of w.buildings) if (!bseen.has(id)) mesh.visible = false;

    // Grub patches.
    const dseen = new Set();
    for (const r of a.resources) {
      let mesh = w.deposits.get(r.id);
      if (!mesh) { mesh = this.makeDeposit(); w.deposits.set(r.id, mesh); w.group.add(mesh); }
      dseen.add(r.id);
      mesh.visible = true;
      mesh.position.set(r.x + 0.5, 0, r.y + 0.5);
      const s = 0.55 + Math.min(1, r.remaining / 1500) * 0.6;
      mesh.scale.setScalar(s);
    }
    for (const [id, mesh] of w.deposits) if (!dseen.has(id)) mesh.visible = false;

    // Attack bolts: a glowing shot travels from each attacker to its target every tick.
    let bi = 0;
    const boltAt = () => {
      let m = w.bolts[bi++];
      if (!m) {
        m = new THREE.Mesh(new THREE.SphereGeometry(0.075, 8, 6), new THREE.MeshBasicMaterial({ color: '#ffffff' }));
        const trail = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 1, 5, 1, true), new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, opacity: 0.35, blending: THREE.AdditiveBlending, depthWrite: false }));
        trail.name = 'trail';
        m.add(trail);
        w.bolts.push(m); w.group.add(m);
      }
      m.visible = true;
      return m;
    };
    for (const [id, p] of pos) {
      const u = p.u;
      if (u.activity !== 'Attacking' || u.targetId == null || p.dying) continue;
      let tp = pos.get(u.targetId);
      if (!tp) { const tb = a._b.get(u.targetId); if (tb) tp = { x: tb.x + 0.5, z: tb.y + 0.5, building: true }; }
      if (!tp) continue;
      const dist = Math.hypot(tp.x - p.x, tp.z - p.z);
      if (dist > 6.5) continue;
      const phase = (f + hash(id) * 0.6) % 1;
      const col = new THREE.Color(TEAM[u.owner % 16]).multiplyScalar(3);
      const m = boltAt();
      m.material.color.copy(col);
      const h0 = u.type === 'Archer' ? 0.6 : 0.45;
      const arc = u.type === 'Archer' ? Math.sin(phase * Math.PI) * dist * 0.18 : 0;
      m.position.set(lerp(p.x, tp.x, phase), h0 + arc + (tp.building ? phase * 0.4 : 0), lerp(p.z, tp.z, phase));
      const trail = m.getObjectByName('trail');
      trail.material.color.copy(col);
      const len = Math.min(dist * phase, 1.2);
      trail.scale.set(1, Math.max(0.01, len), 1);
      trail.rotation.set(0, 0, 0);
      const dir = new THREE.Vector3(tp.x - p.x, 0, tp.z - p.z).normalize();
      trail.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.clone().negate());
      trail.position.copy(dir.clone().multiplyScalar(-len / 2));
    }
    for (let i = bi; i < w.bolts.length; i++) w.bolts[i].visible = false;
    this.fxEnd(w);

    // Camera.
    const cam = this.camera;
    const c = opts.cam;
    cam.fov = c.fov ?? 38;
    cam.position.set(...c.pos);
    cam.up.set(0, 1, 0);
    cam.lookAt(new THREE.Vector3(...c.look));
    if (c.roll) cam.rotateZ(c.roll);
    cam.updateProjectionMatrix();
    this.moon.position.set(c.look[0] + 25, 45, c.look[2] + 18);
    this.moon.target.position.set(c.look[0], 0, c.look[2]);
    this.stars.position.copy(cam.position);
    this.bloom.strength = opts.bloom ?? 0.85;
    this.composer.render();
    this.renderTags(opts, pos, a, b, f);
  }

  puff(w, x, z, f, color, id, size = 1) {
    w.puffs ??= [];
    w.pi ??= 0;
    let m = w.puffs[w.pi];
    if (!m) {
      m = new THREE.Mesh(new THREE.IcosahedronGeometry(0.5, 1), new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
      w.puffs.push(m); w.group.add(m);
    }
    w.pi++;
    m.visible = true;
    m.material.color.set(color).multiplyScalar(2.5);
    m.material.opacity = Math.max(0, 0.9 * (1 - f));
    m.scale.setScalar((0.3 + f * 1.4) * size);
    m.position.set(x, 0.4 * size, z);
  }

  fxEnd(w) {
    if (!w.puffs) return;
    for (let i = w.pi; i < w.puffs.length; i++) w.puffs[i].visible = false;
    w.pi = 0;
  }

  // ---------- info tags

  project(x, y, z) {
    const v = new THREE.Vector3(x, y, z).project(this.camera);
    const W = this.canvas.clientWidth || this.canvas.width, H = this.canvas.clientHeight || this.canvas.height;
    return { x: (v.x + 1) / 2 * W, y: (1 - v.y) / 2 * H, behind: v.z > 1, dist: this.camera.position.distanceTo(new THREE.Vector3(x, y, z)) };
  }

  renderTags(opts, pos, a, b, f) {
    const want = new Map();
    for (const tg of opts.tags || []) want.set(`${tg.kind || 'unit'}:${tg.id}`, tg);
    for (const [key, el] of this.tags) if (!want.has(key)) { el.remove(); this.tags.delete(key); }
    for (const [key, tg] of want) {
      let info = null;
      if ((tg.kind || 'unit') === 'unit') {
        const p = pos.get(tg.id);
        if (!p) { this.tags.get(key)?.remove(); this.tags.delete(key); continue; }
        const u = p.u;
        const act = u.activity === 'Attacking' ? (u.targetId == null ? 'ATTACK-MOVE' : `ATTACK → #${u.targetId}`) : u.activity === 'Gathering' ? (u.carrying ? `HAULING ${u.carrying} GRUBS` : 'DIGGING GRUBS') : u.activity === 'Building' ? 'BUILDING' : u.activity === 'Moving' ? 'MOVING' : 'IDLE';
        const top = { Worker: 0.85, Soldier: 1.0, Archer: 0.95, Scout: 1.05 }[u.type];
        info = { x: p.x, y: top * UNIT_SCALE, z: p.z, owner: u.owner, title: (tg.label ?? u.type).toUpperCase(), id: u.id, hp: u.hp, maxHp: u.maxHp, line: `(${u.x}, ${u.y})`, act: tg.act ?? act, dying: p.dying };
      } else {
        const bd = a._b.get(tg.id);
        if (!bd) { this.tags.get(key)?.remove(); this.tags.delete(key); continue; }
        const name = bd.type === 'CommandCenter' ? hqName(bd.owner) : bd.type === 'Barracks' ? housingName(bd.owner) : BUILDING_NAME[bd.type];
        const kind = { CommandCenter: 'HQ', Barracks: 'HOUSING', ResourceDepot: 'DROP-OFF', TechLab: 'RESEARCH', GuardTower: 'DEFENCE' }[bd.type];
        const h = { CommandCenter: 3.2, Barracks: 1.3, ResourceDepot: 0.9, TechLab: 1.2, GuardTower: 2.6 }[bd.type];
        info = { x: bd.x + 0.5, y: h, z: bd.y + 0.5, owner: bd.owner, title: (tg.label ?? name).toUpperCase(), id: bd.id, hp: bd.hp, maxHp: bd.maxHp, line: `${bd.type} · (${bd.x}, ${bd.y})`, act: tg.act ?? kind };
      }
      const s = this.project(info.x, info.y, info.z);
      let el = this.tags.get(key);
      if (!el) {
        el = document.createElement('div');
        el.className = 'tag';
        el.innerHTML = '<div class="tag-card"><div class="tag-h"><b></b><i></i></div><div class="tag-l"></div><div class="tag-hp"><span></span></div><div class="tag-a"></div></div><div class="tag-stem"></div>';
        this.tagLayer.appendChild(el);
        this.tags.set(key, el);
      }
      const col = TEAM[info.owner % 16];
      el.style.setProperty('--tc', tg.accent || col);
      el.querySelector('b').textContent = info.title;
      el.querySelector('i').textContent = `#${info.id}`;
      el.querySelector('.tag-l').textContent = info.line;
      el.querySelector('.tag-a').textContent = info.act;
      const hp = el.querySelector('.tag-hp span');
      hp.style.width = `${Math.max(0, info.hp / info.maxHp) * 100}%`;
      hp.dataset.v = `${info.hp}/${info.maxHp}`;
      hp.parentElement.dataset.v = `HP ${info.hp}/${info.maxHp}`;
      hp.style.background = info.hp / info.maxHp > 0.5 ? '#5fd3a8' : '#ff8a7a';
      const scale = Math.max(0.55, Math.min(1.25, (opts.tagScale ?? 1) * 15 / s.dist));
      const fade = (tg.alpha ?? 1) * (info.dying ? 0.4 : 1);
      el.style.opacity = s.behind ? 0 : fade;
      el.style.transform = `translate(${s.x}px, ${s.y}px) scale(${scale})`;
    }
  }

  hideTags() { for (const el of this.tags.values()) el.remove(); this.tags.clear(); }
}

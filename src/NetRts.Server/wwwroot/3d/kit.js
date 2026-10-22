// 3D match view: the kit models are built from. A Builder collects coloured primitives and merges them
// into one geometry with per-vertex colour, plus `team` (1 where the player's colour shows through),
// `glow` (self-lit windows, crystals, lanterns) and `limb` (parts that swing as units move).
//
// One tile is one world unit; models stand on y = 0 and face +z.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

export const col = (hex) => new THREE.Color(hex);
export const K = {
  skin: col('#e3b48b'), trousers: col('#3d3429'), leather: col('#76522f'), belt: col('#4a3424'),
  steel: col('#a9b1ba'), steelDark: col('#6f7780'), brightSteel: col('#dfe4ea'), gold: col('#e3b341'),
  wood: col('#8a6440'), woodDark: col('#5b4029'), white: col('#e6e0d4'), black: col('#1c1c1e'),
  brick: col('#a5523b'), brickTan: col('#c7a47a'), stone: col('#a59c8c'), stoneDark: col('#857c6e'),
  slate: col('#4b5058'), pane: col('#56708a'), window: col('#ffcf7a'), glass: col('#8fdcff'), hat: col('#f2b632'),
  badgerGrey: col('#8d9196'), deer: col('#8e6440'), deerBelly: col('#cfb08a'), team: col('#f4f4f4'),
  teamShade: col('#b8b8b8'), ore: col('#ffc94a'), grub: col('#f3e0b5'), soil: col('#6b5039'),
  bell: col('#d9a640'), ember: col('#ff8a2a'), red: col('#ff3b30'),
};

/** Collects coloured parts and merges them into one geometry. */
export class Builder {
  constructor() { this.parts = []; }

  add(geo, colour, { team = 0, glow = 0, limb = 0, joint = 0, at = [0, 0, 0], rot = [0, 0, 0], scale = [1, 1, 1] } = {}) {
    const g = geo.index ? geo.toNonIndexed() : geo.clone();
    g.deleteAttribute('uv');
    const m = new THREE.Matrix4().compose(
      new THREE.Vector3(...at),
      new THREE.Quaternion().setFromEuler(new THREE.Euler(...rot)),
      new THREE.Vector3(...scale));
    g.applyMatrix4(m);
    const n = g.attributes.position.count;
    const c = new Float32Array(n * 3), t = new Float32Array(n), w = new Float32Array(n), l = new Float32Array(n * 2);
    for (let i = 0; i < n; i++) { c[i * 3] = colour.r; c[i * 3 + 1] = colour.g; c[i * 3 + 2] = colour.b; t[i] = team; w[i] = glow; l[i * 2] = limb; l[i * 2 + 1] = joint; }
    g.setAttribute('color', new THREE.BufferAttribute(c, 3));
    g.setAttribute('team', new THREE.BufferAttribute(t, 1));
    g.setAttribute('glow', new THREE.BufferAttribute(w, 1));
    // Limbs swing in the vertex shader: x is the kind (±1 legs from the hip, ±2 arms from the shoulder,
    // ±3 a mount's legs), y where along z the joint is: 0 under a body's middle, a mount's legs at the
    // front or back of its body. One vec2, because the unit shader is at WebGL's 16-attribute limit.
    g.setAttribute('limb', new THREE.BufferAttribute(l, 2));
    this.parts.push(g);
    return this;
  }

  build() {
    const g = mergeGeometries(this.parts);
    g.computeVertexNormals();
    g.computeBoundingBox();
    g.computeBoundingSphere();
    return g;
  }
}

export const box = (w, h, d) => new THREE.BoxGeometry(w, h, d);
export const cyl = (rt, rb, h, s = 8) => new THREE.CylinderGeometry(rt, rb, h, s);
export const ball = (r, d = 1) => new THREE.IcosahedronGeometry(r, d);
export const cone = (r, h, s = 8) => new THREE.ConeGeometry(r, h, s);
export const dome = (r, ws = 10, hs = 5) => new THREE.SphereGeometry(r, ws, hs, 0, Math.PI * 2, 0, Math.PI / 2);
/** A triangular prism (gable, pediment): apex up, depth along z. */
export const prism = (w, h, d) => cyl(0.5, 0.5, d, 3).rotateX(-Math.PI / 2).scale(w / 0.866, h / 0.75, 1).translate(0, -h / 6, 0);

export function foundation(b, w = 0.92, d = 0.92) {
  b.add(box(w, 0.4, d), K.stoneDark, { at: [0, -0.14, 0] })
    .add(box(w - 0.04, 0.03, d - 0.04), K.stone, { at: [0, 0.07, 0] });
}

export function windows(b, w, d, rows, cols, y0, dy, sides = 4, seed = 1) {
  const faces = [[0, d / 2 + 0.006, 0], [0, -d / 2 - 0.006, Math.PI], [w / 2 + 0.006, 0, Math.PI / 2], [-w / 2 - 0.006, 0, -Math.PI / 2]];
  faces.slice(0, sides).forEach(([fx, fz, ry], fi) => {
    const span = fi < 2 ? w : d;
    const n = fi < 2 ? cols : Math.max(1, cols - 1);
    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < n; c++) {
        const off = (c - (n - 1) / 2) * (span / (n + 0.4));
        const lit = ((r * 7 + c * 13 + fi * 5 + seed) % 4) === 0;   // by day most windows show the sky
        const x = fi < 2 ? fx + off : fx, z = fi < 2 ? fz : fz + off;
        b.add(box(0.055, 0.075, 0.01), lit ? K.window : K.pane, { glow: lit ? 0.55 : 0, at: [x, y0 + r * dy, z], rot: [0, ry, 0] });
        b.add(box(0.07, 0.008, 0.02), K.white, { at: [x, y0 + r * dy - 0.042, z], rot: [0, ry, 0] });   // sill
      }
    }
  });
}

export function flag(b, x, y, z, h = 0.3) {
  b.add(cyl(0.008, 0.008, h, 4), K.brightSteel, { at: [x, y + h / 2, z] })
    .add(box(0.005, 0.08, 0.13), K.team, { team: 1, at: [x, y + h - 0.05, z + 0.068] })
    .add(ball(0.013, 0), K.gold, { at: [x, y + h + 0.01, z] });
}

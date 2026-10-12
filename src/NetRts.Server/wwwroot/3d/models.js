// 3D match view: low-poly models built in code, so there are no art files to load. Every model is one
// merged geometry with per-vertex colour plus two extra attributes: `team` (1 where the player's colour
// shows through) and `glow` (self-lit windows, crystals, lanterns). Units and buildings are drawn as
// instanced meshes with per-instance team colour, hit flash and construction height.
//
// One tile is one world unit; models stand on y = 0 and face +z.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

const col = (hex) => new THREE.Color(hex);
const K = {
  skin: col('#e3b48b'), trousers: col('#3d3429'), leather: col('#76522f'), belt: col('#4a3424'),
  steel: col('#a9b1ba'), steelDark: col('#6f7780'), brightSteel: col('#dfe4ea'), gold: col('#e3b341'),
  wood: col('#8a6440'), woodDark: col('#5b4029'), white: col('#e6e0d4'), black: col('#1c1c1e'),
  brick: col('#a5523b'), brickTan: col('#c7a47a'), stone: col('#a59c8c'), stoneDark: col('#857c6e'),
  slate: col('#4b5058'), window: col('#ffcf7a'), glass: col('#8fdcff'), hat: col('#f2b632'),
  badgerGrey: col('#8d9196'), deer: col('#8e6440'), deerBelly: col('#cfb08a'), team: col('#f4f4f4'),
  teamShade: col('#b8b8b8'), ore: col('#ffc94a'), grub: col('#f3e0b5'), soil: col('#6b5039'),
  bell: col('#d9a640'), ember: col('#ff8a2a'), red: col('#ff3b30'),
};

/** Collects coloured parts and merges them into one geometry. */
class Builder {
  constructor() { this.parts = []; }

  add(geo, colour, { team = 0, glow = 0, limb = 0, at = [0, 0, 0], rot = [0, 0, 0], scale = [1, 1, 1] } = {}) {
    const g = geo.index ? geo.toNonIndexed() : geo.clone();
    g.deleteAttribute('uv');
    const m = new THREE.Matrix4().compose(
      new THREE.Vector3(...at),
      new THREE.Quaternion().setFromEuler(new THREE.Euler(...rot)),
      new THREE.Vector3(...scale));
    g.applyMatrix4(m);
    const n = g.attributes.position.count;
    const c = new Float32Array(n * 3), t = new Float32Array(n), w = new Float32Array(n), l = new Float32Array(n);
    for (let i = 0; i < n; i++) { c[i * 3] = colour.r; c[i * 3 + 1] = colour.g; c[i * 3 + 2] = colour.b; t[i] = team; w[i] = glow; l[i] = limb; }
    g.setAttribute('color', new THREE.BufferAttribute(c, 3));
    g.setAttribute('team', new THREE.BufferAttribute(t, 1));
    g.setAttribute('glow', new THREE.BufferAttribute(w, 1));
    // Limbs swing in the vertex shader: ±1 legs (hip pivot), ±2 arms (shoulder), ±3 a mount's legs.
    g.setAttribute('limb', new THREE.BufferAttribute(l, 1));
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

const box = (w, h, d) => new THREE.BoxGeometry(w, h, d);
const cyl = (rt, rb, h, s = 8) => new THREE.CylinderGeometry(rt, rb, h, s);
const ball = (r, d = 1) => new THREE.IcosahedronGeometry(r, d);
const cone = (r, h, s = 8) => new THREE.ConeGeometry(r, h, s);
const dome = (r, ws = 10, hs = 5) => new THREE.SphereGeometry(r, ws, hs, 0, Math.PI * 2, 0, Math.PI / 2);
/** A triangular prism (gable, pediment): apex up, depth along z. */
const prism = (w, h, d) => cyl(0.5, 0.5, d, 3).rotateX(-Math.PI / 2).scale(w / 0.866, h / 0.75, 1).translate(0, -h / 6, 0);

// ---------------------------------------------------------------- units

function head(b, badger, y = 0.34) {
  if (badger) {
    // White face, a black stripe through each eye, grey cheeks, small dark ears.
    b.add(ball(0.072, 1), K.white, { at: [0, y, 0.005], scale: [1, 0.92, 1.12] })
      .add(box(0.024, 0.04, 0.13), K.black, { at: [-0.032, y + 0.028, 0.02] })
      .add(box(0.024, 0.04, 0.13), K.black, { at: [0.032, y + 0.028, 0.02] })
      .add(ball(0.05, 0), K.badgerGrey, { at: [-0.05, y - 0.02, -0.005], scale: [0.7, 0.9, 1.1] })
      .add(ball(0.05, 0), K.badgerGrey, { at: [0.05, y - 0.02, -0.005], scale: [0.7, 0.9, 1.1] })
      .add(cone(0.036, 0.07, 6), K.white, { at: [0, y - 0.012, 0.09], rot: [Math.PI / 2, 0, 0] })
      .add(ball(0.016, 0), K.black, { at: [0, y - 0.012, 0.128] })
      .add(ball(0.02, 0), K.black, { at: [-0.05, y + 0.06, -0.02] })
      .add(ball(0.02, 0), K.black, { at: [0.05, y + 0.06, -0.02] });
  } else {
    b.add(ball(0.068, 1), K.skin, { at: [0, y, 0] })
      .add(box(0.014, 0.016, 0.01), K.black, { at: [-0.025, y + 0.01, 0.064] })
      .add(box(0.014, 0.016, 0.01), K.black, { at: [0.025, y + 0.01, 0.064] });
  }
}

function body(b, { legs = true, arms = true, y = 0, animate = true } = {}) {
  const L = (side, kind) => (animate ? side * kind : 0);
  if (legs) {
    b.add(box(0.06, 0.13, 0.07), K.trousers, { limb: L(1, 1), at: [-0.045, y + 0.065, 0] })
      .add(box(0.06, 0.13, 0.07), K.trousers, { limb: L(-1, 1), at: [0.045, y + 0.065, 0] })
      .add(box(0.064, 0.03, 0.09), K.leather, { limb: L(1, 1), at: [-0.045, y + 0.015, 0.01] })
      .add(box(0.064, 0.03, 0.09), K.leather, { limb: L(-1, 1), at: [0.045, y + 0.015, 0.01] });
  }
  b.add(cyl(0.074, 0.09, 0.16, 8), K.team, { team: 1, at: [0, y + 0.21, 0] })
    .add(cyl(0.093, 0.093, 0.024, 8), K.belt, { at: [0, y + 0.142, 0] })
    .add(box(0.018, 0.018, 0.01), K.gold, { at: [0, y + 0.142, 0.092] });
  if (arms) {
    b.add(box(0.045, 0.13, 0.05), K.teamShade, { team: 1, limb: L(1, 2), at: [-0.106, y + 0.215, 0], rot: [0, 0, 0.12] })
      .add(box(0.045, 0.13, 0.05), K.teamShade, { team: 1, limb: L(-1, 2), at: [0.106, y + 0.215, 0], rot: [0, 0, -0.12] })
      .add(ball(0.026, 0), K.skin, { limb: L(1, 2), at: [-0.118, y + 0.142, 0.005] })
      .add(ball(0.026, 0), K.skin, { limb: L(-1, 2), at: [0.118, y + 0.142, 0.005] });
  }
}

function worker(badger) {
  const b = new Builder();
  body(b);
  head(b, badger);
  const hy = badger ? 0.395 : 0.37;
  b.add(dome(0.078), K.hat, { at: [0, hy, 0] })
    .add(cyl(0.098, 0.098, 0.012, 10), K.hat, { at: [0, hy + 0.003, 0.01] })
    .add(box(0.11, 0.12, 0.06), K.leather, { at: [0, 0.22, -0.088] })
    .add(cyl(0.009, 0.009, 0.28, 5), K.wood, { at: [0.03, 0.27, -0.13], rot: [0, 0, 0.7] })
    .add(box(0.17, 0.022, 0.024), K.steel, { at: [0.115, 0.37, -0.13], rot: [0, 0, 0.7 - Math.PI / 2 + 0.25] });
  return b.build();
}

function soldier(badger) {
  const b = new Builder();
  body(b);
  head(b, badger);
  const hy = badger ? 0.385 : 0.365;
  b.add(dome(0.08, 10, 5), K.steel, { at: [0, hy, 0] })
    .add(cyl(0.083, 0.083, 0.02, 10), K.steelDark, { at: [0, hy, 0] })
    .add(box(0.022, 0.05, 0.11), K.team, { team: 1, at: [0, hy + 0.09, -0.01] })
    .add(ball(0.04, 0), K.steel, { at: [-0.1, 0.285, 0], scale: [1.1, 0.7, 1] })
    .add(ball(0.04, 0), K.steel, { at: [0.1, 0.285, 0], scale: [1.1, 0.7, 1] })
    // Shield on the left arm: player colours with a steel rim and boss.
    .add(cyl(0.115, 0.115, 0.022, 12), K.steelDark, { at: [-0.14, 0.2, 0.04], rot: [Math.PI / 2, 0, 0], scale: [1, 1, 1.15] })
    .add(cyl(0.1, 0.1, 0.026, 12), K.team, { team: 1, at: [-0.14, 0.2, 0.043], rot: [Math.PI / 2, 0, 0], scale: [1, 1, 1.15] })
    .add(ball(0.026, 0), K.brightSteel, { at: [-0.14, 0.2, 0.06] })
    // Sword in the right hand.
    .add(box(0.02, 0.25, 0.008), K.brightSteel, { limb: -2, at: [0.125, 0.27, 0.06], rot: [0.35, 0, 0] })
    .add(box(0.075, 0.016, 0.022), K.gold, { limb: -2, at: [0.125, 0.15, 0.02], rot: [0.35, 0, 0] })
    .add(cyl(0.01, 0.01, 0.05, 5), K.leather, { limb: -2, at: [0.122, 0.125, 0.01], rot: [0.35, 0, 0] });
  return b.build();
}

function archer(badger) {
  const b = new Builder();
  body(b);
  head(b, badger);
  const hy = badger ? 0.4 : 0.38;
  b.add(cone(0.088, 0.14, 8), K.teamShade, { team: 1, at: [0, hy + 0.02, -0.012] })
    .add(box(0.15, 0.2, 0.012), K.teamShade, { team: 1, at: [0, 0.22, -0.085], rot: [0.12, 0, 0] })
    // Bow in the left hand, string pulled to the body.
    .add(new THREE.TorusGeometry(0.15, 0.011, 4, 14, Math.PI * 0.86), K.woodDark, { at: [-0.13, 0.22, -0.07], rot: [0, -Math.PI / 2, -Math.PI * 0.43] })
    .add(cyl(0.003, 0.003, 0.29, 3), K.white, { at: [-0.13, 0.22, -0.048] })
    // Quiver with fletched arrows.
    .add(cyl(0.032, 0.026, 0.17, 6), K.leather, { at: [0.05, 0.26, -0.1], rot: [0.25, 0, -0.35] })
    .add(box(0.05, 0.04, 0.02), K.team, { team: 1, at: [0.085, 0.36, -0.125], rot: [0.25, 0, -0.35] });
  return b.build();
}

function scout(badger) {
  // A rider on a mount: a mule deer for the Badgers, a horse otherwise.
  const b = new Builder();
  const hide = badger ? K.deer : col('#6b4a33');
  const legs = [[-0.045, -0.11], [0.045, -0.11], [-0.045, 0.11], [0.045, 0.11]];
  b.add(box(0.12, 0.12, 0.3), hide, { at: [0, 0.21, 0] })
    .add(box(0.1, 0.03, 0.26), badger ? K.deerBelly : col('#5a3d2a'), { at: [0, 0.148, 0] });
  for (const [x, z] of legs) b.add(box(0.032, 0.15, 0.034), K.woodDark, { limb: (x < 0 ? 3 : -3) * (z > 0 ? 1 : -1), at: [x, 0.075, z] });
  b.add(box(0.06, 0.14, 0.07), hide, { at: [0, 0.3, 0.15], rot: [-0.55, 0, 0] })
    .add(box(0.058, 0.062, 0.12), hide, { at: [0, 0.36, 0.215], rot: [0.15, 0, 0] })
    .add(ball(0.012, 0), K.black, { at: [0, 0.35, 0.278] })
    .add(box(0.02, 0.04, 0.012), hide, { at: [-0.03, 0.405, 0.18] })
    .add(box(0.02, 0.04, 0.012), hide, { at: [0.03, 0.405, 0.18] })
    .add(box(0.03, 0.06, 0.03), badger ? K.white : K.woodDark, { at: [0, 0.24, -0.16], rot: [0.6, 0, 0] })
    .add(box(0.13, 0.025, 0.13), K.team, { team: 1, at: [0, 0.282, -0.01] });
  if (badger) {
    // Antlers.
    for (const s of [-1, 1]) {
      b.add(cyl(0.006, 0.008, 0.09, 4), K.white, { at: [s * 0.03, 0.43, 0.2], rot: [0, 0, s * -0.5] })
        .add(cyl(0.005, 0.006, 0.05, 4), K.white, { at: [s * 0.055, 0.475, 0.21], rot: [0.5, 0, s * -0.2] });
    }
  } else {
    b.add(box(0.02, 0.1, 0.1), K.black, { at: [0, 0.39, 0.13], rot: [-0.55, 0, 0] });
  }
  // The rider sits on the saddle.
  const r = new Builder();
  body(r, { legs: false, animate: false });
  head(r, badger);
  r.add(box(0.16, 0.2, 0.012), K.teamShade, { team: 1, at: [0, 0.2, -0.08], rot: [0.35, 0, 0] })
    .add(cyl(0.08, 0.08, 0.03, 8), K.teamShade, { team: 1, at: [0, 0.29, 0] });
  const rider = r.build().scale(0.8, 0.8, 0.8).translate(0, 0.17, -0.02);
  const all = mergeGeometries([b.build(), rider]);
  all.computeBoundingSphere();
  return all;
}

// ---------------------------------------------------------------- buildings

function foundation(b, w = 0.92, d = 0.92) {
  b.add(box(w, 0.4, d), K.stoneDark, { at: [0, -0.14, 0] })
    .add(box(w - 0.04, 0.03, d - 0.04), K.stone, { at: [0, 0.07, 0] });
}

function windows(b, w, d, rows, cols, y0, dy, sides = 4, seed = 1) {
  const faces = [[0, d / 2 + 0.006, 0], [0, -d / 2 - 0.006, Math.PI], [w / 2 + 0.006, 0, Math.PI / 2], [-w / 2 - 0.006, 0, -Math.PI / 2]];
  faces.slice(0, sides).forEach(([fx, fz, ry], fi) => {
    const span = fi < 2 ? w : d;
    const n = fi < 2 ? cols : Math.max(1, cols - 1);
    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < n; c++) {
        const off = (c - (n - 1) / 2) * (span / (n + 0.4));
        const lit = ((r * 7 + c * 13 + fi * 5 + seed) % 5) !== 0;
        const x = fi < 2 ? fx + off : fx, z = fi < 2 ? fz : fz + off;
        b.add(box(0.055, 0.075, 0.01), lit ? K.window : K.slate, { glow: lit ? 0.55 : 0, at: [x, y0 + r * dy, z], rot: [0, ry, 0] });
      }
    }
  });
}

function flag(b, x, y, z, h = 0.3) {
  b.add(cyl(0.008, 0.008, h, 4), K.brightSteel, { at: [x, y + h / 2, z] })
    .add(box(0.005, 0.08, 0.13), K.team, { team: 1, at: [x, y + h - 0.05, z + 0.068] })
    .add(ball(0.013, 0), K.gold, { at: [x, y + h + 0.01, z] });
}

function campusHall() {
  // Snow College HQ: a red-brick hall with a white portico, a team-coloured roof and a cupola.
  const b = new Builder();
  foundation(b);
  b.add(box(0.8, 0.42, 0.58), K.brick, { at: [0, 0.29, -0.02] })
    .add(box(0.84, 0.04, 0.62), K.white, { at: [0, 0.52, -0.02] })
    .add(cone(0.54, 0.24, 4), K.team, { team: 1, at: [0, 0.66, -0.02], rot: [0, Math.PI / 4, 0], scale: [1.05, 1, 0.78] });
  for (const x of [-0.21, -0.07, 0.07, 0.21]) b.add(cyl(0.024, 0.028, 0.34, 7), K.white, { at: [x, 0.25, 0.33] });
  b.add(box(0.52, 0.035, 0.12), K.white, { at: [0, 0.43, 0.32] })
    .add(prism(0.54, 0.12, 0.12), K.white, { at: [0, 0.5, 0.32] })
    .add(box(0.1, 0.17, 0.01), K.woodDark, { at: [0, 0.165, 0.272] })
    .add(box(0.15, 0.14, 0.15), K.white, { at: [0, 0.82, -0.02] })
    .add(cone(0.11, 0.14, 8), K.team, { team: 1, at: [0, 0.96, -0.02] });
  windows(b, 0.8, 0.58, 2, 5, 0.2, 0.17, 4, 3);
  flag(b, 0, 1.02, -0.02, 0.24);
  return b.build();
}

function keep() {
  // Classic HQ: a stone keep with four corner towers.
  const b = new Builder();
  foundation(b);
  b.add(box(0.56, 0.6, 0.56), K.stone, { at: [0, 0.38, 0] })
    .add(cone(0.44, 0.3, 4), K.team, { team: 1, at: [0, 0.83, 0], rot: [0, Math.PI / 4, 0] });
  for (const [x, z] of [[-0.33, -0.33], [0.33, -0.33], [-0.33, 0.33], [0.33, 0.33]]) {
    b.add(cyl(0.11, 0.12, 0.62, 8), K.stoneDark, { at: [x, 0.39, z] })
      .add(cone(0.14, 0.26, 8), K.team, { team: 1, at: [x, 0.83, z] });
  }
  b.add(box(0.14, 0.2, 0.01), K.woodDark, { at: [0, 0.18, 0.285] });
  windows(b, 0.56, 0.56, 2, 3, 0.32, 0.2, 4, 1);
  flag(b, 0, 0.95, 0, 0.3);
  return b.build();
}

function dorm() {
  // Snow College housing: a three-storey residence hall, its windows lit.
  const b = new Builder();
  foundation(b);
  b.add(box(0.82, 0.62, 0.48), K.brickTan, { at: [0, 0.39, 0] })
    .add(box(0.86, 0.06, 0.52), K.team, { team: 1, at: [0, 0.73, 0] })
    .add(box(0.22, 0.1, 0.16), K.slate, { at: [0.2, 0.81, -0.05] })
    .add(box(0.26, 0.03, 0.16), K.team, { team: 1, at: [0, 0.27, 0.31] })
    .add(cyl(0.012, 0.012, 0.2, 5), K.white, { at: [-0.11, 0.17, 0.37] })
    .add(cyl(0.012, 0.012, 0.2, 5), K.white, { at: [0.11, 0.17, 0.37] })
    .add(box(0.12, 0.17, 0.01), K.glass, { glow: 0.35, at: [0, 0.165, 0.243] });
  windows(b, 0.82, 0.48, 3, 6, 0.3, 0.17, 4, 7);
  return b.build();
}

function hall() {
  // Classic barracks: a timber hall with a gabled roof and banners.
  const b = new Builder();
  foundation(b);
  b.add(box(0.76, 0.34, 0.5), K.wood, { at: [0, 0.25, 0] })
    .add(prism(0.86, 0.28, 0.6), K.team, { team: 1, at: [0, 0.56, 0], rot: [0, Math.PI / 2, 0], scale: [1, 1, 1.45] })
    .add(box(0.14, 0.2, 0.01), K.woodDark, { at: [0, 0.18, 0.256] })
    .add(box(0.08, 0.2, 0.08), K.stoneDark, { at: [0.25, 0.68, -0.1] });
  for (const x of [-0.3, 0.3]) {
    b.add(cyl(0.01, 0.01, 0.42, 4), K.woodDark, { at: [x, 0.27, 0.32] })
      .add(box(0.09, 0.16, 0.006), K.team, { team: 1, at: [x, 0.38, 0.33] });
  }
  windows(b, 0.76, 0.5, 1, 4, 0.27, 0, 4, 2);
  return b.build();
}

function depot(badger) {
  // Co-op store / depot: a timber store with a striped awning, crates and a cart of ore.
  const b = new Builder();
  foundation(b);
  b.add(box(0.6, 0.32, 0.4), K.wood, { at: [-0.06, 0.25, -0.12] })
    .add(prism(0.66, 0.16, 0.46), K.woodDark, { at: [-0.06, 0.47, -0.12], rot: [0, Math.PI / 2, 0], scale: [1, 1, 1.45] })
    .add(box(0.62, 0.02, 0.18), K.team, { team: 1, at: [-0.06, 0.36, 0.15], rot: [0.35, 0, 0] })
    .add(box(0.3, 0.08, 0.01), K.white, { at: [-0.06, 0.45, 0.082] })
    .add(box(0.12, 0.18, 0.01), K.woodDark, { at: [-0.06, 0.17, 0.083] });
  for (const [x, z, s] of [[0.3, 0.22, 0.12], [0.3, 0.08, 0.1], [0.18, 0.3, 0.09], [0.3, 0.15, 0.08]]) {
    b.add(box(s, s, s), K.wood, { at: [x, 0.07 + s / 2 + (s === 0.08 ? 0.12 : 0), z], rot: [0, x * 3, 0] });
  }
  b.add(cyl(0.05, 0.05, 0.12, 8), K.woodDark, { at: [-0.32, 0.13, 0.22] });
  // The cart and its load.
  b.add(box(0.2, 0.06, 0.14), K.woodDark, { at: [-0.3, 0.15, 0.33] });
  for (const s of [-1, 1]) b.add(cyl(0.05, 0.05, 0.02, 8), K.woodDark, { at: [-0.3 + s * 0.06, 0.11, 0.41], rot: [Math.PI / 2, 0, 0] });
  for (let i = 0; i < 5; i++) {
    b.add(badger ? ball(0.035, 0) : cone(0.03, 0.08, 4), badger ? K.grub : K.ore, { glow: badger ? 0.25 : 0.7, at: [-0.34 + (i % 3) * 0.04, 0.2 + Math.floor(i / 3) * 0.03, 0.31 + (i % 2) * 0.03], rot: [i, i * 2, 0] });
  }
  return b.build();
}

function makerspace() {
  // Tech lab / GRSC Makerspace: concrete drum, glass band, a glowing dome and an antenna.
  const b = new Builder();
  foundation(b);
  b.add(cyl(0.38, 0.4, 0.22, 8), K.stone, { at: [0, 0.18, 0] })
    .add(cyl(0.33, 0.33, 0.14, 16), K.glass, { glow: 0.55, at: [0, 0.36, 0] })
    .add(cyl(0.36, 0.36, 0.03, 16), K.team, { team: 1, at: [0, 0.445, 0] })
    .add(dome(0.27, 16, 8), K.glass, { glow: 0.9, at: [0, 0.46, 0] })
    .add(cyl(0.008, 0.012, 0.32, 4), K.steel, { at: [0.18, 0.72, 0.1] })
    .add(ball(0.022, 0), K.red, { glow: 2.2, at: [0.18, 0.89, 0.1] });
  for (const a of [0.6, 2.4, 4.2]) {
    b.add(cyl(0.07, 0.07, 0.24, 8), K.team, { team: 1, at: [Math.cos(a) * 0.36, 0.19, Math.sin(a) * 0.36] })
      .add(dome(0.07, 8, 4), K.steel, { at: [Math.cos(a) * 0.36, 0.31, Math.sin(a) * 0.36] });
  }
  return b.build();
}

function bellTower() {
  // Snow College's Bell Tower: brick shaft, open white belfry with a gold bell, team spire.
  const b = new Builder();
  foundation(b, 0.6, 0.6);
  b.add(box(0.36, 0.78, 0.36), K.brick, { at: [0, 0.46, 0] })
    .add(box(0.4, 0.04, 0.4), K.white, { at: [0, 0.86, 0] });
  for (const [x, z] of [[-0.16, -0.16], [0.16, -0.16], [-0.16, 0.16], [0.16, 0.16]]) {
    b.add(box(0.05, 0.22, 0.05), K.white, { at: [x, 0.99, z] });
  }
  b.add(box(0.42, 0.04, 0.42), K.white, { at: [0, 1.11, 0] })
    .add(cone(0.24, 0.36, 4), K.team, { team: 1, at: [0, 1.31, 0], rot: [0, Math.PI / 4, 0] })
    .add(cyl(0.04, 0.075, 0.12, 10), K.bell, { glow: 0.5, at: [0, 0.98, 0] })
    .add(ball(0.025, 0), K.ember, { glow: 2, at: [0, 1.52, 0] })
    .add(box(0.12, 0.08, 0.01), K.white, { at: [0, 0.68, 0.185] })
    .add(box(0.06, 0.12, 0.01), K.window, { glow: 1, at: [0, 0.45, 0.185] });
  return b.build();
}

function watchTower() {
  // Classic guard tower: tapered stone with battlements, a banner and a brazier.
  const b = new Builder();
  foundation(b, 0.6, 0.6);
  b.add(cyl(0.17, 0.24, 0.9, 4), K.stone, { at: [0, 0.5, 0], rot: [0, Math.PI / 4, 0] })
    .add(box(0.36, 0.06, 0.36), K.stoneDark, { at: [0, 0.97, 0] });
  for (const [x, z] of [[-0.15, -0.15], [0, -0.15], [0.15, -0.15], [-0.15, 0.15], [0, 0.15], [0.15, 0.15], [-0.15, 0], [0.15, 0]]) {
    b.add(box(0.07, 0.08, 0.07), K.stoneDark, { at: [x, 1.04, z] });
  }
  b.add(cyl(0.06, 0.04, 0.06, 8), K.steelDark, { at: [0, 1.04, 0] })
    .add(ball(0.05, 0), K.ember, { glow: 2.4, at: [0, 1.09, 0] })
    .add(box(0.12, 0.26, 0.006), K.team, { team: 1, at: [0, 0.7, 0.2] })
    .add(box(0.03, 0.1, 0.01), K.black, { at: [0, 0.5, 0.215] });
  return b.build();
}

// ---------------------------------------------------------------- resources & props

function oreCluster() {
  const b = new Builder();
  b.add(ball(0.2, 0), K.stoneDark, { at: [0, 0.02, 0], scale: [1.4, 0.5, 1.2] });
  const crystals = [[0, 0, 0.3, 0], [0.11, 0.05, 0.22, 0.4], [-0.1, 0.06, 0.2, -0.45], [0.04, -0.11, 0.18, 0.3], [-0.07, -0.08, 0.16, -0.2]];
  for (const [x, z, h, tilt] of crystals) {
    b.add(cyl(0.0, 0.05, h, 5), K.ore, { glow: 0.85, at: [x, 0.05 + h / 2, z], rot: [tilt * 0.6, x * 9, tilt] })
      .add(cyl(0.05, 0.0, h * 0.25, 5), K.ore, { glow: 0.85, at: [x - Math.sin(tilt) * 0.0, 0.05 - h * 0.1, z], rot: [tilt * 0.6, x * 9, tilt] });
  }
  return b.build();
}

function grubMound() {
  const b = new Builder();
  b.add(dome(0.26, 10, 4), K.soil, { at: [0, -0.02, 0], scale: [1, 0.45, 1] });
  const spots = [[0, 0.03, 0], [0.12, 0.0, 0.06], [-0.1, 0.0, 0.08], [0.05, 0.0, -0.12], [-0.08, 0.0, -0.07]];
  spots.forEach(([x, y, z], i) => {
    b.add(new THREE.TorusGeometry(0.04, 0.018, 5, 9, Math.PI * 1.5), K.grub, { glow: 0.3, at: [x, 0.09 + y, z], rot: [Math.PI / 2 - 0.3, 0, i * 1.3] });
  });
  return b.build();
}

/** Ore a worker is carrying home. */
function nugget(badger) {
  const b = new Builder();
  if (badger) b.add(new THREE.TorusGeometry(0.035, 0.016, 5, 8, Math.PI * 1.5), K.grub, { glow: 0.4, rot: [Math.PI / 2, 0, 0] });
  else b.add(ball(0.045, 0), K.ore, { glow: 0.9 });
  return b.build();
}

/** Wooden scaffold put up around a building site. */
function scaffold() {
  const b = new Builder();
  const H = 0.9;
  for (const [x, z] of [[-0.44, -0.44], [0.44, -0.44], [-0.44, 0.44], [0.44, 0.44], [0, -0.44], [0, 0.44], [-0.44, 0], [0.44, 0]]) {
    b.add(box(0.025, H, 0.025), K.wood, { at: [x, H / 2, z] });
  }
  for (const y of [0.3, 0.6, 0.88]) {
    for (const [x, z, w, d] of [[0, -0.44, 0.9, 0.02], [0, 0.44, 0.9, 0.02], [-0.44, 0, 0.02, 0.9], [0.44, 0, 0.02, 0.9]]) {
      b.add(box(w, 0.02, d), K.woodDark, { at: [x, y, z] });
    }
  }
  return b.build();
}

/** Every geometry the view needs, for the active brand. Heights are for construction and HP bars. */
export function buildModels(badger) {
  return {
    units: {
      Worker: { geo: worker(badger), height: 0.48 },
      Soldier: { geo: soldier(badger), height: 0.52, scale: 1.08 },
      Archer: { geo: archer(badger), height: 0.52 },
      Scout: { geo: scout(badger), height: 0.6 },
    },
    buildings: {
      CommandCenter: { geo: badger ? campusHall() : keep(), height: 1.1 },
      Barracks: { geo: badger ? dorm() : hall(), height: 0.85 },
      ResourceDepot: { geo: depot(badger), height: 0.6 },
      TechLab: { geo: makerspace(), height: 0.9 },
      GuardTower: { geo: badger ? bellTower() : watchTower(), height: 1.55 },
    },
    deposit: badger ? grubMound() : oreCluster(),
    nugget: nugget(badger),
    scaffold: scaffold(),
  };
}

/**
 * The material for units and buildings: vertex colours, with the player's colour where `team` is set,
 * self-lit where `glow` is set, a red-white flash when hit, and (for buildings under construction)
 * everything above the build height cut away with a glowing seam.
 */
export function entityMaterial() {
  const mat = new THREE.MeshStandardMaterial({ vertexColors: true, flatShading: true, roughness: 0.72, metalness: 0.04 });
  mat.onBeforeCompile = (sh) => {
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', `#include <common>
        attribute float team; attribute float glow; attribute float limb;
        attribute vec3 iTeam; attribute float iFlash; attribute float iCut; attribute vec3 iWalk;
        varying float vTeam; varying float vGlow; varying vec3 vTeamCol; varying float vFlash; varying float vCut; varying float vLocalY;`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
        vTeam = team; vGlow = glow; vTeamCol = iTeam; vFlash = iFlash; vCut = iCut; vLocalY = position.y;
        // Swing limbs about the hip or shoulder: iWalk = (phase, leg swing, arm swing).
        if (limb != 0.0) {
          float kind = abs(limb);
          float pivot = kind < 1.5 ? 0.13 : (kind < 2.5 ? 0.28 : 0.15);
          float amp = kind < 2.5 && kind > 1.5 ? -iWalk.z : iWalk.y;
          float ang = sin(iWalk.x) * amp * sign(limb);
          float c = cos(ang), s = sin(ang);
          vec3 p = transformed - vec3(0.0, pivot, 0.0);
          transformed = vec3(p.x, p.y * c - p.z * s, p.y * s + p.z * c) + vec3(0.0, pivot, 0.0);
        }`);
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', `#include <common>
        varying float vTeam; varying float vGlow; varying vec3 vTeamCol; varying float vFlash; varying float vCut; varying float vLocalY;`)
      .replace('#include <clipping_planes_fragment>', `#include <clipping_planes_fragment>
        if (vLocalY > vCut) discard;`)
      .replace('#include <color_fragment>', `#include <color_fragment>
        diffuseColor.rgb = mix(diffuseColor.rgb, diffuseColor.rgb * vTeamCol, vTeam);`)
      .replace('#include <emissivemap_fragment>', `#include <emissivemap_fragment>
        totalEmissiveRadiance += diffuseColor.rgb * vGlow * 2.6;
        totalEmissiveRadiance += vec3(1.0, 0.45, 0.3) * vFlash * 1.8;
        float seam = 1.0 - smoothstep(0.0, 0.035, vCut - vLocalY);
        totalEmissiveRadiance += mix(vec3(1.0, 0.8, 0.4), vTeamCol, 0.4) * seam * step(vCut, 50.0) * 3.0;`);
  };
  mat.customProgramCacheKey = () => 'netrts-entity';
  return mat;
}

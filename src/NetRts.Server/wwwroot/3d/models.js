// 3D match view: low-poly models built in code, so there are no art files to load. Every model is one
// merged geometry with per-vertex colour plus two extra attributes: `team` (1 where the player's colour
// shows through) and `glow` (self-lit windows, crystals, lanterns). Units and buildings are drawn as
// instanced meshes with per-instance team colour, hit flash and construction height.
//
// One tile is one world unit; models stand on y = 0 and face +z.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { col, K, Builder, box, cyl, ball, cone, dome, prism, foundation, windows, flag } from './kit.js';
import { HQ_MODELS, HOUSING_MODELS } from './campus.js';

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
    .add(cyl(0.33, 0.33, 0.14, 16), K.glass, { glow: 0.25, at: [0, 0.36, 0] })
    .add(cyl(0.36, 0.36, 0.03, 16), K.team, { team: 1, at: [0, 0.445, 0] })
    .add(dome(0.27, 16, 8), K.glass, { glow: 0.3, at: [0, 0.46, 0] })
    .add(cyl(0.008, 0.012, 0.32, 4), K.steel, { at: [0.18, 0.72, 0.1] })
    .add(ball(0.022, 0), K.red, { glow: 2.2, at: [0.18, 0.89, 0.1] });
  for (const a of [0.6, 2.4, 4.2]) {
    b.add(cyl(0.07, 0.07, 0.24, 8), K.team, { team: 1, at: [Math.cos(a) * 0.36, 0.19, Math.sin(a) * 0.36] })
      .add(dome(0.07, 8, 4), K.steel, { at: [Math.cos(a) * 0.36, 0.31, Math.sin(a) * 0.36] });
  }
  return b.build();
}

function bellTower() {
  // The Snow College Bell Tower: three red brick stages, each narrower than the one below, banded in
  // buff stone. A walk-through arch at the base, one big bell in the middle stage, three bells stacked
  // under a clock at the top, and campus lamp posts beside it.
  const b = new Builder();
  foundation(b, 0.66, 0.66);
  const brick = col('#a8503a'), buff = col('#d9c49a'), bronze = col('#3b3330'), bellMetal = col('#5e4a32');
  const Y = 0.085;
  // Four piers at the corners of a w-wide square stage, from y0 to y0 + h.
  const piers = (w, p, y0, h) => {
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) b.add(box(p, h, p), brick, { at: [sx * (w - p) / 2, y0 + h / 2, sz * (w - p) / 2] });
  };
  // A band all the way round a stage at height y.
  const band = (w, y, h, colour, out = 0.004) => b.add(box(w + out * 2, h, w + out * 2), colour, { at: [0, y + h / 2, 0] });
  // Lintels across the open faces at height y (the openings face front/back and left/right).
  const lintels = (w, y, h, colour) => {
    b.add(box(w, h, w * 0.98), colour, { at: [0, y + h / 2, 0] });
  };
  const bell = (y, s) => {
    b.add(cyl(0.022 * s, 0.05 * s, 0.07 * s, 10), bellMetal, { glow: 0.15, at: [0, y, 0] })
      .add(cyl(0.054 * s, 0.054 * s, 0.01 * s, 10), bellMetal, { at: [0, y - 0.035 * s, 0] })
      .add(box(0.08 * s, 0.008, 0.012), bronze, { at: [0, y + 0.04 * s, 0] });
  };

  // Base: the arch you walk through, on buff plinths.
  const w0 = 0.5, h0 = 0.44;
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) b.add(box(0.15, 0.07, 0.15), buff, { at: [sx * 0.175, Y + 0.035, sz * 0.175] });
  piers(w0, 0.13, Y + 0.07, h0 - 0.07);
  b.add(box(w0 + 0.008, 0.06, 0.13), buff, { at: [0, Y + 0.28, 0.185] })          // the buff lintels over the arches
    .add(box(w0 + 0.008, 0.06, 0.13), buff, { at: [0, Y + 0.28, -0.185] })
    .add(box(0.13, 0.06, w0 + 0.008), buff, { at: [0.185, Y + 0.28, 0] })
    .add(box(0.13, 0.06, w0 + 0.008), buff, { at: [-0.185, Y + 0.28, 0] });
  lintels(w0, Y + 0.34, h0 - 0.34, brick);
  band(w0, Y + h0 - 0.03, 0.02, buff);
  band(w0, Y + h0 - 0.01, 0.03, bronze, 0.022);
  // Middle stage: one big bell in an open bay.
  const y1 = Y + h0 + 0.02, w1 = 0.38, h1 = 0.42;
  lintels(w1, y1, 0.08, brick);
  band(w1, y1 + 0.08, 0.02, buff);
  piers(w1, 0.1, y1 + 0.1, 0.22);
  band(w1, y1 + 0.32, 0.025, buff);
  lintels(w1, y1 + 0.345, h1 - 0.345, brick);
  band(w1, y1 + h1, 0.03, bronze, 0.02);
  bell(y1 + 0.2, 1.3);
  // Top stage: three bells stacked in a tall slot, the clock above.
  const y2 = y1 + h1 + 0.03, w2 = 0.28, h2 = 0.66;
  lintels(w2, y2, 0.04, brick);
  band(w2, y2 + 0.04, 0.018, buff);
  piers(w2, 0.075, y2 + 0.058, 0.38);
  for (let i = 0; i < 3; i++) {
    b.add(box(w2 - 0.1, 0.012, w2 - 0.1), bronze, { at: [0, y2 + 0.16 + i * 0.12, 0] });
    bell(y2 + 0.11 + i * 0.12, 0.8);
  }
  band(w2, y2 + 0.438, 0.018, buff);
  lintels(w2, y2 + 0.456, h2 - 0.456, brick);
  // Clock faces on all four sides.
  for (let i = 0; i < 4; i++) {
    const a = (i * Math.PI) / 2, r = w2 / 2 + 0.006, cy = y2 + 0.56;
    const cx = Math.sin(a) * r, cz = Math.cos(a) * r;
    b.add(cyl(0.06, 0.06, 0.01, 16), col('#f2efe6'), { glow: 0.25, at: [cx, cy, cz], rot: i % 2 === 0 ? [Math.PI / 2, 0, 0] : [0, 0, Math.PI / 2] })
      .add(box(0.006, 0.045, 0.004), K.black, { at: [cx + Math.sin(a) * 0.006, cy + 0.016, cz + Math.cos(a) * 0.006], rot: [0, a, 0] })
      .add(box(0.032, 0.006, 0.004), K.black, { at: [cx + Math.sin(a) * 0.006 + Math.cos(a) * 0.012, cy, cz + Math.cos(a) * 0.006 - Math.sin(a) * 0.012], rot: [0, a, 0] });
  }
  band(w2, y2 + h2 - 0.02, 0.02, buff);
  band(w2, y2 + h2, 0.03, bronze, 0.02);
  b.add(cone(0.24, 0.07, 4), K.team, { team: 1, at: [0, y2 + h2 + 0.065, 0], rot: [0, Math.PI / 4, 0] });
  // Campus lamp posts.
  for (const [x, z] of [[-0.28, 0.28], [0.28, 0.28]]) {
    b.add(cyl(0.022, 0.026, 0.02, 8), col('#9a968e'), { at: [x, Y + 0.01, z] })
      .add(cyl(0.007, 0.009, 0.3, 6), K.black, { at: [x, Y + 0.16, z] })
      .add(cyl(0.022, 0.016, 0.04, 8), col('#f2e6c8'), { glow: 1.2, at: [x, Y + 0.33, z] })
      .add(cone(0.04, 0.02, 8), K.black, { at: [x, Y + 0.36, z] });
  }
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


/**
 * Every geometry the view needs, for the active brand. In the Snow College theme each player's HQ
 * and housing is modelled on its real campus building (see lore.js for which slot gets which).
 * Buildings are made the first time they're needed; their height (for construction and health bars)
 * comes from the model itself.
 */
export function buildModels(badger) {
  const made = new Map();
  const get = (key, make) => {
    if (!made.has(key)) {
      const geo = make();
      made.set(key, { key, geo, height: geo.boundingBox.max.y });
    }
    return made.get(key);
  };
  const base = {
    CommandCenter: badger ? campusHall : keep,
    Barracks: badger ? dorm : hall,
    ResourceDepot: () => depot(badger),
    TechLab: makerspace,
    GuardTower: badger ? bellTower : watchTower,
  };
  const slotOf = (slot) => ((slot % 16) + 16) % 16;
  return {
    units: {
      Worker: { geo: worker(badger), height: 0.48 },
      Soldier: { geo: soldier(badger), height: 0.52, scale: 1.08 },
      Archer: { geo: archer(badger), height: 0.52 },
      Scout: { geo: scout(badger), height: 0.6 },
    },
    building(type, slot) {
      if (badger && type === 'CommandCenter') {
        const i = slotOf(slot);
        return get(`hq${i}`, HQ_MODELS[i] || campusHall);
      }
      if (badger && type === 'Barracks') {
        const i = slotOf(slot);
        return get(`housing${i}`, HOUSING_MODELS[i] || dorm);
      }
      return get(type, base[type]);
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
        // Swing limbs about the hip or shoulder: iWalk = (phase, leg swing, arm swing). A negative arm
        // swing raises both arms together, up to that many radians (a pickaxe, hammer or sword chop).
        if (limb != 0.0) {
          float kind = abs(limb);
          float pivot = kind < 1.5 ? 0.13 : (kind < 2.5 ? 0.28 : 0.15);
          bool isArm = kind > 1.5 && kind < 2.5;
          float ang = isArm && iWalk.z < 0.0
            ? (sin(iWalk.x) * 0.5 + 0.5) * iWalk.z
            : sin(iWalk.x) * (isArm ? -iWalk.z : iWalk.y) * sign(limb);
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

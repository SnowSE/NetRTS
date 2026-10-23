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

/** A round shield worn over a soldier's own (same place on the left arm): rings, a blue centre and a star. */
function roundShield() {
  const b = new Builder();
  const disc = (r, z, colour) => b.add(cyl(r, r, 0.006, 20), colour, { glow: 0.2, at: [-0.14, 0.2, z], rot: [Math.PI / 2, 0, 0] });
  disc(0.13, 0.058, col('#c8102e'));
  disc(0.102, 0.062, col('#f5f5f5'));
  disc(0.077, 0.066, col('#c8102e'));
  disc(0.052, 0.07, col('#1f3a93'));
  const star = new THREE.Shape();
  for (let i = 0; i < 10; i++) {
    const r = i % 2 ? 0.019 : 0.046, t = Math.PI / 2 + (i * Math.PI) / 5;
    if (i) star.lineTo(Math.cos(t) * r, Math.sin(t) * r);
    else star.moveTo(Math.cos(t) * r, Math.sin(t) * r);
  }
  b.add(new THREE.ShapeGeometry(star), col('#f5f5f5'), { glow: 0.3, at: [-0.14, 0.2, 0.074] });
  return b.build();
}

function archer(badger) {
  const b = new Builder();
  body(b);
  head(b, badger);
  const hy = badger ? 0.4 : 0.38;
  b.add(cone(0.088, 0.14, 8), K.teamShade, { team: 1, at: [0, hy + 0.02, -0.012] })
    .add(box(0.15, 0.2, 0.012), K.teamShade, { team: 1, at: [0, 0.22, -0.085], rot: [0.12, 0, 0] })
    // The bow, held at the side (limb 4: raised to aim in the shader), its string (limb 5: drawn back)
    // and the arrow on the string (limb 6: shown only while drawing).
    .add(new THREE.TorusGeometry(0.15, 0.011, 4, 14, Math.PI * 0.86), K.woodDark, { limb: 4, at: [-0.13, 0.22, -0.07], rot: [0, -Math.PI / 2, -Math.PI * 0.43] })
    .add(cyl(0.003, 0.003, 0.29, 3), K.white, { limb: 5, at: [-0.13, 0.22, -0.048] })
    .add(box(0.008, 0.008, 0.28), col('#d8c39a'), { limb: 6, at: [-0.13, 0.22, 0.092] })
    .add(cone(0.014, 0.04, 4), K.brightSteel, { limb: 6, at: [-0.13, 0.22, 0.25], rot: [Math.PI / 2, 0, 0] })
    .add(box(0.003, 0.022, 0.04), K.team, { team: 1, limb: 6, at: [-0.13, 0.22, -0.03] })
    // Quiver with fletched arrows.
    .add(cyl(0.032, 0.026, 0.17, 6), K.leather, { at: [0.05, 0.26, -0.1], rot: [0.25, 0, -0.35] })
    .add(box(0.05, 0.04, 0.02), K.team, { team: 1, at: [0.085, 0.36, -0.125], rot: [0.25, 0, -0.35] });
  return b.build();
}

function scout(badger) {
  // A rider on a horse: long legs, an arched neck, a dark mane and tail, a saddle blanket in team colours.
  const b = new Builder();
  const coat = col('#7a4f30'), dark = col('#2b1d14'), hoof = col('#1e1a17'), blaze = col('#efe8dc');
  b.add(box(0.12, 0.13, 0.32), coat, { at: [0, 0.25, 0] })                          // barrel
    .add(ball(0.075, 1), coat, { at: [0, 0.255, -0.13], scale: [0.85, 0.9, 0.9] })    // hindquarters
    .add(ball(0.07, 1), coat, { at: [0, 0.27, 0.13], scale: [0.85, 0.95, 0.9] });     // chest
  for (const [x, z] of [[-0.042, -0.12], [0.042, -0.12], [-0.042, 0.12], [0.042, 0.12]]) {
    const limb = (x < 0 ? 3 : -3) * (z > 0 ? 1 : -1);
    // Each leg swings from its own hip or shoulder, at the front or back of the body.
    b.add(box(0.03, 0.19, 0.034), coat, { limb, joint: z, at: [x, 0.095, z] })
      .add(box(0.034, 0.025, 0.04), hoof, { limb, joint: z, at: [x, 0.012, z + 0.004] });
  }
  // The neck rises from the chest and leans forward to the head; the mane runs down the back of it.
  b.add(box(0.06, 0.17, 0.08), coat, { at: [0, 0.37, 0.18], rot: [0.6, 0, 0] })       // neck
    .add(box(0.022, 0.16, 0.05), dark, { at: [0, 0.392, 0.152], rot: [0.6, 0, 0] })    // mane
    .add(box(0.055, 0.06, 0.14), coat, { at: [0, 0.44, 0.265], rot: [0.35, 0, 0] })    // head
    .add(box(0.03, 0.03, 0.06), blaze, { at: [0, 0.445, 0.3], rot: [0.35, 0, 0] })     // blaze
    .add(box(0.018, 0.045, 0.014), coat, { at: [-0.02, 0.5, 0.215] })                  // ears
    .add(box(0.018, 0.045, 0.014), coat, { at: [0.02, 0.5, 0.215] })
    .add(ball(0.01, 0), K.black, { at: [-0.029, 0.46, 0.27] })
    .add(ball(0.01, 0), K.black, { at: [0.029, 0.46, 0.27] })
    .add(box(0.03, 0.15, 0.035), dark, { at: [0, 0.22, -0.2], rot: [0.5, 0, 0] })      // tail
    .add(box(0.14, 0.025, 0.14), K.team, { team: 1, at: [0, 0.322, -0.01] })            // saddle blanket
    .add(box(0.08, 0.03, 0.09), K.leather, { at: [0, 0.345, -0.01] });                  // saddle
  // The rider sits on the saddle.
  const r = new Builder();
  body(r, { legs: false, animate: false });
  head(r, badger);
  r.add(box(0.16, 0.2, 0.012), K.teamShade, { team: 1, at: [0, 0.2, -0.08], rot: [0.35, 0, 0] })
    .add(cyl(0.08, 0.08, 0.03, 8), K.teamShade, { team: 1, at: [0, 0.29, 0] });
  const rider = r.build().scale(0.8, 0.8, 0.8).translate(0, 0.22, -0.02);
  const all = mergeGeometries([b.build(), rider]);
  all.computeBoundingSphere();
  return all;
}

// ---------------------------------------------------------------- siege

// The trebuchet's throwing arm turns about its axle at this height (limb 7: the arm, limb 9: the stone in
// its sling, hidden once thrown). The shader swings both by iDraw.x radians; iDraw.y = 1 hides the stone.
const AXLE_Y = 0.36;
// The Snow Mobile's roof cannon (limb 8) slides back along its barrel by iDraw.x when it fires.

function trebuchet() {
  // A wooden trebuchet on four wheels: a rail frame, two A-frames carrying the axle, a long throwing arm
  // with a boxed counterweight on the short end, and a sling with its stone lying in the trough behind.
  const b = new Builder();
  const wood = K.wood, dark = K.woodDark, iron = K.steelDark;
  for (const x of [-0.12, 0.12]) {
    b.add(box(0.035, 0.045, 0.52), dark, { at: [x, 0.09, 0] })                       // side rails
      .add(box(0.006, 0.05, 0.2), K.team, { team: 1, at: [x + Math.sign(x) * 0.02, 0.1, -0.02] });   // painted boards
    for (const z of [-0.17, 0.17]) {
      b.add(cyl(0.065, 0.065, 0.026, 10), dark, { at: [x * 1.3, 0.065, z], rot: [0, 0, Math.PI / 2] })
        .add(cyl(0.02, 0.02, 0.034, 6), iron, { at: [x * 1.3, 0.065, z], rot: [0, 0, Math.PI / 2] });
    }
    // The A-frame: two legs leaning in to meet under the axle, and a brace across.
    const len = Math.hypot(AXLE_Y - 0.09, 0.15), tilt = Math.atan2(0.15, AXLE_Y - 0.09);
    b.add(box(0.026, len, 0.026), wood, { at: [x, (AXLE_Y + 0.09) / 2, 0.075], rot: [-tilt, 0, 0] })
      .add(box(0.026, len, 0.026), wood, { at: [x, (AXLE_Y + 0.09) / 2, -0.075], rot: [tilt, 0, 0] })
      .add(box(0.022, 0.022, 0.2), dark, { at: [x, 0.2, 0] });
  }
  for (const z of [-0.23, 0, 0.23]) b.add(box(0.27, 0.03, 0.035), dark, { at: [0, 0.09, z] });   // cross beams
  b.add(box(0.2, 0.012, 0.16), wood, { at: [0, 0.1, -0.31] })                          // the trough
    .add(box(0.24, 0.03, 0.03), dark, { at: [0, 0.1, -0.25] })
    .add(cyl(0.016, 0.016, 0.3, 6), iron, { at: [0, AXLE_Y, 0], rot: [0, 0, Math.PI / 2] })   // axle
    .add(cyl(0.03, 0.03, 0.18, 8), dark, { at: [0, 0.14, -0.2], rot: [0, 0, Math.PI / 2] })   // winch
    .add(cyl(0.006, 0.006, 0.2, 4), dark, { at: [0.16, 0.14, -0.2], rot: [0.7, 0, 0] })
    .add(cyl(0.006, 0.006, 0.18, 4), dark, { at: [0.12, AXLE_Y + 0.11, 0] })            // pennant on its staff
    .add(box(0.005, 0.05, 0.09), K.team, { team: 1, at: [0.12, AXLE_Y + 0.17, -0.045] });

  // The arm at rest: the long end down behind, the counterweight up in front (see the shader's limb 7).
  const phi = -2.44, dy = Math.sin(phi), dz = Math.cos(phi);   // the long end's direction in the y-z plane
  const along = (r) => [0, AXLE_Y + dy * r, dz * r];
  const arm = { limb: 7, joint: AXLE_Y };
  b.add(box(0.032, 0.032, 0.58), wood, { ...arm, at: along(0.145), rot: [-phi, 0, 0] })
    .add(box(0.04, 0.04, 0.04), iron, { ...arm, at: along(0) })
    // The counterweight: an iron-banded box of stones hanging from the short end.
    .add(box(0.15, 0.12, 0.11), dark, { ...arm, at: along(-0.19), rot: [-phi, 0, 0] })
    .add(box(0.155, 0.02, 0.115), iron, { ...arm, at: along(-0.17), rot: [-phi, 0, 0] })
    .add(box(0.155, 0.02, 0.115), iron, { ...arm, at: along(-0.23), rot: [-phi, 0, 0] });
  // The sling's ropes hang from the tip to the pouch in the trough.
  const tip = along(0.43);
  b.add(cyl(0.004, 0.004, 0.09, 3), K.white, { ...arm, at: [0, (tip[1] + 0.1) / 2, tip[2] - 0.03], rot: [0.6, 0, 0] })
    .add(box(0.07, 0.015, 0.06), K.leather, { ...arm, at: [0, 0.115, tip[2] - 0.06] })
    .add(ball(0.04, 0), K.stone, { limb: 9, joint: AXLE_Y, at: [0, 0.145, tip[2] - 0.06] });
  return b.build();
}

function snowMobile() {
  // The Snow Mobile: Snow College's step van (a retired Snap-on tool truck), as wrapped today. A tall
  // navy box with a white roof cap, a flat cab front over a short hood and grille, a white swoosh
  // rising along each side with an orange pinstripe, the orange "S" on a dark panel behind the door,
  // white wheels and bumper. The roof carries a cannon where the real one carries its bubble machine.
  const b = new Builder();
  const navy = col('#1d3470'), deep = col('#14214a'), blue = col('#3a64ad'), orange = col('#f58025');
  const white = col('#f4f4f2'), grey = col('#b9bcc0'), tyre = col('#1b1b1d');
  // Wheels: white steel rims on black tyres, the front pair under the cab.
  for (const z of [-0.16, 0.17]) {
    for (const x of [-0.12, 0.12]) {
      b.add(cyl(0.062, 0.062, 0.05, 12), tyre, { at: [x, 0.062, z], rot: [0, 0, Math.PI / 2] })
        .add(cyl(0.036, 0.036, 0.055, 10), white, { at: [x, 0.062, z], rot: [0, 0, Math.PI / 2] })
        .add(cyl(0.012, 0.012, 0.06, 6), grey, { at: [x, 0.062, z], rot: [0, 0, Math.PI / 2] });
    }
  }
  b.add(box(0.22, 0.05, 0.52), K.black, { at: [0, 0.1, -0.01] });                       // chassis
  // The box body, cab and all, with a lighter blue sheen high on the sides and a white roof cap.
  b.add(box(0.27, 0.34, 0.49), navy, { at: [0, 0.29, -0.035] })
    .add(box(0.274, 0.1, 0.4), blue, { at: [0, 0.39, -0.07] })
    .add(box(0.28, 0.035, 0.5), white, { at: [0, 0.475, -0.035] })
    .add(box(0.26, 0.012, 0.48), col('#e2e3e0'), { at: [0, 0.497, -0.035] });
  for (const x of [-0.12, 0.12]) b.add(box(0.016, 0.018, 0.4), K.team, { team: 1, at: [x, 0.51, -0.05] });   // roof rails in the team's colour
  // The cab front: a two-pane windscreen, raked a little, under a navy header with the orange S and
  // "SNOW COLLEGE" in white.
  for (const x of [-0.064, 0.064]) b.add(box(0.118, 0.11, 0.01), K.pane, { glow: 0.1, at: [x, 0.355, 0.21], rot: [-0.12, 0, 0] });
  b.add(box(0.008, 0.11, 0.012), deep, { at: [0, 0.355, 0.212], rot: [-0.12, 0, 0] })
    .add(box(0.03, 0.03, 0.006), orange, { at: [-0.075, 0.435, 0.208] })
    .add(box(0.13, 0.014, 0.006), white, { at: [0.02, 0.435, 0.208] });
  // The hood: a short navy nose with a grey grille, round headlamps and a white bumper.
  b.add(box(0.25, 0.15, 0.08), navy, { at: [0, 0.175, 0.24] })
    .add(box(0.25, 0.03, 0.07), blue, { at: [0, 0.255, 0.235], rot: [0.25, 0, 0] })
    .add(box(0.1, 0.085, 0.008), grey, { at: [0, 0.175, 0.282] });
  for (let i = 0; i < 4; i++) b.add(box(0.1, 0.005, 0.01), K.steelDark, { at: [0, 0.145 + i * 0.02, 0.285] });
  for (const x of [-0.088, 0.088]) {
    b.add(cyl(0.022, 0.022, 0.012, 10), col('#fff4d0'), { glow: 0.8, at: [x, 0.19, 0.282], rot: [Math.PI / 2, 0, 0] })
      .add(box(0.022, 0.01, 0.006), orange, { glow: 0.4, at: [x, 0.155, 0.282] });
  }
  b.add(box(0.28, 0.04, 0.03), white, { at: [0, 0.11, 0.29] });
  // Each side: the cab door window and mirror, the white swoosh with its orange pinstripe rising toward
  // the back, an orange chevron, and the big orange S on its dark panel. The S is built for whoever
  // looks at that side, so it reads the right way round on both.
  for (const s of [-1, 1]) {
    const x = s * 0.137;
    b.add(box(0.006, 0.09, 0.06), K.pane, { at: [x, 0.35, 0.165] })
      .add(box(0.006, 0.2, 0.004), deep, { at: [x, 0.28, 0.13] })                         // door seam
      .add(cyl(0.004, 0.004, 0.05, 4), K.black, { at: [s * 0.155, 0.33, 0.2], rot: [0, 0, Math.PI / 2] })
      .add(box(0.012, 0.06, 0.025), K.black, { at: [s * 0.18, 0.33, 0.2] })
      .add(box(0.006, 0.07, 0.36), white, { at: [x, 0.27, -0.08], rot: [0.32, 0, 0] })
      .add(box(0.007, 0.012, 0.3), orange, { at: [s * 0.138, 0.32, -0.07], rot: [0.32, 0, 0] })
      .add(box(0.007, 0.014, 0.08), orange, { at: [s * 0.138, 0.43, -0.2], rot: [-0.5, 0, 0] });
    const zc = 0.065, w = 0.05, t = 0.014, top = 0.43, h = 0.1;
    b.add(box(0.006, 0.15, 0.095), deep, { at: [x, 0.375, zc] });
    for (const y of [top, top - h / 2, top - h]) b.add(box(0.008, t, w + t), orange, { at: [s * 0.139, y, zc] });
    b.add(box(0.008, h / 2, t), orange, { at: [s * 0.139, top - h / 4, zc + s * w / 2] })
      .add(box(0.008, h / 2, t), orange, { at: [s * 0.139, top - 3 * h / 4, zc - s * w / 2] });
  }
  b.add(box(0.2, 0.02, 0.03), grey, { at: [0, 0.11, -0.285] })                           // rear step
    .add(box(0.03, 0.02, 0.01), K.red, { glow: 0.6, at: [-0.11, 0.17, -0.282] })
    .add(box(0.03, 0.02, 0.01), K.red, { glow: 0.6, at: [0.11, 0.17, -0.282] });
  // The roof cannon, over the cab where the bubble machine sits: a navy turret ring with steel cheeks
  // and the barrel raised a little (limb 8 recoils).
  const ty = 0.5, tz = 0.06, lift = 0.22;
  b.add(cyl(0.07, 0.08, 0.04, 12), deep, { at: [0, ty + 0.02, tz] })
    .add(box(0.025, 0.07, 0.09), K.steelDark, { at: [-0.05, ty + 0.07, tz] })
    .add(box(0.025, 0.07, 0.09), K.steelDark, { at: [0.05, ty + 0.07, tz] });
  const gun = { limb: 8 };
  const bz = (r) => tz + Math.cos(lift) * r, by = (r) => ty + 0.08 + Math.sin(lift) * r;
  b.add(cyl(0.024, 0.036, 0.3, 10), K.black, { ...gun, at: [0, by(0.1), bz(0.1)], rot: [Math.PI / 2 - lift, 0, 0] })
    .add(ball(0.045, 1), K.black, { ...gun, at: [0, by(-0.04), bz(-0.04)] })
    .add(cyl(0.032, 0.032, 0.03, 10), orange, { ...gun, at: [0, by(0.24), bz(0.24)], rot: [Math.PI / 2 - lift, 0, 0] })
    .add(cyl(0.03, 0.03, 0.02, 10), K.team, { team: 1, ...gun, at: [0, by(0.02), bz(0.02)], rot: [Math.PI / 2 - lift, 0, 0] });
  return b.build();
}

/** A shot in flight: a rough stone (classic) or an iron cannonball (Snow College). */
function shell(badger) {
  const b = new Builder();
  if (badger) b.add(ball(0.055, 2), col('#26272b'), {}).add(ball(0.012, 0), K.ember, { glow: 2, at: [0, 0.052, 0] });
  else b.add(ball(0.065, 0), K.stone, { scale: [1, 0.86, 1.1] });
  return b.build();
}

// ---------------------------------------------------------------- buildings


function campusHall() {
  // Snow College HQ: a red-brick hall with a white portico, a team-coloured roof and a cupola.
  const b = new Builder();
  foundation(b);
  b.add(box(0.8, 0.42, 0.58), K.brick, { at: [0, 0.29, -0.02] })
    .add(box(0.84, 0.04, 0.62), K.white, { at: [0, 0.52, -0.02] })
    .add(cone(Math.SQRT1_2, 0.24, 4).rotateY(Math.PI / 4), K.team, { team: 1, at: [0, 0.66, -0.02], scale: [0.88, 1, 0.66] });
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

function siegeShed() {
  // Classic siege workshop: an open-sided timber shed under a gabled roof, a half-built throwing arm on
  // trestles inside, a stack of lumber, and a timber crane with a hook at one corner.
  const b = new Builder();
  foundation(b);
  for (const [x, z] of [[-0.36, -0.28], [0, -0.28], [0.36, -0.28], [-0.36, 0.28], [0, 0.28], [0.36, 0.28]]) {
    b.add(box(0.045, 0.42, 0.045), K.woodDark, { at: [x, 0.28, z] });
  }
  b.add(box(0.78, 0.04, 0.05), K.woodDark, { at: [0, 0.49, -0.28] })
    .add(box(0.78, 0.04, 0.05), K.woodDark, { at: [0, 0.49, 0.28] })
    .add(prism(0.84, 0.26, 0.66), K.team, { team: 1, at: [0, 0.64, 0], rot: [0, Math.PI / 2, 0], scale: [1, 1, 1.32] })
    .add(box(0.64, 0.24, 0.02), K.wood, { at: [0, 0.2, -0.27] });                    // the back wall, half high
  // Trestles and the arm being shaped on them, with its iron fittings waiting beside it.
  for (const x of [-0.2, 0.15]) {
    b.add(box(0.03, 0.1, 0.16), K.wood, { at: [x, 0.12, 0.02] })
      .add(box(0.05, 0.02, 0.18), K.woodDark, { at: [x, 0.175, 0.02] });
  }
  b.add(box(0.6, 0.04, 0.04), col('#b08a5a'), { at: [-0.02, 0.205, 0.02], rot: [0, 0.08, 0] })
    .add(box(0.1, 0.08, 0.1), K.woodDark, { at: [0.26, 0.13, 0.04] })
    .add(cyl(0.035, 0.035, 0.04, 8), K.steelDark, { at: [0.26, 0.19, 0.04], rot: [0, 0, Math.PI / 2] })
    .add(cyl(0.06, 0.06, 0.02, 10), K.woodDark, { at: [-0.3, 0.13, -0.1], rot: [0, 0, Math.PI / 2] });   // a spare wheel
  // Lumber stacked outside.
  for (let i = 0; i < 6; i++) {
    b.add(box(0.05, 0.035, 0.36), i % 2 ? K.wood : col('#a07a4c'), { at: [0.38 + (i % 2) * 0.055, 0.09 + Math.floor(i / 2) * 0.037, 0.06] });
  }
  // The crane: a mast, a jib reaching out over the yard, a rope and a hook.
  b.add(box(0.05, 0.78, 0.05), K.woodDark, { at: [-0.4, 0.46, 0.36] })
    .add(box(0.04, 0.04, 0.42), K.wood, { at: [-0.4, 0.8, 0.2], rot: [-0.25, 0, 0] })
    .add(box(0.03, 0.3, 0.03), K.wood, { at: [-0.4, 0.66, 0.29], rot: [0.7, 0, 0] })
    .add(cyl(0.004, 0.004, 0.4, 3), K.white, { at: [-0.4, 0.66, 0.02] })
    .add(new THREE.TorusGeometry(0.02, 0.006, 4, 8, Math.PI * 1.4), K.steelDark, { at: [-0.4, 0.44, 0.02], rot: [0, Math.PI / 2, Math.PI] })
    .add(box(0.08, 0.06, 0.08), K.wood, { at: [-0.4, 0.4, 0.02] })
    .add(box(0.012, 0.12, 0.08), K.team, { team: 1, at: [-0.4, 0.68, 0.39] });
  return b.build();
}

function washburn() {
  // The Washburn Building on Snow College's Richfield campus, home of the welding and automotive shops:
  // a long, low tan-block building with a flat roof, big roll-up bay doors (one up, a welder's glow
  // inside), a navy sign band, rooftop units and team-coloured trim along the parapet.
  const b = new Builder();
  foundation(b);
  const block = col('#cdb68c'), blockDark = col('#b39a70'), navy = col('#00205b'), door = col('#9aa0a6');
  b.add(box(0.84, 0.36, 0.62), block, { at: [0, 0.25, -0.02] })
    .add(box(0.86, 0.04, 0.64), K.team, { team: 1, at: [0, 0.45, -0.02] })              // the parapet cap
    .add(box(0.84, 0.03, 0.62), blockDark, { at: [0, 0.1, -0.02] })                       // block course near the ground
    .add(box(0.2, 0.08, 0.14), K.slate, { at: [0.22, 0.51, -0.1] })                       // rooftop units
    .add(box(0.12, 0.06, 0.1), K.steel, { at: [-0.18, 0.5, -0.15] })
    .add(cyl(0.02, 0.02, 0.14, 6), K.steelDark, { at: [-0.3, 0.54, -0.2] })               // the shop's exhaust stack
    .add(box(0.62, 0.05, 0.012), navy, { at: [0, 0.4, 0.296] })                           // sign band
    .add(box(0.4, 0.016, 0.004), col('#f47b20'), { glow: 0.25, at: [0, 0.4, 0.303] });
  // Two bay doors on the front: the left one down (ribbed steel), the right one up, the shop lit inside.
  b.add(box(0.26, 0.27, 0.01), door, { at: [-0.18, 0.205, 0.293] });
  for (let i = 0; i < 6; i++) b.add(box(0.26, 0.006, 0.006), K.steelDark, { at: [-0.18, 0.09 + i * 0.045, 0.3] });
  b.add(box(0.26, 0.27, 0.01), col('#2a2622'), { at: [0.16, 0.205, 0.292] })
    .add(box(0.26, 0.05, 0.014), door, { at: [0.16, 0.315, 0.296] })                    // the rolled-up door
    .add(box(0.12, 0.09, 0.01), K.ember, { glow: 1.2, at: [0.2, 0.13, 0.294] })           // a welder's glow
    .add(ball(0.018, 0), col('#cfe8ff'), { glow: 3, at: [0.17, 0.14, 0.29] })
    .add(box(0.08, 0.06, 0.06), K.steelDark, { at: [0.08, 0.1, 0.27] });                 // a workbench
  for (const x of [-0.33, -0.03, 0.31]) b.add(box(0.012, 0.28, 0.02), blockDark, { at: [x, 0.21, 0.3] });   // piers between the bays
  // A side door and a band of shop windows down each side.
  b.add(box(0.01, 0.17, 0.08), K.woodDark, { at: [0.425, 0.165, 0.15] });
  for (const s of [-1, 1]) {
    for (const z of [-0.22, -0.06, 0.08]) {
      b.add(box(0.01, 0.06, 0.1), z === -0.06 ? K.window : K.pane, { glow: z === -0.06 ? 0.5 : 0, at: [s * 0.425, 0.33, z] });
    }
  }
  flag(b, 0.36, 0.47, 0.2, 0.22);
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
    SiegeWorkshop: badger ? washburn : siegeShed,
  };
  const slotOf = (slot) => ((slot % 16) + 16) % 16;
  return {
    units: {
      Worker: { geo: worker(badger), height: 0.48 },
      Soldier: { geo: soldier(badger), height: 0.52, scale: 1.08 },
      Archer: { geo: archer(badger), height: 0.52 },
      Scout: { geo: scout(badger), height: 0.7 },
      // A vehicle, not a walker: the view rolls it along instead of striding (see updateUnits).
      Trebuchet: { geo: badger ? snowMobile() : trebuchet(), height: 0.6, scale: 1.15, vehicle: true },
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
    shield: roundShield(),
    scaffold: scaffold(),
    shell: shell(badger),
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
        attribute float team; attribute float glow; attribute vec2 limb;
        attribute vec3 iTeam; attribute float iFlash; attribute float iCut; attribute vec3 iWalk; attribute vec2 iDraw;
        varying float vTeam; varying float vGlow; varying vec3 vTeamCol; varying float vFlash; varying float vCut; varying float vLocalY;`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
        vTeam = team; vGlow = glow; vTeamCol = iTeam; vFlash = iFlash; vCut = iCut; vLocalY = position.y;
        // Swing limbs about the hip or shoulder: iWalk = (phase, leg swing, arm swing). A negative arm
        // swing raises both arms together, up to that many radians (a pickaxe, hammer or sword chop).
        float kind = abs(limb.x);
        if (kind > 6.5) {
          // Siege engines. The trebuchet's arm (7) and the stone in its sling (9) turn about the axle,
          // whose height is limb.y, by iDraw.x radians; iDraw.y = 1 while the stone is away. The Snow
          // Mobile's cannon (8) slides back along its barrel by iDraw.x when it fires.
          if (kind > 7.5 && kind < 8.5) {
            transformed += vec3(0.0, -0.025, -0.075) * iDraw.x;
          } else {
            float c = cos(iDraw.x), s = sin(iDraw.x);
            vec3 p = transformed - vec3(0.0, limb.y, 0.0);
            transformed = vec3(p.x, p.y * c - p.z * s, p.y * s + p.z * c) + vec3(0.0, limb.y, 0.0);
            if (kind > 8.5 && iDraw.y > 0.5) transformed = vec3(0.0, limb.y, 0.0);
          }
        } else if (kind > 3.5) {
          // An archer's bow (4), string (5) and nocked arrow (6). iDraw = (raise, pull): the bow comes up
          // from the hip to aim out front, the string bends back toward the archer, and the arrow on it
          // slides back with the string and only shows while there's an arrow to loose.
          transformed += vec3(0.03, 0.07, 0.17) * iDraw.x;
          if (kind > 4.5 && kind < 5.5) transformed.z -= iDraw.y * 0.1 * max(0.0, 1.0 - abs(position.y - 0.22) / 0.145);
          if (kind > 5.5) {
            transformed.z -= iDraw.y * 0.1;
            if (iDraw.x < 0.5 || iDraw.y < 0.04) transformed = vec3(-0.13, 0.22, -0.06);
          }
        } else if (limb.x != 0.0) {
          float pivot = kind < 1.5 ? 0.13 : (kind < 2.5 ? 0.28 : 0.19);
          bool isArm = kind > 1.5 && kind < 2.5;
          float ang = isArm && iWalk.z < 0.0
            ? (sin(iWalk.x) * 0.5 + 0.5) * iWalk.z
            : sin(iWalk.x) * (isArm ? -iWalk.z : iWalk.y) * sign(limb.x);
          float c = cos(ang), s = sin(ang);
          vec3 hinge = vec3(0.0, pivot, limb.y);
          vec3 p = transformed - hinge;
          transformed = vec3(p.x, p.y * c - p.z * s, p.y * s + p.z * c) + hinge;
        }`);
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', `#include <common>
        varying float vTeam; varying float vGlow; varying vec3 vTeamCol; varying float vFlash; varying float vCut; varying float vLocalY;`)
      .replace('#include <clipping_planes_fragment>', `#include <clipping_planes_fragment>
        if (vLocalY > vCut) discard;`)
      .replace('#include <color_fragment>', `#include <color_fragment>
        diffuseColor.rgb = mix(diffuseColor.rgb, diffuseColor.rgb * vTeamCol, vTeam);
        // Contact shading: models darken toward the ground, so they sit in the land rather than on it.
        diffuseColor.rgb *= mix(0.72, 1.0, smoothstep(-0.02, 0.16, vLocalY));`)
      .replace('#include <emissivemap_fragment>', `#include <emissivemap_fragment>
        totalEmissiveRadiance += diffuseColor.rgb * vGlow * 2.6;
        totalEmissiveRadiance += vec3(1.0, 0.45, 0.3) * vFlash * 1.8;
        float seam = 1.0 - smoothstep(0.0, 0.035, vCut - vLocalY);
        totalEmissiveRadiance += mix(vec3(1.0, 0.8, 0.4), vTeamCol, 0.4) * seam * step(vCut, 50.0) * 3.0;`);
  };
  mat.customProgramCacheKey = () => 'netrts-entity';
  return mat;
}

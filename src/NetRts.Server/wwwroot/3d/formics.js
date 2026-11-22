// 3D match view: the Formics (the hardest house bot) aren't badgers or knights but a hive of insects.
// Whatever the theme, a seat the Formics play draws its units with these models instead: chitin bodies
// with the player's color on the abdomen and carapace, glowing eyes, four walking legs and two forelimbs.
// They use the same rig as the theme's units (kit.js `limb`): legs swing from the hip (1), forelimbs from
// the shoulder (2) so they dig, strike and cheer like arms, a flyer's legs dangle like a mount's (3), and
// the siege beetle's cannon kicks back along its barrel (8) like the Snow Mobile's.
//
// One tile is one world unit; models stand on y = 0 and face +z.
import * as THREE from 'three';
import { col, K, Builder, cyl, ball, cone, box } from './kit.js';
import { houseBot } from '../util.js';

const C = {
  chitin: col('#2a241e'), shell: col('#4a3d2e'), sheen: col('#55643a'), eye: col('#3fd11f'),
  acid: col('#9dff3a'), bone: col('#dcd2ae'), wing: col('#cfe6dc'),
};
const UP = new THREE.Vector3(0, 1, 0);

/** The seats the Formics play, from a frame's players. */
export function formicSlots(players) {
  return new Set((players || []).filter((p) => houseBot(p.name) === 'formics').map((p) => p.slot));
}

/** A tapering rod from one point to another (a leg or antenna segment). */
function rod(b, from, to, r, color, opts = {}) {
  const a = new THREE.Vector3(...from), d = new THREE.Vector3(...to).sub(a);
  const e = new THREE.Euler().setFromQuaternion(new THREE.Quaternion().setFromUnitVectors(UP, d.clone().normalize()));
  return b.add(cyl(r * 0.75, r, d.length(), 5), color, { ...opts, at: a.addScaledVector(d, 0.5).toArray(), rot: [e.x, e.y, e.z] });
}

/** A jointed limb through the points, with a knob at each joint; x is mirrored for the right side (side = -1). */
function limb(b, points, r, color, side, opts = {}) {
  const p = points.map(([x, y, z]) => [x * side, y, z]);
  for (let i = 1; i < p.length; i++) {
    rod(b, p[i - 1], p[i], r * (1 - i * 0.15), color, opts);
    if (i < p.length - 1) b.add(ball(r * 1.05, 0), color, { ...opts, at: p[i] });
  }
  return p[p.length - 1];
}

/** Big compound eyes, mandibles and feelers on a head centered at (0, y, z). */
function face(b, y, z, { mandible = 1 } = {}) {
  for (const s of [-1, 1]) {
    b.add(ball(0.024, 1), C.eye, { glow: 0.45, at: [s * 0.036, y + 0.01, z + 0.028], scale: [0.8, 1, 1] })
      .add(cone(0.012 * mandible, 0.05 * mandible, 4), C.bone, { at: [s * 0.02, y - 0.03, z + 0.05], rot: [Math.PI / 2 + 0.3, 0, -s * 0.5] });
    limb(b, [[0.014, y + 0.04, z + 0.02], [0.05, y + 0.13, z + 0.06], [0.085, y + 0.12, z + 0.12]], 0.006, C.chitin, s);
  }
}

/**
 * The upright body every walker shares: four splayed legs and a low thorax, the abdomen behind in the
 * player's color with dark bands, an upright torso and the head. abdomen scales the abdomen.
 */
function walker(b, { abdomen = 1, acid = false } = {}) {
  const r = 0.085 * abdomen, az = -0.12 - r;
  b.add(ball(0.055, 1), C.chitin, { at: [0, 0.15, -0.01], scale: [0.9, 0.75, 1.2] })            // hips
    .add(ball(0.026, 0), C.chitin, { at: [0, 0.165, -0.085] })                                   // waist
    .add(ball(r, 1), acid ? C.acid : K.teamShade, { team: acid ? 0 : 1, glow: acid ? 0.45 : 0, at: [0, 0.14 + r * 0.6, az], scale: [0.85, 0.8, 1.2] })
    .add(cone(0.02, 0.06, 5), C.chitin, { at: [0, 0.13 + r * 0.5, az - r * 1.25], rot: [-Math.PI / 2 - 0.3, 0, 0] });   // sting
  for (const dz of [-0.35, 0.3]) {
    b.add(cyl(r * 0.86, r * 0.86, 0.016, 10), acid ? K.teamShade : C.chitin, { team: acid ? 1 : 0, at: [0, 0.14 + r * 0.6, az + dz * r], rot: [Math.PI / 2, 0, 0], scale: [1, 1, 0.94] });
  }
  b.add(ball(0.05, 1), C.shell, { at: [0, 0.24, 0.035], rot: [0.25, 0, 0], scale: [0.95, 1.5, 0.85] })   // torso
    .add(ball(0.05, 1), C.chitin, { at: [0, 0.345, 0.075], scale: [1, 0.9, 1.15] });                     // head
  face(b, 0.345, 0.075);
  // Front legs step with the far rear leg, like a trotting horse.
  for (const s of [-1, 1]) {
    limb(b, [[0.04, 0.15, 0.02], [0.11, 0.19, 0.06], [0.15, 0.005, 0.1]], 0.016, C.chitin, s, { limb: s, joint: 0.02 });
    limb(b, [[0.04, 0.15, -0.04], [0.11, 0.185, -0.09], [0.155, 0.005, -0.14]], 0.016, C.chitin, s, { limb: -s, joint: -0.04 });
  }
}

/** The forelimbs, hanging forward from the shoulders; tool(side, hand) adds whatever each one holds. */
function forelimbs(b, tool) {
  for (const s of [-1, 1]) {
    const opts = { limb: s * 2, joint: 0.04 };
    const hand = limb(b, [[0.055, 0.28, 0.05], [0.1, 0.21, 0.09], [0.085, 0.19, 0.18]], 0.013, C.shell, s, opts);
    tool(s, hand, opts);
  }
}

function drone() {
  const b = new Builder();
  walker(b);
  forelimbs(b, (s, [x, y, z], opts) => {
    for (const d of [-1, 1]) b.add(cone(0.01, 0.045, 4), C.bone, { ...opts, at: [x + d * 0.008, y - 0.01, z + 0.02], rot: [Math.PI / 2 + 0.6, 0, 0] });
  });
  return b.build();
}

function warrior() {
  const b = new Builder();
  walker(b, { abdomen: 1.1 });
  // Armor: a crest down the head and back, spiked shoulders and a chest plate, all in the player's color.
  b.add(box(0.014, 0.05, 0.11), K.team, { team: 1, at: [0, 0.39, 0.06], rot: [-0.3, 0, 0] })
    .add(box(0.08, 0.1, 0.02), K.team, { team: 1, at: [0, 0.25, 0.078], rot: [0.25, 0, 0] });
  for (const s of [-1, 1]) {
    b.add(ball(0.03, 0), C.shell, { at: [s * 0.06, 0.29, 0.04] })
      .add(cone(0.013, 0.07, 4), C.bone, { at: [s * 0.08, 0.33, 0.03], rot: [0, 0, -s * 0.6] })
      .add(cone(0.016, 0.07, 4), C.bone, { at: [s * 0.022, 0.31, 0.15], rot: [Math.PI / 2 + 0.2, 0, -s * 0.7] });   // big mandibles
  }
  // Scythes: a long hooked blade along each forearm.
  forelimbs(b, (s, [x, y, z], opts) => {
    b.add(box(0.012, 0.03, 0.2), C.bone, { ...opts, at: [x, y + 0.04, z + 0.02], rot: [-0.5, 0, 0] })
      .add(box(0.004, 0.012, 0.18), K.team, { ...opts, team: 1, at: [x, y + 0.058, z + 0.02], rot: [-0.5, 0, 0] })
      .add(cone(0.01, 0.06, 4), C.bone, { ...opts, at: [x, y + 0.08, z + 0.13], rot: [0.6, 0, 0] });
  });
  return b.build();
}

function spitter() {
  const b = new Builder();
  walker(b, { abdomen: 1.35, acid: true });
  // A spout under the jaw and drips of acid down the sac.
  b.add(cyl(0.012, 0.02, 0.05, 6), C.sheen, { at: [0, 0.31, 0.135], rot: [Math.PI / 2 + 0.4, 0, 0] })
    .add(ball(0.01, 0), C.acid, { glow: 1.2, at: [0, 0.3, 0.16] })
    .add(box(0.07, 0.016, 0.05), K.team, { team: 1, at: [0, 0.37, 0.06], rot: [-0.2, 0, 0] });
  forelimbs(b, (s, [x, y, z], opts) => b.add(cone(0.008, 0.035, 4), C.bone, { ...opts, at: [x, y - 0.005, z + 0.025], rot: [Math.PI / 2 + 0.4, 0, 0] }));
  return b.build();
}

function flyer() {
  // A hovering flyer: a long banded tail, four wings that sweep as it flies (limb 2) and legs dangling
  // underneath (limb 3).
  const b = new Builder();
  b.add(ball(0.05, 1), C.shell, { at: [0, 0.33, 0.02], scale: [0.9, 0.85, 1.3] })
    .add(ball(0.045, 1), C.chitin, { at: [0, 0.345, 0.11] })
    .add(box(0.02, 0.012, 0.09), K.team, { team: 1, at: [0, 0.373, 0.02] });
  face(b, 0.345, 0.11, { mandible: 0.7 });
  rod(b, [0, 0.33, -0.04], [0, 0.31, -0.32], 0.03, K.teamShade, { team: 1 });
  for (let i = 0; i < 4; i++) b.add(ball(0.03 - i * 0.004, 0), C.chitin, { at: [0, 0.327 - i * 0.006, -0.08 - i * 0.065], scale: [1.08, 1.08, 0.5] });
  b.add(cone(0.012, 0.05, 4), C.chitin, { at: [0, 0.305, -0.345], rot: [-Math.PI / 2, 0, 0] });
  for (const s of [-1, 1]) {
    for (const [z, w, sign] of [[0.045, 1, 1], [-0.025, 0.9, -1]]) {
      b.add(ball(0.1, 1), C.wing, { glow: 0.2, limb: s * sign * 2, joint: z, at: [s * 0.14 * w, 0.37, z], scale: [1.5 * w, 0.05, 0.36], rot: [0, s * 0.15 * sign, 0] });
    }
    for (const z of [0.06, 0.01, -0.04]) {
      limb(b, [[0.03, 0.3, z], [0.075, 0.22, z + 0.03], [0.06, 0.12, z + 0.02]], 0.008, C.chitin, s, { limb: (s < 0 ? 3 : -3) * (z > 0 ? 1 : -1), joint: z });
    }
  }
  return b.build();
}

/** Where the siege beetle's shot leaves its cannon (model units, facing +z). */
const MUZZLE = { y: 0.47, z: 0.25 };

function siegeBeetle() {
  // A siege beetle: a broad armored shell in the player's color, six stubby legs, a horned head, and an
  // acid sac on its back feeding a cannon that kicks back when it fires (limb 8).
  const b = new Builder();
  b.add(ball(0.2, 1), C.chitin, { at: [0, 0.13, -0.02], scale: [0.95, 0.5, 1.2] });
  for (const s of [-1, 1]) {
    b.add(ball(0.2, 1), K.teamShade, { team: 1, at: [s * 0.075, 0.19, -0.05], scale: [0.55, 0.5, 1.15] });
    for (const z of [0.12, 0, -0.14]) limb(b, [[0.12, 0.1, z], [0.22, 0.12, z + 0.03], [0.25, 0.005, z + 0.05]], 0.018, C.chitin, s);
  }
  b.add(box(0.012, 0.03, 0.4), C.chitin, { at: [0, 0.29, -0.06] })                               // the seam
    .add(ball(0.08, 1), C.chitin, { at: [0, 0.14, 0.23], scale: [1, 0.85, 1] })
    .add(cone(0.03, 0.14, 5), C.bone, { at: [0, 0.22, 0.29], rot: [0.9, 0, 0] });                 // horn
  face(b, 0.14, 0.24, { mandible: 1.5 });
  b.add(ball(0.09, 1), C.acid, { glow: 0.5, at: [0, 0.33, -0.13], scale: [1, 0.8, 1.1] });
  const gun = { limb: 8 };
  rod(b, [0, 0.31, -0.04], [0, MUZZLE.y, MUZZLE.z], 0.045, C.shell, gun);
  b.add(cyl(0.05, 0.05, 0.025, 8), C.chitin, { ...gun, at: [0, 0.35, 0.03], rot: [0.95, 0, 0] })
    .add(cyl(0.04, 0.04, 0.02, 8), C.acid, { ...gun, glow: 0.8, at: [0, MUZZLE.y - 0.005, MUZZLE.z - 0.005], rot: [0.95, 0, 0] });
  return b.build();
}

/** The Formics' unit models, shaped like the theme's (buildModels): geometry, health-bar height, scale. */
export function formicUnits() {
  return {
    Worker: { geo: drone(), height: 0.44, scale: 0.95 },
    Soldier: { geo: warrior(), height: 0.46, scale: 1.2 },
    Archer: { geo: spitter(), height: 0.48 },
    Scout: { geo: flyer(), height: 0.5 },
    Trebuchet: { geo: siegeBeetle(), height: 0.5, scale: 1.05, vehicle: true, muzzle: MUZZLE },
  };
}

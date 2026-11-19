// 3D match view: building armor on show. A player who has taken Intro to Construction (Fortify1) gets a stone
// plinth wall around the base of each of their finished buildings; Structural Engineering (Fortify2) adds stepped
// stone buttresses at the corners, capped in bronze, a third as tall as the building. Both are drawn around the
// building's own footprint, so a slim Bell Tower gets a slim one.
import * as THREE from 'three';

const M = new THREE.Matrix4();
const S = new THREE.Vector3();
const WHITE = new THREE.Color('#ffffff');

/** Each player's building-armor level (0, 1 or 2), by slot, from the state's upgrades. */
export function fortifyLevels(state) {
  const levels = new Map();
  for (const p of state?.players || []) {
    const u = p.upgrades || [];
    levels.set(p.slot, u.includes('Fortify2') ? 2 : u.includes('Fortify1') ? 1 : 0);
  }
  return levels;
}

/** How wide a building model is, against the 0.94 square the fortifications are made for. */
export function footprint(geo) {
  const bb = geo.boundingBox;
  return Math.min(1.1, Math.max(0.55, Math.max(bb.max.x - bb.min.x, bb.max.z - bb.min.z) / 0.94));
}

/** Adds one building's fortifications at pos (turned by q) to the stone and steel layers. */
export function pushFortify(layers, level, pos, q, scale, width, height) {
  if (level >= 1) layers.fortStone.push(M.compose(pos, q, S.set(scale * width, scale, scale * width)), WHITE);
  if (level >= 2) layers.fortButtress.push(M.compose(pos, q, S.set(scale * width, scale * height, scale * width)), WHITE);
}

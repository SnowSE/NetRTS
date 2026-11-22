// 3D match view: how siege engines move when they fire, and where their shots leave from. The trebuchet
// (models.js) swings its arm over its axle; a gun (the Snow Mobile, the Formics' siege beetle: a unit
// model with a `muzzle`) kicks back along its barrel.
import { smoothstep } from './noise.js';

// The trebuchet's arm swings this far over (radians) to let its stone go, from rest at this angle about
// its axle; the arm's tip is this far out.
const ARM_UP = 2.5, ARM_REST = -2.44, ARM_AXLE = 0.36, ARM_TIP = 0.43;
const SIEGE_COOLDOWN = 8;   // GameRules: a trebuchet fires every 8 ticks

/**
 * A siege engine's moving parts (iDraw, see models.js) `since` ms after it fired, with ticks `interval` ms
 * apart. The trebuchet's arm swings over to let the stone go right at the shot's launch, quivers at the
 * top, then is winched back down before the next shot: [arm angle, 1 while the stone is away]. A gun kicks
 * back and runs out again: [recoil, 0].
 */
export function siegePose(def, since, interval) {
  if (def.muzzle) return [since < 0 || since > 1200 ? 0 : since < 40 ? since / 40 : Math.exp(-(since - 40) / 220), 0];
  const reload = Math.max(400, SIEGE_COOLDOWN * interval);
  const swing = Math.min(320, reload * 0.15), hold = Math.min(450, reload * 0.2), back = reload * 0.85;
  const t = since + swing;   // the arm starts over before the stone lets go
  if (t < 0 || t >= back) return [0, 0];
  if (t < swing) return [ARM_UP * (t / swing) ** 2, 0];
  if (t < swing + hold) {
    const w = (t - swing) / hold;
    return [ARM_UP + Math.sin(w * Math.PI * 3) * 0.12 * (1 - w), 1];
  }
  return [ARM_UP * (1 - smoothstep(0, 1, (t - swing - hold) / (back - swing - hold))), 1];
}

/** Where a siege engine's shot leaves it, in model units facing +z: the gun's muzzle, or the arm's tip at the top of its swing. */
export function muzzleOf(def) {
  if (def.muzzle) return def.muzzle;
  const ang = ARM_REST - ARM_UP;
  return { y: ARM_AXLE + Math.sin(ang) * ARM_TIP, z: Math.cos(ang) * ARM_TIP };
}

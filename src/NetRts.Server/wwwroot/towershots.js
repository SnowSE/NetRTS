// Guard tower fire, for the match views (2D map, 3D view and its commentary). Frames list each tower shot
// (towerShots); replays recorded before they did only show a unit losing health, so for those a shot is
// guessed: a unit hurt with nothing attacking it and no shell landing on it, from an enemy tower in reach.

/** GameRules: guard towers reach 5 tiles in every direction, diagonals included. */
export const TOWER_RANGE = 5;

/** The completed enemy guard tower nearest u (a unit) and within reach in frame s, or null. */
export function towerInReach(s, u) {
  let best = null, bd = Infinity;
  for (const b of s.buildings) {
    if (b.type !== 'GuardTower' || !b.completed || b.owner === u.owner) continue;
    const d = Math.max(Math.abs(b.x - u.x), Math.abs(b.y - u.y));
    if (d <= TOWER_RANGE && d < bd) { best = b; bd = d; }
  }
  return best;
}

/**
 * The tower shots of the tick frame s shows ({ towerId, owner, fromX, fromY, targetId, x, y }): its own, or
 * for an older frame the ones worked out from prev (the frame before it). A target the shot killed isn't
 * in s.units.
 */
export function towerShotsOf(prev, s) {
  if (s.towerShots) return s.towerShots;
  if (!prev) return [];
  const before = new Map(prev.units.map((u) => [u.id, u.hp]));
  const targeted = new Set(s.units.filter((u) => u.activity === 'Attacking').map((u) => u.targetId));
  const shelled = new Set((prev.shells || []).filter((sh) => sh.landsTick === s.tick).map((sh) => sh.y * 4096 + sh.x));
  const shots = [];
  for (const u of s.units) {
    if (!(u.hp < (before.get(u.id) ?? 0)) || targeted.has(u.id) || shelled.has(u.y * 4096 + u.x)) continue;
    const t = towerInReach(s, u);
    if (t) shots.push({ towerId: t.id, owner: t.owner, fromX: t.x, fromY: t.y, targetId: u.id, x: u.x, y: u.y });
  }
  return shots;
}

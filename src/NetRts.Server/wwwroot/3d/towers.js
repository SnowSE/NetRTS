// 3D match view: guard tower fire (which shots: ../towershots.js).
import * as THREE from 'three';

const AIM_UP = new THREE.Vector3(0, 0.25, 0);   // at the body, not the feet

/**
 * A tower shot (towerShotsOf): a fireball from the tower's battlements to the target where it stands at the
 * end of the tick, or where it fell if the shot killed it. Not when both ends are in the viewer's fog.
 * q: the view's "play this when the picture reaches the tick".
 */
export function drawTowerShot(view, t, q) {
  if (view.vision != null && t.owner !== view.vision && !view.visibleTo(t.x, t.y) && !view.visibleTo(t.fromX, t.fromY)) return;
  const a = view.units.get(t.targetId);
  const at = view.ground(a?.tx ?? t.x + 0.5, a?.tz ?? t.y + 0.5).add(AIM_UP);
  const top = view.ground(t.fromX + 0.5, t.fromY + 0.5)
    .add(new THREE.Vector3(0, view.models.building('GuardTower', t.owner).height * view.buildingScale - 0.1, 0));
  q(() => view.effects.bolt(top, at, Math.min(0.45, view.interval * 0.0007 + 0.1)));
}

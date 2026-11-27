// The farm (spring rules): a fenced plot of tilled rows. In the Snow College theme it's a compost heap
// with grubs working through it; otherwise a little field of golden ore sprouts.
import { K, Builder, box, ball, cone, dome } from './kit.js';

export function farm(badger) {
  const b = new Builder();
  b.add(box(0.86, 0.06, 0.86), K.soil, { at: [0, 0.03, 0] });
  for (const z of [-0.3, -0.1, 0.1, 0.3]) b.add(box(0.7, 0.05, 0.08), K.woodDark, { at: [0, 0.08, z] });
  // A low rail fence in the team's color.
  for (const [x, z, w, d] of [[0, -0.42, 0.86, 0.03], [0, 0.42, 0.86, 0.03], [-0.42, 0, 0.03, 0.86], [0.42, 0, 0.03, 0.86]]) {
    b.add(box(w, 0.03, d), K.team, { team: 1, at: [x, 0.15, z] });
  }
  for (const x of [-0.42, 0.42]) for (const z of [-0.42, 0.42]) b.add(box(0.05, 0.2, 0.05), K.wood, { at: [x, 0.1, z] });
  if (badger) {
    b.add(dome(0.24, 10, 5), K.soil, { at: [0.12, 0.08, -0.05], scale: [1, 0.8, 1] });
    for (let i = 0; i < 6; i++) b.add(ball(0.035, 0), K.grub, { glow: 0.25, at: [0.02 + (i % 3) * 0.09, 0.2 + (i % 2) * 0.04, -0.12 + Math.floor(i / 3) * 0.12], rot: [i, i * 2, 0] });
  } else {
    for (const z of [-0.3, -0.1, 0.1, 0.3]) for (const x of [-0.24, 0, 0.24]) b.add(cone(0.03, 0.1, 4), K.ore, { glow: 0.5, at: [x, 0.15, z] });
  }
  return b.build();
}

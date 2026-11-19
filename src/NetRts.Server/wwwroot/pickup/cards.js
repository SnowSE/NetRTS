// How a pick-up item looks: the card in the hand (picture or course number, cost, name, stats) and the
// small picture beside a step or a choice.
import { esc } from '../util.js';
import { item, art, course, tcls } from './items.js';

const CLASS_ICON = '<path d="M9 3h6"/><path d="M10 3v6l-5.6 9.7A1.5 1.5 0 0 0 5.7 21h12.6a1.5 1.5 0 0 0 1.3-2.3L14 9V3"/><path d="M7.5 15h9"/>';

/** The item's picture at about size px, or the flask for a class. */
export function icon(id, size = 20) {
  if (item(id)?.type !== 'research') return `<img class="ic thumb" src="${art(id)}" width="${size + 8}" height="${size + 8}" alt="">`;
  return `<svg class="ic" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${CLASS_ICON}</svg>`;
}

export const artHTML = (id) => (course(id) ? `<span class="course">${course(id)}</span>` : `<img src="${art(id)}" alt="" loading="lazy">`);

function statsHTML(id) {
  const it = item(id), s = it.stats;
  if (!s) return `<span class="fx">${esc(it.blurb)}</span>`;
  return `<span class="stats">${[['Health', s.hp], ['Damage', s.atk ?? '–'], ['Armor', s.arm], ['Range', s.rng ?? '–']]
    .map(([l, v]) => `<i><b>${v}</b>${l}</i>`).join('')}</span>`;
}

/** A card in the hand; why: the strip over the picture saying why it can't be played. */
export function card(id, disabled, why) {
  const it = item(id);
  const long = it.name.length > 10 ? ' long' : '';
  return `<button type="button" class="card t-${tcls(id)}" data-a="card" data-v="${id}" ${disabled ? 'disabled' : ''} aria-label="${esc(`${it.name}, ${it.cost} grubs${why ? `: ${why}` : ''}`)}">`
    + `<span class="art">${artHTML(id)}${why ? `<span class="lock">${esc(why)}</span>` : ''}</span>`
    + `<span class="cost">${it.cost}</span><span class="nm${long}">${esc(it.name)}</span>${statsHTML(id)}</button>`;
}

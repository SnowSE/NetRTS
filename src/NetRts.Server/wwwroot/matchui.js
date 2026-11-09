// Pieces of a match's UI that the spectator page and the Block Bot cockpit both show, each in its own
// layout: the clock (tick, tick limit, time left) with its progress bar, and what a building is and is doing.
import { html, fmt } from './util.js';
import { say, unitName, upgradeName, buildingName, buildingRole, ORE } from './lore.js';

/**
 * Where a match is, from its live state and/or summary: the tick (or atLeast, if that's later: a Block
 * Bot status can be a tick ahead of the last frame), the tick limit, how far along (0-100) and roughly
 * how long is left at the match's pace, in ms. A match can also end sooner, by elimination.
 */
export function matchClock({ state, summary, atLeast = 0 } = {}) {
  const tick = Math.max(state?.tick ?? summary?.tick ?? 0, atLeast);
  const max = state?.maxTicks ?? summary?.maxTicks ?? 0;
  const ms = state?.tickIntervalMs ?? summary?.tickIntervalMs ?? 1000;
  return { tick, max, pct: max ? Math.min(100, (tick / max) * 100) : 0, msLeft: Math.max(0, max - tick) * ms };
}

/** 1:23, or 1:02:03 */
export function clockText(ms) {
  const s = Math.round(ms / 1000), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), ss = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

/**
 * A bar filled pct percent. With label (and now / max) it's a progressbar for screen readers; without,
 * it's decoration beside numbers that say the same. cls: the page's class for it.
 */
export function progressBar(pct, { cls = 'progress', label = null, now = null, max = null } = {}) {
  const fill = html`<span style="width:${Math.max(0, Math.min(100, pct)).toFixed(2)}%"></span>`;
  return label
    ? html`<div class="${cls}" role="progressbar" aria-label="${label}" aria-valuemin="0" aria-valuemax="${max ?? 100}" aria-valuenow="${now ?? Math.round(pct)}">${fill}</div>`
    : html`<div class="${cls}" aria-hidden="true">${fill}</div>`;
}

/** A building's name (per team in the Snow College theme), what it's for, and its health as a percentage. */
export function buildingFacts(b) {
  return {
    name: buildingName(b.type, b.owner),
    role: buildingRole(b.type),
    hpPct: b.maxHp ? Math.max(0, Math.min(100, (b.hp / b.maxHp) * 100)) : 0,
  };
}

/**
 * What a building is busy with, a line each: being built, training (queue 'all': every unit in the queue
 * with its progress; 'count': the first, and how many wait behind it) and research.
 */
export function buildingWork(b, { queue = 'all' } = {}) {
  const lines = [];
  if (!b.completed) lines.push(html`Under construction, ${b.constructionPercent}%`);
  const q = b.production || [];
  if (q.length) {
    lines.push(queue === 'count'
      ? html`${say('Producing', 'Training')} ${unitName(q[0].unitType)} ${q[0].percent}%${q.length > 1 ? html` <span class="muted">(+${q.length - 1} queued)</span>` : ''}`
      : html`${say('Producing', 'Training')} ${q.map((p) => `${unitName(p.unitType)} ${p.percent}%`).join(', ')}`);
  }
  if (b.research) lines.push(html`${say('Researching', 'Studying')} ${upgradeName(b.research.upgrade)} ${b.research.percent}%`);
  return lines;
}

/** A building type's numbers from GET /api/v1/rules (rules), as [label, value] pairs. */
export function buildingStats(type, rules) {
  const r = rules?.buildings?.find((x) => x.type === type);
  if (!r) return [];
  return [
    ['Armor', r.armor],
    r.damage ? ['Attack', `${r.damage} damage, range ${r.range}`] : null,
    r.storageBonus ? ['Stores', `${fmt(r.storageBonus)} ${ORE}`] : null,
    r.produces?.length ? ['Trains', r.produces.map((t) => unitName(t)).join(', ')] : null,
    r.canResearch ? ['Classes', say('researches upgrades', 'teaches the classes')] : null,
  ].filter(Boolean);
}

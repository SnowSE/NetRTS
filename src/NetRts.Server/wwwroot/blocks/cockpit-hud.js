// The cockpit's overlays on the 3D view: the stats rail (the clock, a trend line per stat over the last
// minute, the scoreboard), the building you clicked, and the sound and picture-quality icons.
import { html, displayName, slotVar, fmt, rankPlayers } from '../util.js';
import { ORE } from '../lore.js';
import { matchClock, clockText, progressBar, buildingFacts, buildingWork, buildingStats } from '../matchui.js';

const HISTORY_TICKS = 60;

/** Your stats, a point per tick seen, over the last minute: { tick, grubs, workers, fighters, buildings }, oldest first. */
export class StatHistory {
  constructor() { this.points = []; }
  add(status) {
    const s = status?.stats;
    const h = this.points;
    if (!s || (h.length && h[h.length - 1].tick >= status.tick)) return;
    h.push({ tick: status.tick, grubs: s.grubs, workers: s.workers, fighters: s.fighters, buildings: s.buildings });
    while (h.length > 2 && h[0].tick < status.tick - HISTORY_TICKS) h.shift();
  }
}

/** tone: the --hud-<tone> colour (styles.css .hud-dark) of its number and line. */
function trendTile(history, status, key, label, tone) {
  const h = history.points;
  const now = h[h.length - 1]?.[key] ?? status?.stats?.[key] ?? 0;
  const first = h[0], last = h[h.length - 1];
  const span = first && last ? last.tick - first.tick : 0;
  const perMin = span >= 5 ? Math.round(((last[key] - first[key]) * 60) / span) : null;
  const W = 112, H = 20;
  let line = '', area = '', mark = null;
  if (h.length >= 2) {
    const vals = h.map((p) => p[key]);
    const lo = Math.min(...vals), hi = Math.max(...vals), range = Math.max(1, hi - lo);
    const x = (t) => ((t - first.tick) / Math.max(1, span)) * W;
    line = h.map((p) => `${x(p.tick).toFixed(1)},${(H - 2 - ((p[key] - lo) / range) * (H - 4)).toFixed(1)}`).join(' ');
    area = `0,${H} ${line} ${W},${H}`;
    // Where the newest version went live, when that was within the last minute.
    const v = status?.versions?.[status.versions.length - 1];
    if (v && v.version > 1 && v.tick > first.tick) mark = x(v.tick).toFixed(1);
  }
  const rate = perMin == null ? '' : perMin === 0 ? 'steady' : `${perMin > 0 ? '▲ +' : '▼ −'}${fmt(Math.abs(perMin))}/min`;
  const rateClass = perMin == null || perMin === 0 ? '' : perMin > 0 ? ' up' : ' down';
  return html`<div class="bb-tile" style="--tile:var(--hud-${tone})">
    <span class="bb-tile-label">${label}</span>
    <span class="bb-tile-row"><span class="bb-tile-value">${fmt(now)}</span><span class="bb-tile-rate${rateClass}">${rate}</span></span>
    <svg width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${label} over the last minute, now ${fmt(now)}">
      ${line ? html`<polyline points="${area}" fill="currentColor" fill-opacity="0.14" stroke="none"/><polyline points="${line}" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>` : ''}
      ${mark ? html`<line class="bb-tile-mark" x1="${mark}" y1="0" x2="${mark}" y2="${H}" stroke-width="1" stroke-dasharray="2 2"><title>v${status.versions[status.versions.length - 1].version} went live</title></line>` : ''}
    </svg>
  </div>`;
}

/** Everyone when it fits; with lots of players, the top four, then you. */
function scoreboard(state, status) {
  const players = rankPlayers(state?.players).map(({ p, total, rank }) => ({ ...p, total, rank, me: p.slot === status?.slot }));
  if (!players.length) return '';
  let rows = players;
  if (players.length > 5) {
    const top = players.slice(0, 4);
    rows = top.some((p) => p.me) ? players.slice(0, 5) : [...top, null, players.find((p) => p.me)].filter((p) => p !== undefined);
  }
  return html`<div class="bb-tile bb-board">
    <span class="bb-tile-label">Scoreboard <span class="bb-board-count">${players.length} players</span></span>
    <ol>${rows.map((p) => p == null
      ? html`<li class="bb-board-gap" aria-hidden="true">⋯</li>`
      : html`<li class="${p.me ? 'me' : ''}${p.eliminated ? ' out' : ''}" style="--pc:${slotVar(p.slot)}"><span class="bb-board-rank">${p.rank}</span><span class="bb-board-name">${p.me ? 'You' : displayName(p.name)}</span><span class="bb-board-score">${fmt(p.total)}</span></li>`)}</ol>
  </div>`;
}

/** The tick, the tick limit and roughly how long until it (a match can also end sooner, by elimination). */
function clockTile(state, summary, status) {
  const { tick, max, pct, msLeft } = matchClock({ state, summary, atLeast: status?.tick ?? 0 });
  if (!max) return '';
  const done = summary?.status === 'Completed' || status?.matchStatus === 'Completed';
  return html`<div class="bb-tile bb-clock" title="The match ends at tick ${fmt(max)} if nobody is knocked out first. Then the highest score wins.">
    <span class="bb-tile-label">Tick <span>${done ? 'final' : `${clockText(msLeft)} left`}</span></span>
    <span class="bb-tile-value">${fmt(tick)}<small> / ${fmt(max)}</small></span>
    ${progressBar(pct, { cls: 'bb-clock-bar', label: 'Match progress', now: tick, max })}
  </div>`;
}

/** The stats rail: { state: the last frame, summary, status: the Block Bot's, history: a StatHistory }. */
export function hudHtml({ state, summary, status, history }) {
  return html`
    ${clockTile(state, summary, status)}
    ${trendTile(history, status, 'grubs', ORE[0].toUpperCase() + ORE.slice(1), 'accent')}
    ${trendTile(history, status, 'workers', 'Workers', 'workers')}
    ${trendTile(history, status, 'fighters', 'Fighters', 'fighters')}
    ${trendTile(history, status, 'buildings', 'Buildings', 'buildings')}
    ${scoreboard(state, status)}`;
}

/**
 * The building you clicked: what it is, whose (mine: yours; else its owner's name), its health, what it's
 * doing, and its numbers from the rules. live: false once it has been destroyed.
 */
export function buildingCardHtml(b, { live, mine, ownerName, rules }) {
  const facts = buildingFacts(b);
  const stats = buildingStats(b.type, rules);
  return html`
    <div class="bb-bcard-head">
      <h2>${facts.name}</h2>
      <button type="button" class="bb-bcard-x" data-bclose aria-label="Close">×</button>
    </div>
    <p class="bb-bcard-owner"><span class="bb-bcard-dot" style="--pc:${slotVar(b.owner)}" aria-hidden="true"></span>${mine ? 'Yours' : ownerName}</p>
    ${live ? html`
      <div class="bb-bcard-hp"><span>HP</span><b>${fmt(b.hp)} / ${fmt(b.maxHp)}</b></div>
      ${progressBar(facts.hpPct, { cls: `bb-clock-bar bb-hpbar${facts.hpPct < 34 ? ' low' : ''}` })}
      ${buildingWork(b, { queue: 'count' }).map((line) => html`<p>${line}</p>`)}`
      : html`<p class="bb-bcard-gone">Destroyed.</p>`}
    <p class="bb-bcard-role">${facts.role}</p>
    ${stats.length ? html`<dl class="bb-bcard-stats">${stats.map(([k, v]) => html`<div><dt>${k}</dt><dd>${v}</dd></div>`)}</dl>` : ''}
    ${live ? html`<button type="button" class="bb-bcard-zoom" data-bzoom>Zoom to it</button>` : ''}`;
}

/** Signal bars for the picture quality: level 1 to 3 filled. */
export const qualityIcon = (level) => html`<svg width="20" height="20" viewBox="0 0 24 24" aria-hidden="true">${[0, 1, 2].map((i) =>
  html`<rect x="${2 + i * 7.5}" y="${16 - i * 6}" width="5" height="${6 + i * 6}" rx="1" fill="${i < level ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="1.5"/>`)}</svg>`;

/** A speaker, with waves (on) or a cross (muted). */
export const soundIcon = (muted) => html`<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="${muted ? 'M16 9l5 6M21 9l-5 6' : 'M16 9.5a4 4 0 0 1 0 5M18.5 7a7.5 7.5 0 0 1 0 10'}"/></svg>`;

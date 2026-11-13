// Shared helpers: API access, escaping, player colours.
import { GAME, say, nickname } from './lore.js';

export class ApiError extends Error {
  constructor(status, code, message) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

/** fetch JSON from the API; throws ApiError with the server's {code, message} on failure. */
export async function api(path, options = {}) {
  const init = { ...options, headers: { accept: 'application/json', ...(options.headers || {}) } };
  if (options.body !== undefined && typeof options.body !== 'string') {
    init.body = JSON.stringify(options.body);
    init.headers['content-type'] = 'application/json';
  }
  let res;
  try {
    res = await fetch(path, init);
  } catch {
    throw new ApiError(0, 'NETWORK', `Could not reach the ${GAME} server. Check that it is running.`);
  }
  const text = await res.text();
  let body = null;
  if (text) {
    try { body = JSON.parse(text); } catch { body = null; }
  }
  if (!res.ok) {
    const code = body?.code || `HTTP_${res.status}`;
    let message = body?.message || res.statusText || 'Request failed.';
    if (res.status === 429) message = body?.message || 'Too many requests. Wait a moment and try again.';
    throw new ApiError(res.status, code, message);
  }
  return body;
}

const ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
export const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ESC[c]);

/** Tagged template that escapes interpolations unless wrapped with raw(). */
export function html(strings, ...values) {
  let out = strings[0];
  values.forEach((v, i) => {
    out += render(v) + strings[i + 1];
  });
  return { __html: out };
}
function render(v) {
  if (v == null || v === false) return '';
  if (Array.isArray(v)) return v.map(render).join('');
  if (typeof v === 'object' && '__html' in v) return v.__html;
  return esc(v);
}
export const raw = (s) => ({ __html: s });
export const setHtml = (el, h) => { el.innerHTML = h.__html ?? h; };

/**
 * Like setHtml, but keeps the elements that are still there and changes only what differs, so redrawing
 * often (a HUD each tick) doesn't throw away what's under the pointer: its tooltip, its hover.
 */
export function patchHtml(el, h) {
  const next = document.createElement('template');
  next.innerHTML = h.__html ?? h;
  patchChildren(el, next.content);
}

function patchChildren(el, want) {
  const have = [...el.childNodes];
  const wanted = [...want.childNodes];
  wanted.forEach((w, i) => {
    const old = have[i];
    if (!old || old.nodeType !== w.nodeType || old.nodeName !== w.nodeName) {
      if (old) old.replaceWith(w); else el.append(w);
      return;
    }
    if (w.nodeType !== Node.ELEMENT_NODE) {
      if (old.nodeValue !== w.nodeValue) old.nodeValue = w.nodeValue;
      return;
    }
    for (const a of [...old.attributes]) if (!w.hasAttribute(a.name)) old.removeAttribute(a.name);
    for (const a of [...w.attributes]) if (old.getAttribute(a.name) !== a.value) old.setAttribute(a.name, a.value);
    patchChildren(old, w);
  });
  have.slice(wanted.length).forEach((n) => n.remove());
}

/** Player colours: one per slot, up to 16 players. */
export const SLOT_COLOURS = 16;

/** CSS custom property name for a slot's colour. */
export const slotVar = (slot) => `var(--p${((slot % SLOT_COLOURS) + SLOT_COLOURS) % SLOT_COLOURS})`;

/** Reads the resolved player colours (theme aware) for the canvas. */
export function playerColours() {
  const cs = getComputedStyle(document.documentElement);
  return Array.from({ length: SLOT_COLOURS }, (_, i) => cs.getPropertyValue(`--p${i}`).trim());
}

export function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

/**
 * "house-rusher" -> "rusher" (or "Honey Badger" in the Snow College theme) for display, and a second
 * copy "house-rusher 2" -> "rusher 2"; keeps other names as they are.
 */
export const displayName = (name) => {
  if (!name?.startsWith('house-')) return name ?? '';
  const [bot, copy] = name.slice(6).split(' ');
  return [nickname(bot) ?? bot, copy].filter(Boolean).join(' ');
};

/** The tab's title: the parts, most specific first, then the game ("Alice vs Bob · Block Bot · Badger Brawl"). */
export const setTitle = (...parts) => { document.title = [...parts, GAME].filter(Boolean).join(' · '); };

/**
 * A match's heading: "Alice vs Honey Badger", or past four seats "16-player brawl" (the scoreboard lists
 * everyone). players: the summary's or state's; seats: how many it holds (defaults to how many are in).
 */
export function matchup(players, seats = players?.length ?? 0) {
  if (!players?.length) return say('Waiting for players', 'Waiting for badgers');
  return seats > 4 ? `${seats}-player ${say('match', 'brawl')}` : players.map((p) => displayName(p.name)).join(' vs ');
}

/** The blue check beside a bot claimed by a Discord-verified person; hover shows who. */
export function verifiedBadge(p) {
  if (!p?.verified) return '';
  const who = p.ownerName ? `Verified with Discord: ${p.ownerName}` : 'Verified with Discord';
  return html`<svg class="verified" viewBox="0 0 16 16" role="img" aria-label="${who}"><title>${who}</title>
    <circle cx="8" cy="8" r="8"/><path d="M4.4 8.3l2.4 2.4 4.8-5" fill="none" stroke="#fff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
}

/** A player for lists: bot name, blue check, and (with owner) the owner's Discord server nickname. */
export function playerLabel(p, { owner = true } = {}) {
  return html`${displayName(p?.name)}${verifiedBadge(p)}${owner && p?.verified && p.ownerName ? html`<span class="owner">${p.ownerName}</span>` : ''}`;
}

/**
 * A bot linked to its page, and its verified owner linked to their profile. Not for use inside
 * another link (a match row, say): use playerLabel there.
 */
export function botAndOwner(p, { id = p?.playerId, name = p?.name, owner = true } = {}) {
  const bot = html`<a class="plain" href="#/bot/${id}">${displayName(name)}</a>${verifiedBadge(p)}`;
  if (!owner || !p?.ownerName) return bot;
  return html`${bot}${p.ownerSlug ? html`<a class="owner" href="#/player/${p.ownerSlug}">${p.ownerName}</a>` : html`<span class="owner">${p.ownerName}</span>`}`;
}

export function timeAgo(iso) {
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return '';
  const s = Math.max(0, (Date.now() - t) / 1000);
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`;
  return new Date(t).toLocaleDateString();
}

export const fmt = (n) => (typeof n === 'number' ? n.toLocaleString() : '–');

/** Tooltip for the Skill column: OpenSkill's conservative estimate, round(40 × (μ − 3σ)), floored at 0. */
export const SKILL_TITLE = 'Opponent-aware skill (OpenSkill): beating strong bots counts for more. A conservative estimate, 40 × (μ − 3σ): it starts at 0 and climbs as results come in.';

const howWon = (reason) =>
  ({ Elimination: say('by elimination', 'by taking the last HQ'), TimeLimit: 'on score at the time limit', Surrender: 'by surrender' }[reason] || '');

export function outcomeText(outcome, players) {
  if (!outcome) return '';
  const winner = players?.find((p) => p.playerId === outcome.winnerId);
  if (!winner) return `Draw ${outcome.reason === 'TimeLimit' ? 'at the time limit' : ''} after ${fmt(outcome.ticks)} ticks`.replace('  ', ' ');
  return `${displayName(winner.name)} won ${howWon(outcome.reason)} after ${fmt(outcome.ticks)} ticks`;
}

/** outcomeText as markup: the winner (in their colour) and how they won stand out; the tick count doesn't. */
export function outcomeHtml(outcome, players) {
  if (!outcome) return '';
  const winner = players?.find((p) => p.playerId === outcome.winnerId);
  if (!winner) {
    return html`<strong class="outcome-how">Draw${outcome.reason === 'TimeLimit' ? ' at the time limit' : ''}</strong> after ${fmt(outcome.ticks)} ticks`;
  }
  return html`<strong class="pname outcome-winner" style="--pc:${slotVar(winner.slot)}">${displayName(winner.name)}</strong>
    won <strong class="outcome-how">${howWon(outcome.reason)}</strong> after ${fmt(outcome.ticks)} ticks`;
}

/**
 * One row of a match list. Finished matches leave out the status column: they all say "Completed".
 * clock: show when a finished match started as a time of day (for lists grouped by date) rather than how long ago.
 */
export function matchRow(m, { status = true, clock = false } = {}) {
  const players = [...(m.players || [])].sort((a, b) => a.slot - b.slot);
  // A league match lists who has joined (they're split into heats later), not a matchup.
  const shown = m.heats ? players.slice(0, 12) : players;
  const names = !players.length
    ? html`<span class="muted">${say('no players yet', 'no badgers yet')}</span>`
    : m.heats
      ? html`${shown.map((p, i) => html`${i ? ', ' : ''}${playerLabel(p, { owner: false })}`)}${players.length > shown.length ? html` <span class="muted">+${players.length - shown.length} more</span>` : ''}`
      : players.map((p, i) => html`${i ? html`<span class="vs-sep">vs</span>` : ''}<span class="pname" style="--pc:${slotVar(p.slot)}">${playerLabel(p, { owner: false })}</span>`);
  const open = m.maxPlayers - players.length;
  let meta;
  if (m.heats) {
    // A league match, played in heats.
    meta = m.status === 'Waiting'
      ? (m.heats.length ? html`${m.heats.length} heats · starting soon` : html`<b>${players.length}</b> joined · verified bots, join now`)
      : html`${m.heats.filter((h) => h.status !== 'Completed').length} of ${m.heats.length} heats live`;
  } else if (m.status === 'Completed' && m.outcome) {
    meta = html`${outcomeHtml(m.outcome, m.players)}<br>${clock
      ? new Date(m.createdAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
      : timeAgo(m.createdAt)}`;
  } else if (m.status === 'Waiting') {
    meta = html`${open} seat${open === 1 ? '' : 's'} open`;
  } else {
    const pct = m.maxTicks ? Math.min(100, (m.tick / m.maxTicks) * 100) : 0;
    meta = html`tick ${fmt(m.tick)} / ${fmt(m.maxTicks)}<div class="minibar" aria-hidden="true"><span style="width:${pct.toFixed(1)}%"></span></div>`;
  }
  // A name keeps pointing at the newest match that uses it, so finished matches link by id.
  const key = m.name && m.status !== 'Completed' ? m.name : m.matchId;
  return html`<li><a class="match-row ${status ? '' : 'no-status'}" href="#/match/${key}" aria-label="${m.status} ${say('match', 'brawl')}: ${players.map((p) => displayName(p.name)).join(' versus ')}">
    ${status ? html`<span class="status ${m.status}">${m.status === 'Active' ? 'Live' : m.status}</span>` : ''}
    <span class="who">${m.category && m.category !== 'Practice' ? html`<span class="tag cat ${m.category}">${m.category}</span>` : ''}${m.name ? html`<b style="margin-right:8px">${m.name}</b>` : ''}${names}${m.heats ? '' : html`<span class="muted small" style="margin-left:8px">${m.mapWidth}×${m.mapHeight}</span>`}</span>
    <span class="meta">${meta}</span></a></li>`;
}

/**
 * Everything a mounted page starts that must stop when it's torn down: timers, listeners on window or
 * document, and awaited requests. dispose() (in the page's teardown) cancels the timers, removes the
 * listeners and runs the cleanups; after that no callback from this scope runs, and a promise passed
 * through wait() never settles, so code after `await scope.wait(...)` simply doesn't run on a page
 * that has gone.
 */
export function mountScope() {
  let alive = true;
  const timers = new Set();
  const cleanups = [];
  return {
    get alive() { return alive; },
    /** setTimeout that forgets itself once it has fired, and never fires after dispose. */
    later(fn, ms) {
      if (!alive) return 0;
      const t = setTimeout(() => { timers.delete(t); if (alive) fn(); }, ms);
      timers.add(t);
      return t;
    },
    /** Cancels a timer from later(). */
    clear(t) { clearTimeout(t); timers.delete(t); },
    /** addEventListener, removed on dispose. */
    on(target, type, fn, opts) {
      target.addEventListener(type, fn, opts);
      cleanups.push(() => target.removeEventListener(type, fn, opts));
    },
    /** Runs fn on dispose (closing a stream, destroying a view); straight away if that has happened. */
    cleanup(fn) { if (alive) cleanups.push(fn); else fn(); },
    /** The promise's result, only while the page is still mounted; afterwards it never settles. */
    wait(promise) {
      return new Promise((resolve, reject) => {
        Promise.resolve(promise).then((v) => { if (alive) resolve(v); }, (e) => { if (alive) reject(e); });
      });
    },
    dispose() {
      if (!alive) return;
      alive = false;
      timers.forEach(clearTimeout);
      timers.clear();
      for (const fn of cleanups.splice(0).reverse()) {
        try { fn(); } catch (e) { console.error(e); }
      }
    },
  };
}

/** Slot -> display name; a slot nobody has named yet reads "Player N". */
class PlayerNames extends Map {
  get(slot) { return super.get(slot) ?? `Player ${Number(slot) + 1}`; }
}

/** Every player's display name by slot, from the match summary and (newer, so it wins) the live state. */
export function playerNames(summary, state) {
  const names = new PlayerNames();
  for (const p of summary?.players || []) names.set(p.slot, displayName(p.name));
  for (const p of state?.players || []) names.set(p.slot, displayName(p.name));
  return names;
}

/** A score's total (older results have only the three parts); 0 for no score. */
export const totalOf = (score) => (score ? score.total ?? score.destruction + score.economy + score.survival : 0);

/**
 * Players in scoreboard order: those still in first, then by total score, then by slot. scoreOf finds a
 * player's score where it isn't on the player itself. Returns [{ p, total, rank }].
 */
export function rankPlayers(players, scoreOf = (p) => p.score) {
  return (players || [])
    .map((p) => ({ p, total: totalOf(scoreOf(p)) }))
    .sort((a, b) => (Number(!!a.p.eliminated) - Number(!!b.p.eliminated)) || (b.total - a.total) || (a.p.slot - b.p.slot))
    .map((r, i) => ({ ...r, rank: i + 1 }));
}

/** Saves obj as a pretty-printed .json file through the browser's download. */
export function downloadJson(fileName, obj) {
  downloadText(fileName.endsWith('.json') ? fileName : `${fileName}.json`, JSON.stringify(obj, null, 2), 'application/json');
}

/** Saves text as a file through the browser's download. */
export function downloadText(fileName, text, type = 'text/plain') {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], { type }));
  a.download = fileName;
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

/**
 * A house bot's name for people: its nickname in the Snow College theme ("Honey Badger"), else its API
 * name. withName: the nickname followed by the API name in brackets, for where both matter.
 */
export function botLabel(name, { withName = false } = {}) {
  const nick = nickname(name);
  if (!nick) return name;
  return withName ? `${nick} (${name})` : nick;
}

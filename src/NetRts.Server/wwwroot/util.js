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

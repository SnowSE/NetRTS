// Shared helpers: API access, escaping, player colours.
import { BOT_NICKNAME } from './lore.js';

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
    throw new ApiError(0, 'NETWORK', 'Could not reach the Badger Brawl server. Check that it is running.');
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

/** CSS custom property name for a slot's colour. */
export const slotVar = (slot) => `var(--p${((slot % 4) + 4) % 4})`;

/** Reads the resolved player colours (theme aware) for the canvas. */
export function playerColours() {
  const cs = getComputedStyle(document.documentElement);
  return [0, 1, 2, 3].map((i) => cs.getPropertyValue(`--p${i}`).trim());
}

export function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

/** "house-rusher" -> "Honey Badger" for display; keeps the original otherwise. */
export const displayName = (name) => {
  if (!name?.startsWith('house-')) return name ?? '';
  const bot = name.slice(6);
  return BOT_NICKNAME[bot] ?? bot;
};

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

export function outcomeText(outcome, players) {
  if (!outcome) return '';
  const winner = players?.find((p) => p.playerId === outcome.winnerId);
  const how = { Elimination: 'by digging out the last sett', TimeLimit: 'on score at the time limit', Surrender: 'by surrender' }[outcome.reason] || '';
  if (!winner) return `Draw ${outcome.reason === 'TimeLimit' ? 'at the time limit' : ''} after ${fmt(outcome.ticks)} ticks`.replace('  ', ' ');
  return `${displayName(winner.name)} won ${how} after ${fmt(outcome.ticks)} ticks`;
}

// Block Bot: what this browser keeps (strategies, preferences, which strategy played which match, and
// the player's key) and the authorised API call.
import { api, downloadJson } from '../util.js';
import { load, save, forget } from '../storage.js';
import { SNOW } from '../lore.js';

const KEYS = {
  strategies: 'netrts.blocks.strategies',
  player: 'netrts.blocks.player',
  matches: 'netrts.blocks.matches',
  prefs: 'netrts.blocks.prefs',
};
const LOCAL = 'localStorage';
const SESSION = 'sessionStorage';

export const strategies = () => load(LOCAL, KEYS.strategies, {});
export const getStrategy = (id) => strategies()[id] ?? null;
export function putStrategy(s) {
  const all = strategies();
  all[s.id] = { ...s, updatedAt: new Date().toISOString() };
  return save(LOCAL, KEYS.strategies, all);
}
export function deleteStrategy(id) {
  const all = strategies();
  delete all[id];
  save(LOCAL, KEYS.strategies, all);
}
export const prefs = () => load(LOCAL, KEYS.prefs, {});
export const setPref = (k, v) => save(LOCAL, KEYS.prefs, { ...prefs(), [k]: v });
export const matchRecords = () => load(LOCAL, KEYS.matches, {});
export function recordMatch(matchId, strategyId) {
  const all = matchRecords();
  all[matchId] = { strategyId, at: new Date().toISOString() };
  // Keep the newest 30.
  const keep = Object.entries(all).sort((a, b) => (b[1].at > a[1].at ? 1 : -1)).slice(0, 30);
  save(LOCAL, KEYS.matches, Object.fromEntries(keep));
}

export const newId = () => (crypto.randomUUID ? crypto.randomUUID() : `s${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`);
export const clone = (x) => JSON.parse(JSON.stringify(x));

export function newStrategy(program) {
  const s = { id: newId(), program: clone(program), createdAt: new Date().toISOString() };
  putStrategy(s);
  return s;
}

/** A strategy as a file to keep or move to another computer (the locker's "Open a strategy file" reads it back). */
export function downloadStrategy(strategy) {
  const name = (strategy.program.name || 'strategy').replace(/[^A-Za-z0-9_-]+/g, '-').replace(/^-|-$/g, '') || 'strategy';
  downloadJson(name, { format: 'netrts-block-bot', version: 1, program: strategy.program });
}

// ------------------------------------------------------------------ who's playing

// The player's key lives in sessionStorage, so a shared lab computer forgets it when the tab closes (as the
// API page does with its key), unless they tick "Keep me signed in on this computer": then localStorage.
export const player = () => load(SESSION, KEYS.player, null) ?? load(LOCAL, KEYS.player, null);
/** Whether the key is kept on this computer (past this tab). */
export const keptSignedIn = () => load(LOCAL, KEYS.player, null) != null;
/** Signs in as p (keep: on this computer, not just this tab); null signs out of both. */
export function setPlayer(p, { keep = false } = {}) {
  forget(SESSION, KEYS.player);
  forget(LOCAL, KEYS.player);
  if (p) save(keep ? LOCAL : SESSION, KEYS.player, p);
}

/** The site theme (the global Snow College / original toggle), so the server explains itself in the same words. */
const THEME = SNOW ? 'snow' : 'classic';
/** A Block Bot call that returns its status (the rules' "why" lines): the server words it in the site's theme. */
export const themed = (path) => `${path}${path.includes('?') ? '&' : '?'}theme=${THEME}`;

export function authed(path, options = {}) {
  const p = player();
  return api(path, { ...options, headers: { ...(options.headers || {}), authorization: `Bearer ${p?.apiKey ?? ''}` } });
}

/** The key file the locker's "Browse for a key file" reads back. */
export function downloadKey(me) {
  if (!me?.apiKey) return;
  downloadJson(`${me.name}-api-key`, { playerId: me.playerId, name: me.name, apiKey: me.apiKey });
}

/** Set by the editor's "Play it": the locker opens scrolled to step 3. */
export const nav = { jumpToPlay: false };

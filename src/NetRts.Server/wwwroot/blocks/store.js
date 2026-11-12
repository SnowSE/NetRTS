// Block Bot: what this browser keeps (strategies, preferences, which strategy played which match, and
// the player's key) and the authorised API call. While someone is signed in to the website, sync.js
// copies the strategies to and from their account; it reads and writes them here too.
import { api, downloadJson } from '../util.js';
import { load, save, forget } from '../storage.js';
import { SNOW } from '../lore.js';
import { LIMITS } from './program.js';

const KEYS = {
  strategies: 'netrts.blocks.strategies',
  player: 'netrts.blocks.player',
  matches: 'netrts.blocks.matches',
  prefs: 'netrts.blocks.prefs',
  sync: 'netrts.blocks.sync',
};
const LOCAL = 'localStorage';
const SESSION = 'sessionStorage';

// ------------------------------------------------------------------ strategies

// { id: { id, program, createdAt, updatedAt, pulled? } }. pulled marks the account copy sync.js last
// brought down over this one (from another computer), so a page still showing the older one can tell.
export const strategies = () => load(LOCAL, KEYS.strategies, {});
export const getStrategy = (id) => strategies()[id] ?? null;
/** Every strategy at once (sync.js, after merging in the account's); false when they couldn't be kept. */
export const keepStrategies = (all) => save(LOCAL, KEYS.strategies, all);

/**
 * Saves a page's strategy. If sync.js brought another computer's version down over it since the page read
 * it (s.pulled differs), that version is kept as a copy rather than overwritten, and s is marked as up to
 * date with it, so the page's next save goes straight through.
 */
export function putStrategy(s) {
  const all = strategies();
  const kept = all[s.id];
  if (kept && (kept.pulled ?? null) !== (s.pulled ?? null)) {
    keepCopy(all, kept);
    s.pulled = kept.pulled;
  }
  all[s.id] = { ...s, updatedAt: new Date().toISOString() };
  const ok = keepStrategies(all);
  if (ok) changed(s.id);
  return ok;
}

/** Deletes a strategy here (and, when someone is signed in, sync.js deletes it from their account). */
export function deleteStrategy(id) {
  const all = strategies();
  const gone = all[id];
  delete all[id];
  keepStrategies(all);
  changed(id, gone);
}

const ACCOUNT_COPY = ' (from your account)';
/** Adds s to all as a new strategy named "… (from your account)": the other side when both sides changed one. */
export function keepCopy(all, s) {
  const max = LIMITS().maxNameLength;
  const name = `${(s.program.name || 'Strategy').slice(0, Math.max(1, max - ACCOUNT_COPY.length))}${ACCOUNT_COPY}`.slice(0, max);
  const copy = { id: newId(), program: { ...clone(s.program), name }, createdAt: new Date().toISOString(), updatedAt: s.updatedAt };
  all[copy.id] = copy;
  return copy;
}

// What sync.js last agreed with each account, per account id: { known: { id: { version, at } }, deleted: { id: version } }.
// known: the account's version and this browser's updatedAt when they last matched. deleted: deleted here,
// not yet on the account.
export const syncRecords = () => load(LOCAL, KEYS.sync, {});
export const saveSyncRecords = (records) => save(LOCAL, KEYS.sync, records);

// Pages' saves and deletes, for sync.js to send on.
const listeners = new Set();
/** Calls fn(id) after each putStrategy, fn(id, deleted) after each deleteStrategy; returns the unsubscribe. */
export function onLocalChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}
const changed = (id, deleted) => listeners.forEach((fn) => fn(id, deleted));

// ------------------------------------------------------------------ preferences and matches

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

// The player: { playerId, name, apiKey }, or { playerId, name, viaAccount: true } for one of the bots on the
// signed-in Discord account, which needs no key. The player's key lives in sessionStorage, so a shared lab computer forgets it when the tab closes (as the
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

/**
 * A call as the player: with its key, or, for one of the bots on the signed-in Discord account (picked
 * without a key, viaAccount), with the website sign-in and the bot's id.
 */
export function authed(path, options = {}) {
  const p = player();
  const who = p?.viaAccount ? { 'x-play-as': p.playerId } : { authorization: `Bearer ${p?.apiKey ?? ''}` };
  return api(path, { ...options, headers: { ...(options.headers || {}), ...who } });
}

/** The key file the locker's "Browse for a key file" reads back. */
export function downloadKey(me) {
  if (!me?.apiKey) return;
  downloadJson(`${me.name}-api-key`, { playerId: me.playerId, name: me.name, apiKey: me.apiKey });
}

/** Set by the editor's "Play it": the locker opens scrolled to step 3. */
export const nav = { jumpToPlay: false };

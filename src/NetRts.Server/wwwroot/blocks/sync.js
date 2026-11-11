// Block Bot strategies on the website account. While someone is signed in (Discord), every strategy in
// this browser is saved to their account and every one on their account comes down here, so they follow
// them to any computer. Signed out, nothing here runs and strategies stay in this browser as before.
//
// The browser's copy (store.js) is the one pages read and write; this sends changes on in the background
// (SAVE_DELAY after the last edit, again later if the account can't be reached) and merges the account's
// in when a Block Bot page opens, every POLL while one is open, and when the window gets focus:
//   - only one side changed since this browser last synced it: that side wins;
//   - both changed: this browser's stays under its id (an open editor keeps working) and the account's is
//     kept as a copy, "… (from your account)", so neither is lost;
//   - deleted here while signed in: deleted from the account (remembered until that works); deleted on
//     the account and unchanged here: deleted here; changed here since: saved back.
// A strategy this browser synced with a different account isn't saved to this one.
//
// It runs while a Block Bot page holds it (holdSync, from mountBlocks), not inside one page's mountScope:
// pages come and go as you move between the locker and the editor, and a save that has reached the account
// must still be recorded here when its answer comes after the page has gone. Pages hear about it through
// SYNC_EVENT on window, which they listen for in their own scope.
import { api, html, raw } from '../util.js';
import { strategies, keepStrategies, keepCopy, syncRecords, saveSyncRecords, onLocalChange } from './store.js';
import { sameProgram, canon } from './program.js';
import { ICON } from './icons.js';

/**
 * Fired on window when how saving to the account is going changed; detail.strategies is true when strategies
 * in this browser changed because of the account (came down, were deleted, or a copy was kept).
 */
export const SYNC_EVENT = 'netrts:strategy-sync';

const BASE = '/api/v1/account/strategies';
const SAVE_DELAY = 1500;
const POLL = 60_000;
const RETRY = [5_000, 20_000, 60_000];

let holders = 0;
let accountPromise = null;   // the signed-in account's id, or null: asked once a page load
let account = null;
let failures = 0;            // network or server failures in a row (reset by a good pass)
const refused = new Map();   // id -> { at, message }: the account said no to this version (until it changes)
let saveTimer = 0;
let retryTimer = 0;
let pollTimer = 0;
let queue = Promise.resolve();
let reconcileQueued = false;
let flushQueued = false;
let offLocal = null;
const merged = [];             // holdSync's ready, waiting for the next merge (or failure)

const emit = (fromAccount = false) => window.dispatchEvent(new CustomEvent(SYNC_EVENT, { detail: { strategies: fromAccount } }));

/** Whether someone is signed in, so strategies are saved to their account. */
export const syncing = () => account != null;

/**
 * Starts syncing while a Block Bot page is open; returns { ready, release }. ready resolves once the
 * account's strategies are merged in (or after waitMs, or straight away when nobody is signed in), so a page
 * mounted after it reads the merged list.
 */
export function holdSync(waitMs = 2000) {
  holders++;
  if (holders === 1) start();
  let released = false;
  const ready = Promise.race([
    signedInAccount().then((a) => (a ? new Promise((r) => merged.push(r)) : null)).catch(() => null),
    new Promise((r) => setTimeout(r, waitMs)),
  ]);
  return {
    ready,
    release() {
      if (released) return;
      released = true;
      if (--holders === 0) stop();
    },
  };
}

function start() {
  offLocal = onLocalChange(changedHere);
  window.addEventListener('focus', onFocus);
  window.addEventListener('online', onFocus);
  pollTimer = setInterval(onFocus, POLL);
  signedInAccount().then((a) => { if (a) reconcileSoon(); }, () => {});
}

function stop() {
  offLocal?.();
  window.removeEventListener('focus', onFocus);
  window.removeEventListener('online', onFocus);
  clearInterval(pollTimer);
  clearTimeout(retryTimer);
  // Leaving Block Bot straight after an edit: send it now rather than at the next visit.
  if (saveTimer) { clearTimeout(saveTimer); saveTimer = 0; flushSoon(); }
}

function onFocus() { if (account && holders) reconcileSoon(); }

function signedInAccount() {
  accountPromise ??= api('/api/v1/account').then((a) => {
    account = a?.signedIn && a.account ? a.account.accountId : null;
    emit();
    return account;
  }, (e) => { accountPromise = null; throw e; });
  return accountPromise;
}

function changedHere(id, deleted) {
  if (!account) return;
  if (deleted) {
    // Gone from the account too (on the next save), so it doesn't come back from another computer.
    const records = syncRecords();
    const mine = recordsFor(records, account);
    if (mine.known[id]) {
      mine.deleted[id] = mine.known[id].version;
      delete mine.known[id];
      saveSyncRecords(records);
    }
  }
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => { saveTimer = 0; flushSoon(); }, SAVE_DELAY);
  emit();
}

// ------------------------------------------------------------------ what the page shows

/** How a strategy stands with the account: local (nobody signed in), saved, pending, failed, refused, other or stale. */
export function syncState(s) {
  if (!account || !s) return { state: 'local' };
  const all = syncRecords();
  const kept = strategies()[s.id];
  if (kept && (kept.pulled ?? null) !== (s.pulled ?? null)) return { state: 'stale' };
  const mine = all[account];
  if (mine?.known?.[s.id] && mine.known[s.id].at === kept?.updatedAt) return { state: 'saved' };
  if (ownedElsewhere(all, s.id)) return { state: 'other' };
  const no = refused.get(s.id);
  if (no && no.at === kept?.updatedAt) return { state: 'refused', message: no.message };
  return { state: failures ? 'failed' : 'pending' };
}

/** The edit page's "saved" line for strategy s (the page's own copy of it). */
export function savedLabel(s) {
  const { state, message } = syncState(s);
  switch (state) {
    case 'saved': return html`${raw(ICON.check)} Saved to your account`;
    case 'pending': return html`${raw(ICON.check)} Saved in this browser · saving to your account…`;
    case 'failed': return html`${raw(ICON.check)} Saved in this browser · couldn’t reach your account, trying again soon`;
    case 'refused': return html`${raw(ICON.check)} Saved in this browser, not your account: ${message}`;
    case 'other': return html`${raw(ICON.check)} Saved in this browser (it came from another account)`;
    case 'stale': return html`Changed on another computer: reload to see it. Your changes here are kept too.`;
    default: return html`${raw(ICON.check)} Saved in this browser`;
  }
}

// ------------------------------------------------------------------ syncing

// One network job at a time, and at most one of each kind waiting.
function enqueue(job) {
  queue = queue.then(job).then(succeeded, failed);
  return queue;
}

function reconcileSoon() {
  if (reconcileQueued) return;
  reconcileQueued = true;
  enqueue(() => { reconcileQueued = false; return reconcile(); });
}

function flushSoon() {
  if (flushQueued || reconcileQueued) return;
  flushQueued = true;
  enqueue(() => { flushQueued = false; return flush(); });
}

function succeeded() {
  if (!failures) return;
  failures = 0;
  emit();
}

const mergedNow = () => merged.splice(0).forEach((r) => r());

function failed(e) {
  mergedNow();
  if (e?.status === 401) { signedOut(); return; }
  failures++;
  emit();
  clearTimeout(retryTimer);
  retryTimer = setTimeout(() => { if (holders) reconcileSoon(); else flushSoon(); }, RETRY[Math.min(failures, RETRY.length) - 1]);
}

function signedOut() {
  account = null;
  accountPromise = null;
  emit();
}

const recordsFor = (all, id) => (all[id] ??= { known: {}, deleted: {} });
const ownedElsewhere = (all, id) => Object.entries(all).some(([acc, r]) => acc !== account && (r.known?.[id] || r.deleted?.[id] != null));
const dirty = (s, known) => !known || known.at !== s.updatedAt;
const notRefused = (s) => refused.get(s.id)?.at !== s.updatedAt;

/** Sends just what changed here over the versions last seen; anything less simple is a full reconcile. */
async function flush() {
  const acc = account;
  if (!acc) return;
  const all = syncRecords();
  const mine = recordsFor(all, acc);
  const edited = Object.values(strategies()).filter((s) => dirty(s, mine.known[s.id]) && notRefused(s) && !ownedElsewhere(all, s.id));
  if (Object.keys(mine.deleted).length || edited.some((s) => !mine.known[s.id])) return reconcile();
  for (const s of edited) {
    if (!(await upload(acc, s.id, mine.known[s.id].version))) return reconcile();
  }
}

/** Merges the account's strategies with this browser's (the rules at the top), then sends what the account lacks. */
async function reconcile() {
  const acc = account;
  if (!acc) return;

  for (const [id, version] of Object.entries(recordsFor(syncRecords(), acc).deleted)) {
    try {
      await api(`${BASE}/${encodeURIComponent(id)}?version=${version}`, { method: 'DELETE' });
    } catch (e) {
      // 404: already gone. 409: changed on another computer since, so it stays (and comes back down here).
      if (e.status !== 404 && e.status !== 409) throw e;
    }
    const records = syncRecords();
    delete recordsFor(records, acc).deleted[id];
    saveSyncRecords(records);
  }

  const remote = await api(BASE);
  if (account !== acc) return;

  // Merge, with no awaits between reading this browser's copies and writing them back.
  const all = strategies();
  const records = syncRecords();
  const mine = recordsFor(records, acc);
  const sends = [];
  let touched = false;
  const take = (r) => {
    all[r.id] = { id: r.id, program: canon(r.program), createdAt: r.createdAt, updatedAt: r.updatedAt, pulled: `${r.version}@${r.updatedAt}` };
    mine.known[r.id] = { version: r.version, at: r.updatedAt };
    touched = true;
  };
  const seen = new Set();
  for (const r of remote) {
    seen.add(r.id);
    if (mine.deleted[r.id] != null) continue;   // deleted here; the delete didn't go through yet
    const local = all[r.id];
    const known = mine.known[r.id];
    if (!local) { take(r); continue; }
    const here = dirty(local, known);
    const there = !known || known.version !== r.version;
    if (!here && !there) continue;
    if (sameProgram(local.program, r.program)) { mine.known[r.id] = { version: r.version, at: local.updatedAt }; continue; }
    if (!here) { take(r); continue; }
    if (there) {
      // Changed on both sides: keep the account's too, and count its version as seen (it's safe in the copy).
      sends.push({ id: keepCopy(all, { ...r, program: canon(r.program) }).id, base: null });
      mine.known[r.id] = { version: r.version, at: known?.at ?? null };
      touched = true;
    }
    if (notRefused(local)) sends.push({ id: r.id, base: r.version });
  }
  for (const local of Object.values(all)) {
    if (seen.has(local.id) || ownedElsewhere(records, local.id)) continue;
    const known = mine.known[local.id];
    if (known) {
      delete mine.known[local.id];
      // Deleted on another computer: gone here too, unless it changed here since.
      if (!dirty(local, known)) { delete all[local.id]; touched = true; continue; }
    }
    if (notRefused(local)) sends.push({ id: local.id, base: null });
  }
  if (touched) keepStrategies(all);
  saveSyncRecords(records);
  if (touched) emit(true);
  mergedNow();

  let again = false;
  for (const { id, base } of sends) {
    if (!(await upload(acc, id, base))) again = true;
  }
  // Another computer got in first: merge again with what it saved (once here; later passes catch the rest).
  if (again && account === acc) reconcileSoon();
}

/** Saves one strategy over the account's version base (null: new); false when the account's had moved on. */
async function upload(acc, id, base) {
  const s = strategies()[id];
  if (!s) return true;
  try {
    const r = await api(`${BASE}/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: { program: s.program, baseVersion: base, createdAt: s.createdAt ?? null },
    });
    const records = syncRecords();
    recordsFor(records, acc).known[id] = { version: r.version, at: s.updatedAt };
    saveSyncRecords(records);
    refused.delete(id);
    emit();
    return true;
  } catch (e) {
    if (e.code === 'STRATEGY_CHANGED') return false;
    if (e.status >= 400 && e.status < 500 && e.status !== 401 && e.status !== 408 && e.status !== 429) {
      // The account won't take this version (a broken rule, too many strategies): don't send it again until it changes.
      refused.set(id, { at: s.updatedAt, message: e.message });
      emit();
      return true;
    }
    throw e;
  }
}

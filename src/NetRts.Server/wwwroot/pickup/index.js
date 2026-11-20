// #/pickup: the Pick-up Game on a phone. Opening it puts you in line (POST /pickup/join, the token kept in
// sessionStorage so a reload keeps your name and place) with a ready-made plan to change. The page polls
// GET /pickup/me every second and follows your phase:
//
//   line, next, countdown   the Deck (deck.js) under a top bar that says where you are; edits are saved
//                           (PUT /pickup/plan) 400 ms after the last change
//   playing                 now playing (playing.js) over the Deck
//   over                    match over (over.js), until "Tweak your plan and get back in line" (rejoin)
//
// The big screen (#/pickup/screen) is screen.js. app.js loads these only when a pick-up page opens.
import { api, esc, mountScope, setTitle } from '../util.js';
import { load, save } from '../storage.js';
import { takePage } from './page.js';
import { createSheets } from './sheets.js';
import { createDeck, dragToReorder, mouseScroll } from './deck.js';
import { mountPlaying } from './playing.js';
import { overHTML } from './over.js';
import { setCatalog, normalize, left, clock, ordinal, samePlan } from './items.js';

// Who this phone is, kept on the device (localStorage) so a new tab, a closed browser or a server restart
// brings back the same player: the token, and the name to ask for if the server has forgotten the token.
const TOKEN_KEY = 'netrts.pickup.token';
const NAME_KEY = 'netrts.pickup.name';
const PLAN_KEY = 'netrts.pickup.plan';   // the plan too, so a server restart or a reload never loses it
const DEVICE = 'localStorage';
const stored = () => load(DEVICE, TOKEN_KEY, null) ?? load('sessionStorage', TOKEN_KEY, null);
const POLL_MS = 1000;
const SAVE_MS = 400;
/** A match is about 2:30 and takes up to three from the line: a rough wait for a place in line. */
const waitMinutes = (place) => Math.max(1, Math.round(Math.ceil(place / 3) * 2.5));

export function mountPickup(main) {
  const scope = mountScope();
  const { later } = scope;
  scope.cleanup(takePage(main));
  setTitle('Pick-up Game');
  main.innerHTML = `<div class="pk" id="pk">
      <div class="p" id="pk-screen"><p class="pk-loading">Getting you in line…</p></div>
      <div class="sheet-wrap" id="pk-sheet" hidden></div>
      <div class="ov" id="pk-ov" hidden></div>
      <div class="toast" id="pk-toast" role="status" hidden></div>
    </div>`;
  const root = main.querySelector('#pk');
  const $ = (id) => root.querySelector(`#${id}`);

  let me = null;          // GET /pickup/me
  let plan = null;        // the plan being edited (API shape); the server's copy is saved from it
  let decks = [];
  let saveT = 0;
  let saveError = '';
  let offline = false;
  let playing = null;     // the now-playing overlay, while a match plays
  let playedPlan = null;  // the plan as it stood when the match started (what played)
  let overFor = null;     // the match whose match-over screen is showing
  let liveEdit = false;   // changing the plan while the match plays (Change my plan)
  let liveStep = -1;      // the step the deck last showed as "now"

  const sheets = createSheets($('pk-sheet'), $('pk-toast'), scope);
  const deck = createDeck({
    plan: () => plan,
    replace: (p) => { plan = p; changed(); },
    changed: () => changed(),
    redraw: () => draw(),
    decks: () => decks,
    live: () => (liveEdit && me?.phase === 'playing' ? { step: me.step ?? 0 } : null),
    sheets,
  });
  const eatDrag = dragToReorder(root, scope, (kind, from, to) => deck.move(kind, from, to));
  const eatScroll = mouseScroll(root, scope);
  const eatClick = () => eatDrag() | eatScroll();   // both, so neither keeps a stale true
  scope.cleanup(() => playing?.dispose());

  // ---- talking to the server

  const token = () => me?.token ?? stored();

  async function join() {
    const t = stored(), name = load(DEVICE, NAME_KEY, null);
    me = await scope.wait(api('/api/v1/pickup/join', { method: 'POST', body: { token: t, name } }));
    save(DEVICE, TOKEN_KEY, me.token);
    save(DEVICE, NAME_KEY, me.name);
  }

  /** A fresh entry in line (the server forgot this phone, or it never had one); a plan already edited here goes with it. */
  async function rejoinLine() {
    await join();
    plan ??= devicePlan() ?? normalize(me.plan);
    saveNow();
  }

  /** The plan kept on this phone, if there is one. */
  const devicePlan = () => { const p = load(DEVICE, PLAN_KEY, null); return p ? normalize(p) : null; };

  function changed() {
    save(DEVICE, PLAN_KEY, plan);
    if (playing) {
      // During the match the server hands each saved plan to the bot; now playing shows the new steps.
      playedPlan = normalize(plan);
      playing.setPlan(playedPlan);
    }
    draw();
    scope.clear(saveT);
    saveT = later(saveNow, SAVE_MS);
  }

  async function saveNow() {
    scope.clear(saveT);
    saveT = 0;
    const p = plan;
    try {
      await scope.wait(api('/api/v1/pickup/plan', { method: 'PUT', body: { token: token(), plan: p } }));
      setSaveError('');
    } catch (e) {
      if (e.code === 'UNKNOWN_TOKEN') { await rejoinLine().catch(() => {}); return; }
      setSaveError(e.status === 400 ? e.message : `Couldn't save your plan: ${e.message}`);
    }
  }

  function setSaveError(text) {
    saveError = text;
    const el = $('pk-err');
    if (el) { el.textContent = text; el.hidden = !text; }
  }

  async function poll() {
    try {
      me = await scope.wait(api(`/api/v1/pickup/me?token=${encodeURIComponent(token() ?? '')}`));
      offline = false;
    } catch (e) {
      if (e.code === 'UNKNOWN_TOKEN' || e.status === 404) {
        try { await rejoinLine(); offline = false; } catch { offline = true; }
      } else offline = true;
    }
    if (me) follow();
    later(poll, POLL_MS);
  }

  // ---- phases

  /** Shows what me's phase calls for. */
  function follow() {
    paintHead();
    if (me.phase === 'playing') {
      if (overFor) closeOverlay();
      if (!playing || playing.matchId !== me.matchId) {
        playing?.dispose();
        sheets.close();
        playedPlan = normalize(plan);
        playing = { matchId: me.matchId, ...mountPlaying($('pk-ov'), { plan: playedPlan, token: token(), matchId: me.matchId }) };
      }
      playing.update(me);
      $('pk-ov').hidden = liveEdit;
      if (liveEdit && me.step !== liveStep) { liveStep = me.step; draw(); }
      return;
    }
    const was = playing;
    if (liveEdit) { liveEdit = false; draw(); }
    if (playing) { playing.dispose(); playing = null; }
    if (me.phase === 'over' && me.result) {
      if (overFor !== (me.matchId ?? 'last')) showOver();
      return;
    }
    if (overFor) closeOverlay();
    if (was && me.champion) sheets.toast('You won! You hold the hill.');
  }

  function showOver() {
    sheets.close();
    overFor = me.matchId ?? 'last';
    const ov = $('pk-ov');
    ov.className = 'ov over';
    ov.hidden = false;
    ov.innerHTML = overHTML(me, playedPlan ?? plan);
    ov.scrollTop = 0;
  }

  function closeOverlay() {
    overFor = null;
    const ov = $('pk-ov');
    ov.hidden = true;
    ov.innerHTML = '';
  }

  async function rejoin(btn) {
    btn.disabled = true;
    try {
      me = await scope.wait(api('/api/v1/pickup/rejoin', { method: 'POST', body: { token: token() } }));
      closeOverlay();
      follow();
      window.scrollTo(0, 0);
    } catch (e) {
      btn.disabled = false;
      const err = $('pk-over-err');
      if (err) { err.textContent = e.message; err.hidden = false; }
    }
  }

  /** Not ready yet (keep my place, let the next player go) or ready again. */
  async function hold(on, btn) {
    btn.disabled = true;
    try {
      if (saveT) await saveNow();
      me = await scope.wait(api('/api/v1/pickup/hold', { method: 'POST', body: { token: token(), hold: on } }));
      follow();
    } catch (e) {
      btn.disabled = false;
      sheets.toast(e.message);
    }
  }

  function editLive(on) {
    liveEdit = on && me?.phase === 'playing';
    liveStep = me?.step ?? -1;
    sheets.close();
    $('pk-ov').hidden = liveEdit;
    draw();
    window.scrollTo(0, 0);
  }

  async function startNow(btn) {
    btn.disabled = true;
    try {
      if (saveT) await saveNow();   // the plan as it is now is the one that plays
      me = await scope.wait(api('/api/v1/pickup/start-now', { method: 'POST', body: { token: token() } }));
      follow();
    } catch (e) {
      btn.disabled = false;
      sheets.toast(e.message);
    }
  }

  // ---- drawing

  /** The top bar: who you are and your place in line, or that you're next, or the countdown. */
  function headHTML() {
    const coin = `<div class="coin"><b>${left(plan)}</b><small>grubs left</small></div>`;
    const name = esc(me?.name ?? '');
    if (offline) return `<div><b>${name}</b><small>Reconnecting…</small></div>${coin}`;
    if (me?.phase === 'countdown') {
      return `<div class="cd-count"><small>You're up!</small><b>Starting in ${me.secondsLeft ?? 0}…</b><small>Your plan at zero is the one that plays.</small></div>${coin}`;
    }
    if (me?.phase === 'playing' && liveEdit) {
      return `<div class="cd-count"><small>Your match is on the big screen</small><b>Changes reach your bot right away</b><button type="button" class="cd-start" data-a="back-to-match">Back to the match</button></div>${coin}`;
    }
    if (me?.phase === 'next') {
      return `<div class="cd-count"><small>You're next, ${name}!</small><b>Starts in ${clock(me.secondsLeft ?? 0)}</b><button type="button" class="cd-start" data-a="start-now">Start now</button><button type="button" class="cd-hold" data-a="hold" data-v="1">Need more time</button></div>${coin}`;
    }
    if (me?.holding && me.place) {
      return `<div class="cd-count"><small>${name} · ${ordinal(me.place)} in line, holding your place</small><b>Not ready yet</b><button type="button" class="cd-start" data-a="hold" data-v="0">I'm ready</button><small>The next player goes ahead until you're ready.</small></div>${coin}`;
    }
    const where = me?.champion && me.place == null
      ? `<span class="crown">♛</span> You hold the hill · ${me.wins} win${me.wins === 1 ? '' : 's'}`
      : me?.place ? `${ordinal(me.place)} in line · about ${waitMinutes(me.place)} min` : 'In line';
    const notReady = me?.place && me.phase === 'line' ? '<button type="button" class="cd-hold" data-a="hold" data-v="1">Not ready yet</button>' : '';
    return `<div><b>${name}</b><small>${where}</small>${notReady}</div>${coin}`;
  }

  function paintHead() {
    const h = $('pk-head');
    if (!h) return;
    h.classList.toggle('go-time', me?.phase === 'countdown' || (me?.phase === 'playing' && liveEdit));
    h.classList.toggle('next-time', me?.phase === 'next' || (me?.phase === 'line' && !!me?.holding));
    const next = headHTML();
    if (h.innerHTML !== next) h.innerHTML = next;
  }

  /** The whole Deck, keeping the hand's scroll where it was. */
  function draw() {
    const screen = $('pk-screen');
    const handX = screen.querySelector('.hand')?.scrollLeft ?? 0;
    screen.className = 'p p-cards';
    screen.innerHTML = `<header class="cd-head" id="pk-head"></header>
      <p class="pk-error" id="pk-err" role="alert" ${saveError ? '' : 'hidden'}>${esc(saveError)}</p>
      ${deck.html()}`;
    paintHead();
    const hand = screen.querySelector('.hand');
    if (hand) hand.scrollLeft = handX;
  }

  // ---- input

  const ACTIONS = {
    ...sheets.actions,
    ...deck.actions,
    'start-now': (v, el) => startNow(el),
    hold: (v, el) => hold(v === '1', el),
    'edit-live': () => editLive(true),
    'back-to-match': () => editLive(false),
    rejoin: (v, el) => rejoin(el),
  };
  scope.on(root, 'click', (e) => {
    if (eatClick()) { e.preventDefault(); return; }
    const el = e.target.closest('[data-a]');
    if (!el || el.disabled) return;
    const f = ACTIONS[el.dataset.a];
    if (f) { e.preventDefault(); f(el.dataset.v, el); }
  });

  // ---- start

  (async () => {
    try {
      const t = stored();
      const [rules, list] = await scope.wait(Promise.all([
        api('/api/v1/rules'),
        api('/api/v1/pickup/decks').catch(() => []),
        t ? api(`/api/v1/pickup/me?token=${encodeURIComponent(t)}`).then((m) => { me = m; }, () => {}) : null,
      ]));
      setCatalog(rules);
      decks = list || [];
      if (!me) await join();
      // This phone's own copy wins over the server's (the server may have restarted and started over).
      const kept = devicePlan();
      plan = kept ?? normalize(me.plan);
      if (kept && !samePlan(kept, me.plan)) saveNow();
      else save(DEVICE, PLAN_KEY, plan);
    } catch (e) {
      $('pk-screen').innerHTML = `<div class="pk-fail"><h1>Pick-up Game</h1><p class="pk-error" role="alert">Couldn't get you in line: ${esc(e.message)}</p><p><a class="big-btn" href="#/pickup" data-retry>Try again</a></p></div>`;
      root.querySelector('[data-retry]').addEventListener('click', (ev) => { ev.preventDefault(); window.dispatchEvent(new HashChangeEvent('hashchange')); });
      return;
    }
    draw();
    follow();
    later(poll, POLL_MS);
  })();

  return () => scope.dispose();
}

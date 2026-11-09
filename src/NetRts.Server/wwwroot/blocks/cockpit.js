// #/blocks/match/<id>: the cockpit. The match in the 3D view, your rules lighting up as they fire,
// switches to flip, and each rule saved into the running match as you change it.
import { api, html, raw, setHtml, displayName, outcomeText, mountScope, playerNames } from '../util.js';
import { say } from '../lore.js';
import { openMatchFeed } from '../matchfeed.js';
import { mount3d } from '../3d/mount.js';
import { getStrategy, putStrategy, newStrategy, prefs, matchRecords, recordMatch, player, authed, clone } from './store.js';
import { RULES, THEME, sameProgram } from './program.js';
import { RuleEditor } from './rule-editor.js';
import { ICON } from './icons.js';
import { StatHistory, hudHtml, buildingCardHtml, qualityIcon, soundIcon } from './cockpit-hud.js';

// Home: your base from above, a little zoomed out. The view starts there; the ⌂ button comes back.
const HOME_VIEW = { dist: 16, pitch: 1.05 };
const HOME_ICON = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 11l9-7 9 7"/><path d="M5 10v10h14V10"/><path d="M10 20v-5h4v5"/></svg>';

export function mountCockpit(main, matchId) {
  if (!player()) {
    setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1><p>This browser doesn’t have your ${say('bot', 'badger')}’s key. <a href="#/blocks">Go to the locker</a> to set it up, or <a href="#/match/${matchId}">just watch the match</a>.</p></section>`);
    return () => {};
  }

  // Polls, the match feed, the 3D view and the page-wide listeners all stop at teardown, and no request
  // that comes back after it (scope.wait) touches the page, or the one that replaced it.
  const scope = mountScope();
  const { later } = scope;
  let summary = null;       // the match (GET /matches/{id})
  let status = null;        // your Block Bot in it (GET /blocks/matches/{id}/status)
  let strategy = null;      // the strategy being edited here (saved in the locker as you go)
  let editor = null;
  let lastState = null;     // the newest spectator frame
  let mapData = null;
  let view3d = null;
  let audio = null;
  let feed = null;
  let hovered = null;
  let applying = false;
  let homed = false;
  let waitingMsg = false;
  let pickedId = null;      // the building clicked in the 3D view
  let pickedLast = null;    // the last picture of it, for when it's gone
  const history = new StatHistory();

  main.classList.add('wide', 'bb-full');
  setHtml(main, html`
    <section class="bb-cockpit" id="bb-cockpit">
      <aside class="bb-left" aria-label="Your rules">
        <div class="bb-left-head">
          <div class="bb-bar">
            <a href="#/blocks" class="bb-back">← My strategies</a>
            <span class="bb-live-pill" id="bb-pill">LIVE</span>
            <span class="bb-spacer"></span>
            <a class="btn ghost small-btn" href="#/match/${matchId}">Spectator view</a>
            <button type="button" class="btn ghost small-btn" id="bb-surrender">Give up</button>
          </div>
          <h1 class="bb-match-title" id="bb-title">Loading the match…</h1>
          <p class="small muted bb-running" id="bb-running"></p>
          <div class="bb-waiting" id="bb-waiting" aria-live="polite" hidden></div>
        </div>
        <div class="bb-controls">
          <section class="bb-switches-row" aria-labelledby="bb-sw-h">
            <h2 id="bb-sw-h" class="bb-label">Switches · instant</h2>
            <div class="bb-switches" id="bb-switches"></div>
          </section>
          <section class="bb-apply" id="bb-apply" aria-live="polite"></section>
        </div>
        <div class="bb-bar bb-edit-bar">
          <h2 class="bb-label" title="Point at a rule to make the ${say('units', 'badgers')} it’s moving pulse.">Your rules</h2>
          <span class="bb-spacer"></span>
          <button type="button" class="btn ghost small-btn" id="bb-code" aria-pressed="${!!prefs().code}">${raw(ICON.code)} Python</button>
        </div>
        <div id="bb-editor"></div>
        <section aria-labelledby="bb-ver-h" class="bb-ver">
          <h2 id="bb-ver-h" class="bb-label">Your versions this match</h2>
          <ol class="bb-versions" id="bb-versions"></ol>
        </section>
      </aside>
      <div class="bb-right">
        <div class="bb-3d-wrap">
          <div class="map3d bb-3d" id="bb-3d"></div>
          <div class="bb-view-left" role="group" aria-label="Camera">
            <button type="button" class="bb-sq" data-zoom="in" aria-label="Zoom in" title="Zoom in (+)">+</button>
            <button type="button" class="bb-sq" data-zoom="out" aria-label="Zoom out" title="Zoom out (−)">−</button>
            <button type="button" class="bb-sq" data-home aria-label="Home: center on my base" title="Home: center on my base (H)">${raw(HOME_ICON)}</button>
          </div>
          <div class="bb-view-right">
            <button type="button" class="bb-sq" id="bb-sound" aria-pressed="true" aria-label="Sound"></button>
            <button type="button" class="bb-sq" id="bb-quality" aria-label="Picture quality"></button>
          </div>
          <aside class="bb-hud" id="bb-hud" aria-label="Your stats over the last minute, and the scoreboard"></aside>
          <aside class="bb-bcard" id="bb-bcard" aria-label="The building you clicked" aria-live="polite" hidden></aside>
          <p class="bb-3d-msg" id="bb-3d-msg">Building the 3D valley…</p>
          <div class="bb-result" id="bb-result" hidden></div>
        </div>
      </div>
    </section>`);
  const root = main.querySelector('#bb-cockpit');
  const $ = (id) => root.querySelector(`#${id}`);

  // The cockpit fills the window under the site's top bar: the rules scroll on the left, the view stays put.
  const fitHeight = () => root.style.setProperty('--bb-top', `${Math.max(0, root.getBoundingClientRect().top + window.scrollY)}px`);
  fitHeight();
  scope.on(window, 'resize', fitHeight);
  scope.cleanup(() => { feed?.close(); main.classList.remove('wide', 'bb-full'); });

  // ---- the rules

  function ensureStrategy(program) {
    if (strategy) return;
    const rec = matchRecords()[matchId];
    strategy = rec ? getStrategy(rec.strategyId) : null;
    if (!strategy) {
      strategy = newStrategy({ ...program, name: `${program.name || 'Strategy'} (from a match)`.slice(0, 40) });
      recordMatch(matchId, strategy.id);
    }
    editor = new RuleEditor($('bb-editor'), {
      compact: true,
      program: strategy.program,
      // Each rule's Save (and each on/off, move or delete) sends the whole program in as a new version.
      onSave: async (p) => {
        if (status?.matchStatus === 'Completed') throw new Error('The match is over, so there’s nothing to send it to.');
        try {
          status = await scope.wait(authed(`/api/v1/blocks/matches/${matchId}/program`, { method: 'PUT', body: p }));
        } catch (e) {
          throw new Error(e.code === 'INVALID_PROGRAM' ? `That doesn’t work yet: ${e.message}` : e.message);
        }
        strategy = { ...strategy, program: { ...p, name: strategy.program.name } };
        putStrategy(strategy);
        renderSide();
        renderApply();
      },
      onHover: (i) => { hovered = i; highlight(); },
    });
  }

  // The saved strategy can differ from what's playing when it was changed in the full editor meanwhile:
  // then the cockpit offers to send it in, or to go back to what's playing.
  const pending = () => strategy && status?.program && !editor?.busy && !sameProgram(strategy.program, status.program);

  function renderApply() {
    const box = $('bb-apply');
    if (!status) return;
    if (status.matchStatus !== 'Completed' && pending()) {
      setHtml(box, html`<div class="bb-pending"><p><b>This strategy changed in the editor</b> since this version started playing.</p>
        <div class="bb-row-btns"><button type="button" class="btn start small-btn" id="bb-apply-btn" ${applying ? 'disabled' : ''}>Send the changes in</button>
        <button type="button" class="btn ghost small-btn" id="bb-undo">Keep what’s playing</button></div><p class="bb-error" id="bb-apply-err" role="alert"></p></div>`);
    } else {
      setHtml(box, '');
    }
    editor?.setStatus(status.rules, pending() ? '(running version)' : '');
  }

  async function apply() {
    if (!strategy || applying) return;
    applying = true;
    renderApply();
    try {
      status = await scope.wait(authed(`/api/v1/blocks/matches/${matchId}/program`, { method: 'PUT', body: strategy.program }));
      renderSide();
    } catch (e) {
      applying = false;
      renderApply();
      $('bb-apply-err').textContent = e.code === 'INVALID_PROGRAM' ? `Your strategy has a problem: ${e.message}` : e.message;
      return;
    }
    applying = false;
    renderApply();
  }

  function renderSide() {
    if (!status) return;
    const sw = Object.entries(status.switches || {});
    setHtml($('bb-switches'), sw.length
      ? html`${sw.map(([name, on]) => html`<button type="button" class="bb-switch" data-switch="${name}" aria-pressed="${on}">${name}<span>${on ? 'ON' : 'off'}</span></button>`)}`
      : html`<p class="small muted">None yet: add one from a check’s ▾, like “Attack!”.</p>`);
    setHtml($('bb-versions'), html`${(status.versions || []).map((v) => html`<li${v.version === status.version ? ' aria-current="true"' : ''}><b>v${v.version}</b> <span class="muted">from tick ${v.tick}</span></li>`)}`);
    history.add(status);
    renderHud();
    $('bb-running').textContent = `${status.programName || 'Strategy'} · running v${status.version}${status.error ? ` · problem: ${status.error}` : ''}`;
  }

  async function flip(name) {
    const on = !status?.switches?.[name];
    try {
      status = await scope.wait(authed(`/api/v1/blocks/matches/${matchId}/switches`, { method: 'PUT', body: { name, on } }));
      renderSide();
    } catch (e) { setMsg(e.message); later(() => setMsg(''), 3000); }
  }

  // ---- the match

  function renderSummary() {
    if (!summary) return;
    $('bb-title').textContent = summary.players.length ? summary.players.map((p) => displayName(p.name)).join(' vs ') : 'Match';
    const pill = $('bb-pill');
    pill.textContent = summary.status === 'Active' ? 'LIVE' : summary.status === 'Waiting' ? 'WAITING' : 'FINISHED';
    pill.dataset.state = summary.status;
    $('bb-surrender').hidden = summary.status !== 'Active';
    const result = $('bb-result');
    if (summary.status === 'Completed') {
      const won = summary.outcome?.winnerId === player()?.playerId;
      result.hidden = false;
      setHtml(result, html`<h2>${won ? 'You won!' : summary.outcome?.winnerId ? 'Good game' : 'A draw'}</h2>
        <p>${outcomeText(summary.outcome, summary.players)}</p>
        <div class="bb-row-btns"><a class="btn start" href="#/blocks">Play again</a><a class="btn ghost" href="#/blocks/edit/${strategy?.id ?? ''}">Edit this strategy</a><a class="btn ghost" href="#/match/${matchId}">Watch the replay</a></div>`);
    }
    renderWaiting();
    if (summary.status === 'Waiting') {
      setMsg(`Waiting for players: ${summary.players.length} of ${summary.maxPlayers} seats taken.`);
      waitingMsg = true;
    } else if (waitingMsg) {
      waitingMsg = false;
      setMsg('');
    }
  }

  /** A match waiting for people: who's in, how others find it, and (for whoever created it) Start now. */
  function renderWaiting() {
    const box = $('bb-waiting');
    if (summary?.status !== 'Waiting') { box.hidden = true; return; }
    const me = player()?.playerId;
    const creator = !summary.hostedBy && summary.players.find((p) => p.slot === 0)?.playerId === me;
    const n = summary.players.length;
    box.hidden = false;
    setHtml(box, html`
      <p><b>Waiting for players: ${n} of ${summary.maxPlayers}.</b>
        ${summary.players.map((p) => html`<span class="bb-waiting-name">${displayName(p.name)}</span>`)}</p>
      <p class="small">Others join from Block Bot → Real match → Join a match${summary.name ? html`, or by typing <b>${summary.name}</b>` : ''}.
        It starts when every seat is taken${creator ? ', or when you start it' : summary.hostedBy ? ' or when the host starts it' : ' or when its creator starts it'}.</p>
      ${creator ? html`<button type="button" class="btn start small-btn" id="bb-start-now" ${n >= 2 ? '' : 'disabled'}>${n >= 2 ? `Start now with ${n} players` : 'Start now (needs 2 players)'}</button>` : ''}
      <p class="bb-error" id="bb-start-err" role="alert"></p>`);
  }

  async function startNow(btn) {
    btn.disabled = true;
    try {
      summary = await scope.wait(authed(`/api/v1/blocks/matches/${matchId}/start`, { method: 'POST' }));
    } catch (e) {
      $('bb-start-err').textContent = e.message;
      btn.disabled = false;
      return;
    }
    renderSummary();
    if (summary?.status === 'Active') openFeed(true);
  }

  function setMsg(text) {
    const m = $('bb-3d-msg');
    m.hidden = !text;
    m.textContent = text || '';
  }
  const gone = () => setMsg('This match is over and gone from the server. Your strategy is still saved here.');

  // ---- data: the match summary and your bot's status are polled; the picture comes from the match feed

  async function pollSummary() {
    try {
      summary = await scope.wait(api(`/api/v1/matches/${matchId}`));
    } catch (e) {
      if (e.status === 404) { gone(); return; }
      later(pollSummary, 10000);
      return;
    }
    renderSummary();
    if (summary.status === 'Active') openFeed(true);
    if (summary.status === 'Completed' && !lastState) openFeed(false);
    if (summary.status !== 'Completed') later(pollSummary, summary.status === 'Waiting' ? 2000 : 4000);
  }

  async function pollStatus() {
    try {
      status = await scope.wait(authed(`/api/v1/blocks/matches/${matchId}/status?theme=${THEME}`));
    } catch (e) {
      if (e.code === 'NO_BLOCK_BOT') { setMsg('You don’t have a Block Bot in this match.'); return; }
      if (e.status === 404) { gone(); return; }
      later(pollStatus, 10000);
      return;
    }
    ensureStrategy(status.program);
    renderSide();
    renderApply();
    if (view3d && status.slot >= 0 && view3d.vision !== status.slot) view3d.setVision(status.slot);
    highlight();
    if (status.matchStatus !== 'Completed') later(pollStatus, Math.max(400, Math.min(1000, summary?.tickIntervalMs ?? 1000)));
  }

  /** The map and every frame while it plays; for a match already over (live: false), its final positions. */
  function openFeed(live) {
    if (feed || !view3d) return;
    feed = openMatchFeed(matchId, {
      live,
      onMap: (map) => {
        mapData = map;
        view3d.setMap(map);
        if (status?.slot >= 0) view3d.setVision(status.slot);
      },
      onState: (s) => {
        lastState = s;
        view3d.setState(s);
        if (pickedId != null) renderBuildingCard();
        if (!homed) goHome();
      },
      onEnd: renderHud,
      onGone: gone,
    });
  }

  // ---- the 3D view

  async function start3d() {
    try {
      const v = await mount3d($('bb-3d'), {
        scope,
        names: () => playerNames(summary, lastState),
        onReady: () => {
          if (summary?.status !== 'Waiting') setMsg('');
          goHome();
        },
      });
      scope.cleanup(v.dispose);
      ({ view3d, audio } = v);
    } catch (e) {
      setMsg(`The 3D view couldn’t start here (${e?.message || 'no WebGL'}). Your bot is still playing.`);
      return;
    }
    audio.setActive(true);
    // The same viewer as the spectator page: its quality adapts to the computer (and remembers a choice).
    view3d.onQualityChange = renderViewTools;
    view3d.onPickBuilding = (b) => { pickedId = b?.id ?? null; pickedLast = b; renderBuildingCard(); };
    renderViewTools();
  }

  /** The speaker (sound on or muted) and the signal bars (picture quality: one, two or three filled). */
  function renderViewTools() {
    const q = $('bb-quality'), snd = $('bb-sound');
    if (view3d) {
      const label = `Picture quality: ${view3d.qualityName}. Click to change.`;
      q.setAttribute('aria-label', label);
      q.title = label;
      setHtml(q, qualityIcon(view3d.qualityLevel));
    }
    if (audio) {
      const muted = audio.settings.muted;
      const label = muted ? 'Sound is off: turn it on' : 'Sound is on: mute';
      snd.setAttribute('aria-pressed', String(!muted));
      snd.setAttribute('aria-label', label);
      snd.title = label;
      setHtml(snd, soundIcon(muted));
    }
  }

  function renderHud() {
    if (status) setHtml($('bb-hud'), hudHtml({ state: lastState, summary, status, history }));
  }

  function renderBuildingCard() {
    const box = $('bb-bcard');
    const live = pickedId == null ? null : lastState?.buildings?.find((x) => x.id === pickedId);
    const b = live ?? (pickedId == null ? null : pickedLast);
    if (!b) { box.hidden = true; return; }
    if (live) pickedLast = live;
    box.hidden = false;
    setHtml(box, buildingCardHtml(b, {
      live: !!live,
      mine: b.owner === status?.slot,
      ownerName: playerNames(summary, lastState).get(b.owner),
      rules: RULES,
    }));
  }

  /** Your base from above: your HQ (any of them), else where you started. */
  function goHome() {
    if (!view3d?.ready) return;
    const slot = status?.slot;
    const hq = (lastState?.buildings || []).find((b) => b.owner === slot && b.type === 'CommandCenter')
      ?? mapData?.startPositions?.[slot];
    if (!hq) return;
    homed = true;
    view3d.lookAtTile(hq.x, hq.y, HOME_VIEW);
  }

  function highlight() {
    if (!view3d) return;
    const ids = hovered != null ? status?.rules?.[hovered]?.unitIds : null;
    view3d.highlight = ids?.length ? new Set(ids) : null;
  }

  // ---- input

  root.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    if (b.id === 'bb-apply-btn') apply();
    else if (b.id === 'bb-undo' && status?.program) {
      strategy = { ...strategy, program: { ...clone(status.program), name: strategy.program.name } };
      putStrategy(strategy);
      editor.setProgram(strategy.program);
      renderApply();
    } else if (b.dataset.switch) flip(b.dataset.switch);
    else if ('home' in b.dataset) goHome();
    else if (b.id === 'bb-start-now') startNow(b);
    else if ('bclose' in b.dataset) { pickedId = null; renderBuildingCard(); }
    else if ('bzoom' in b.dataset && pickedLast) view3d?.lookAtTile(pickedLast.x, pickedLast.y, { dist: 5 });
    else if (b.dataset.zoom) view3d?.zoomStep(b.dataset.zoom);
    else if (b.id === 'bb-quality' && view3d) {
      view3d.cycleQuality();
      renderViewTools();
    } else if (b.id === 'bb-sound' && audio) {
      audio.unlock();
      audio.set('muted', !audio.settings.muted);
      renderViewTools();
    } else if (b.id === 'bb-code') {
      const on = b.getAttribute('aria-pressed') !== 'true';
      b.setAttribute('aria-pressed', String(on));
      editor?.setCode(on);
    } else if (b.id === 'bb-surrender') {
      if (b.dataset.confirm !== 'yes') { b.dataset.confirm = 'yes'; b.textContent = 'Really give up?'; b.classList.add('danger'); return; }
      scope.wait(authed(`/api/v1/matches/${matchId}/surrender`, { method: 'POST' })).catch((ex) => setMsg(ex.message));
    }
  });

  // + and - zoom from anywhere on the page, a step at a time, except while typing a number or picking from
  // a list. (With the 3D view focused, the view's own keys do it, smoothly while held.)
  scope.on(document, 'keydown', (e) => {
    if (e.ctrlKey || e.metaKey || e.altKey || !view3d) return;
    if (e.target.closest?.('input, select, textarea, [contenteditable]')) return;
    if (e.key === 'Escape' && pickedId != null && !e.target.closest?.('.bb-editor')) { pickedId = null; renderBuildingCard(); return; }
    if ((e.key === 'h' || e.key === 'H' || e.key === 'Home') && !e.target.closest?.('.map3d')) { e.preventDefault(); goHome(); return; }
    const dir = view3d.zoomDir(e.key);
    if (!dir || e.target.closest?.('.map3d')) return;
    e.preventDefault();
    view3d.zoomStep(dir);
  });

  (async () => {
    await start3d();
    await pollSummary();
    await pollStatus();
  })();

  return () => scope.dispose();
}

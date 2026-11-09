// #/blocks/match/<id>: the cockpit. The match in the 3D view, your rules lighting up as they fire,
// switches to flip, and each rule saved into the running match as you change it.
import { api, html, raw, setHtml, displayName, outcomeText, slotVar } from '../util.js';
import { SNOW, say, unitName, upgradeName, buildingName, buildingRole, ORE } from '../lore.js';
import { getStrategy, putStrategy, newStrategy, prefs, matchRecords, recordMatch, player, authed, clone } from './store.js';
import { RULES, THEME, sameProgram } from './program.js';
import { RuleEditor } from './rule-editor.js';
import { ICON } from './icons.js';

export function mountCockpit(main, matchId) {
  let alive = true;
  let summary = null;
  let status = null;
  let strategy = null;      // the strategy being edited here (saved in the locker as you go)
  let view3d = null;
  let es = null;
  let lastState = null;
  let hovered = null;
  let applying = false;
  // Home: your base from above, a little zoomed out. The view starts there; the ⌂ button comes back.
  const HOME_VIEW = { dist: 16, pitch: 1.05 };
  let homed = false;
  let mapData = null;
  let audio = null;
  const QUALITY_NAMES = { low: 'Low', medium: 'Medium', high: 'High' };
  const QUALITY_BARS = { low: 1, medium: 2, high: 3 };
  /** The speaker (sound on or muted) and the signal bars (picture quality: one, two or three filled). */
  function renderViewTools() {
    const q = $('bb-quality'), snd = $('bb-sound');
    if (q && view3d) {
      const n = QUALITY_BARS[view3d.quality] ?? 2;
      const label = `Picture quality: ${QUALITY_NAMES[view3d.quality] ?? 'Medium'}. Click to change.`;
      q.setAttribute('aria-label', label);
      q.title = label;
      setHtml(q, html`<svg width="20" height="20" viewBox="0 0 24 24" aria-hidden="true">${[0, 1, 2].map((i) =>
        html`<rect x="${2 + i * 7.5}" y="${16 - i * 6}" width="5" height="${6 + i * 6}" rx="1" fill="${i < n ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="1.5"/>`)}</svg>`);
    }
    if (snd && audio) {
      const muted = audio.settings.muted;
      const label = muted ? 'Sound is off: turn it on' : 'Sound is on: mute';
      snd.setAttribute('aria-pressed', String(!muted));
      snd.setAttribute('aria-label', label);
      snd.title = label;
      setHtml(snd, html`<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="${muted ? 'M16 9l5 6M21 9l-5 6' : 'M16 9.5a4 4 0 0 1 0 5M18.5 7a7.5 7.5 0 0 1 0 10'}"/></svg>`);
    }
  }

  // ---- the stats rail: a trend line per stat over the last minute, and the scoreboard
  const HISTORY_TICKS = 60;
  const history = [];   // { tick, grubs, workers, fighters, buildings }, oldest first
  function remember() {
    const s = status?.stats;
    if (!s || (history.length && history[history.length - 1].tick >= status.tick)) return;
    history.push({ tick: status.tick, grubs: s.grubs, workers: s.workers, fighters: s.fighters, buildings: s.buildings });
    while (history.length > 2 && history[0].tick < status.tick - HISTORY_TICKS) history.shift();
  }
  const fmtN = (n) => Number(n ?? 0).toLocaleString();
  function trendTile(key, label, color) {
    const now = history[history.length - 1]?.[key] ?? status?.stats?.[key] ?? 0;
    const first = history[0], last = history[history.length - 1];
    const span = first && last ? last.tick - first.tick : 0;
    const perMin = span >= 5 ? Math.round(((last[key] - first[key]) * 60) / span) : null;
    const W = 112, H = 20;
    let line = '', area = '', mark = null;
    if (history.length >= 2) {
      const vals = history.map((h) => h[key]);
      const lo = Math.min(...vals), hi = Math.max(...vals), range = Math.max(1, hi - lo);
      const x = (t) => ((t - first.tick) / Math.max(1, span)) * W;
      line = history.map((h) => `${x(h.tick).toFixed(1)},${(H - 2 - ((h[key] - lo) / range) * (H - 4)).toFixed(1)}`).join(' ');
      area = `0,${H} ${line} ${W},${H}`;
      // Where the newest version went live, when that was within the last minute.
      const v = status?.versions?.[status.versions.length - 1];
      if (v && v.version > 1 && v.tick > first.tick) mark = x(v.tick).toFixed(1);
    }
    const rate = perMin == null ? '' : perMin === 0 ? 'steady' : `${perMin > 0 ? '▲ +' : '▼ −'}${fmtN(Math.abs(perMin))}/min`;
    const rateClass = perMin == null || perMin === 0 ? '' : perMin > 0 ? ' up' : ' down';
    return html`<div class="bb-tile">
      <span class="bb-tile-label">${label}</span>
      <span class="bb-tile-row"><span class="bb-tile-value" style="color:${color}">${fmtN(now)}</span><span class="bb-tile-rate${rateClass}">${rate}</span></span>
      <svg width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${label} over the last minute, now ${fmtN(now)}">
        ${line ? html`<polyline points="${area}" fill="${color}" fill-opacity="0.14" stroke="none"/><polyline points="${line}" fill="none" stroke="${color}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>` : ''}
        ${mark ? html`<line x1="${mark}" y1="0" x2="${mark}" y2="${H}" stroke="#f2c14e" stroke-width="1" stroke-dasharray="2 2"><title>v${status.versions[status.versions.length - 1].version} went live</title></line>` : ''}
      </svg>
    </div>`;
  }
  /** Everyone when it fits; with lots of players, the top four, then you. */
  function scoreboard() {
    const players = [...(lastState?.players || [])]
      .sort((a, b) => (a.eliminated - b.eliminated) || ((b.score?.total ?? 0) - (a.score?.total ?? 0)))
      .map((p, i) => ({ ...p, rank: i + 1, me: p.slot === status?.slot }));
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
        : html`<li class="${p.me ? 'me' : ''}${p.eliminated ? ' out' : ''}" style="--pc:${slotVar(p.slot)}"><span class="bb-board-rank">${p.rank}</span><span class="bb-board-name">${p.me ? 'You' : displayName(p.name)}</span><span class="bb-board-score">${fmtN(p.score?.total)}</span></li>`)}</ol>
    </div>`;
  }
  /** The tick, the tick limit and roughly how long until it (a match can also end sooner, by elimination). */
  function clockTile() {
    const tick = Math.max(lastState?.tick ?? 0, status?.tick ?? 0);
    const max = lastState?.maxTicks ?? summary?.maxTicks ?? 0;
    if (!max) return '';
    const done = summary?.status === 'Completed' || status?.matchStatus === 'Completed';
    const secs = Math.round((Math.max(0, max - tick) * (lastState?.tickIntervalMs ?? summary?.tickIntervalMs ?? 1000)) / 1000);
    const left = done ? 'final' : `${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')} left`;
    const pct = Math.min(100, (tick / max) * 100);
    return html`<div class="bb-tile bb-clock" title="The match ends at tick ${fmtN(max)} if nobody is knocked out first. Then the highest score wins.">
      <span class="bb-tile-label">Tick <span>${left}</span></span>
      <span class="bb-tile-value">${fmtN(tick)}<small> / ${fmtN(max)}</small></span>
      <span class="bb-clock-bar" role="progressbar" aria-label="Match progress" aria-valuemin="0" aria-valuemax="${max}" aria-valuenow="${tick}"><span style="width:${pct.toFixed(1)}%"></span></span>
    </div>`;
  }

  function renderHud() {
    const box = $('bb-hud');
    if (!box || !status) return;
    setHtml(box, html`
      ${clockTile()}
      ${trendTile('grubs', ORE[0].toUpperCase() + ORE.slice(1), '#f2c14e')}
      ${trendTile('workers', 'Workers', '#7cc7f0')}
      ${trendTile('fighters', 'Fighters', '#ff8a7a')}
      ${trendTile('buildings', 'Buildings', '#c7b2f0')}
      ${scoreboard()}`);
  }
  const timers = [];
  const later = (fn, ms) => timers.push(setTimeout(() => { if (alive) fn(); }, ms));

  if (!player()) {
    setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1><p>This browser doesn’t have your ${say('bot', 'badger')}’s key. <a href="#/blocks">Go to the locker</a> to set it up, or <a href="#/match/${matchId}">just watch the match</a>.</p></section>`);
    return () => {};
  }

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
            <button type="button" class="bb-sq" data-home aria-label="Home: center on my base" title="Home: center on my base (H)">${raw('<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 11l9-7 9 7"/><path d="M5 10v10h14V10"/><path d="M10 20v-5h4v5"/></svg>')}</button>
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

  // The cockpit fills the window under the site's top bar: the rules scroll on the left, the view stays put.
  const fitHeight = () => {
    const el = main.querySelector('#bb-cockpit');
    if (el) el.style.setProperty('--bb-top', `${Math.max(0, el.getBoundingClientRect().top + window.scrollY)}px`);
  };
  fitHeight();
  window.addEventListener('resize', fitHeight);

  const $ = (id) => main.querySelector(`#${id}`);
  let editor = null;

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
          status = await authed(`/api/v1/blocks/matches/${matchId}/program`, { method: 'PUT', body: p });
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

  // The saved strategy can differ from what's playing when it was changed in the full editor meanwhile.
  const pending = () => strategy && status?.program && !editor?.busy && !sameProgram(strategy.program, status.program);

  function renderApply() {
    const box = $('bb-apply');
    if (!box || !status) return;
    if (status.matchStatus !== 'Completed' && pending()) {
      setHtml(box, html`<div class="bb-pending"><p><b>This strategy changed in the editor</b> since this version started playing.</p>
        <div class="bb-row-btns"><button type="button" class="btn start small-btn" id="bb-apply-btn" ${applying ? 'disabled' : ''}>Send the changes in</button>
        <button type="button" class="btn ghost small-btn" id="bb-undo">Keep what’s playing</button></div><p class="bb-error" id="bb-apply-err" role="alert"></p></div>`);
    } else {
      setHtml(box, '');
    }
    editor?.setStatus(status.rules, pending() ? '(running version)' : '');
  }

  function renderSide() {
    if (!status) return;
    const sw = Object.entries(status.switches || {});
    setHtml($('bb-switches'), sw.length
      ? html`${sw.map(([name, on]) => html`<button type="button" class="bb-switch" data-switch="${name}" aria-pressed="${on}">${name}<span>${on ? 'ON' : 'off'}</span></button>`)}`
      : html`<p class="small muted">None yet: add one from a check’s ▾, like “Attack!”.</p>`);
    setHtml($('bb-versions'), html`${(status.versions || []).map((v) => html`<li${v.version === status.version ? ' aria-current="true"' : ''}><b>v${v.version}</b> <span class="muted">from tick ${v.tick}</span></li>`)}`);
    remember();
    renderHud();
    $('bb-running').textContent = `${status.programName || 'Strategy'} · running v${status.version}`;
    if (status.error) $('bb-running').textContent += ` · problem: ${status.error}`;
  }

  function renderSummary() {
    if (!summary) return;
    const names = summary.players.map((p) => displayName(p.name));
    $('bb-title').textContent = summary.players.length ? names.join(' vs ') : 'Match';
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
  let waitingMsg = false;

  /** A match waiting for people: who's in, how others find it, and (for whoever created it) Start now. */
  function renderWaiting() {
    const box = $('bb-waiting');
    if (!box) return;
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
      summary = await authed(`/api/v1/blocks/matches/${matchId}/start`, { method: 'POST' });
      renderSummary();
      if (summary?.status === 'Active' && !es) startStream();
    } catch (e) {
      const err = $('bb-start-err');
      if (err) err.textContent = e.message;
      btn.disabled = false;
    }
  }

  function setMsg(text) {
    const m = $('bb-3d-msg');
    if (!m) return;
    m.hidden = !text;
    m.textContent = text || '';
  }

  // ---- data
  async function pollSummary() {
    try { summary = await api(`/api/v1/matches/${matchId}`); } catch (e) {
      if (e.status === 404) { gone(); return; }
      later(pollSummary, 10000);
      return;
    }
    renderSummary();
    if (summary?.status === 'Active' && !es) startStream();
    if (summary?.status === 'Completed' && !lastState) showFinalBoard();
    if (summary?.status !== 'Completed') later(pollSummary, summary?.status === 'Waiting' ? 2000 : 4000);
  }

  async function pollStatus() {
    try {
      status = await authed(`/api/v1/blocks/matches/${matchId}/status?theme=${THEME}`);
      ensureStrategy(status.program);
      renderSide();
      renderApply();
      if (view3d && status.slot >= 0 && view3d.vision !== status.slot) view3d.setVision(status.slot);
      highlight();
    } catch (e) {
      if (e.code === 'NO_BLOCK_BOT') { setMsg('You don’t have a Block Bot in this match.'); return; }
      if (e.status === 404) { gone(); return; }
      later(pollStatus, 10000);
      return;
    }
    if (status?.matchStatus !== 'Completed') later(pollStatus, Math.max(400, Math.min(1000, summary?.tickIntervalMs ?? 1000)));
  }

  async function start3d() {
    const box = $('bb-3d');
    try {
      const [{ View3D }, { GameAudio }] = await Promise.all([import('./3d/view.js'), import('./3d/audio.js')]);
      if (!alive) return;
      const names = () => new Map((lastState?.players || summary?.players || []).map((p) => [p.slot, displayName(p.name)]));
      audio = new GameAudio();
      audio.setActive(true);
      // The same viewer as the spectator page: its quality adapts to the computer (and remembers a choice).
      view3d = new View3D(box, { badger: SNOW, names, audio });
      box.view3d = view3d;   // handy from the dev tools console, as on the spectator page
      view3d.onQualityChange = renderViewTools;
      view3d.onPickBuilding = (b) => { pickedId = b?.id ?? null; pickedLast = b; renderBuildingCard(); };
      renderViewTools();
      view3d.onReady = () => {
        if (!alive) return;
        if (summary?.status !== 'Waiting') setMsg('');
        goHome();
      };
    } catch (e) {
      setMsg(`The 3D view couldn’t start here (${e?.message || 'no WebGL'}). Your bot is still playing.`);
    }
  }

  /** A finished match: the last picture, while the server still has it. */
  async function showFinalBoard() {
    if (!view3d) return;
    try {
      mapData = await api(`/api/v1/matches/${matchId}/map`);
      view3d.setMap(mapData);
      lastState = await api(`/api/v1/matches/${matchId}/spectate?fog=true`);
      view3d.setState(lastState);
      goHome();
      renderHud();
    } catch { /* archived: only the result is left */ }
  }

  let retryMs = 2000;
  const gone = () => setMsg('This match is over and gone from the server. Your strategy is still saved here.');
  async function startStream() {
    if (!view3d || !alive) return;
    if (!lastState) {
      try {
        const map = await api(`/api/v1/matches/${matchId}/map`);
        if (!alive) return;
        mapData = map;
        view3d.setMap(map);
        if (status?.slot >= 0) view3d.setVision(status.slot);
      } catch (e) {
        // Gone (the server restarted, say): stop. Unreachable: try again, less and less often.
        if (e.status === 404) { gone(); return; }
        later(startStream, retryMs);
        retryMs = Math.min(retryMs * 2, 30000);
        return;
      }
    }
    es = new EventSource(`/api/v1/matches/${matchId}/spectate/stream?fog=true`);
    es.onmessage = (msg) => {
      let s;
      try { s = JSON.parse(msg.data); } catch { return; }
      retryMs = 2000;   // connected again: the next drop starts the backoff over
      lastState = s;
      view3d?.setState(s);
      if (pickedId != null) renderBuildingCard();
      if (!homed) goHome();
    };
    es.onerror = () => {
      es?.close();
      es = null;
      if (alive && summary?.status !== 'Completed') { later(startStream, retryMs); retryMs = Math.min(retryMs * 2, 30000); }
    };
  }

  // ---- the building you clicked in the 3D view: what it is, whose, its health and what it's doing
  let pickedId = null;
  let pickedLast = null;   // the last picture of it, for when it's gone
  function renderBuildingCard() {
    const box = $('bb-bcard');
    if (!box) return;
    if (pickedId == null) { box.hidden = true; return; }
    const live = lastState?.buildings?.find((x) => x.id === pickedId);
    const b = live ?? pickedLast;
    if (!b) { box.hidden = true; return; }
    if (live) pickedLast = live;
    const mine = b.owner === status?.slot;
    const ownerName = displayName((lastState?.players || summary?.players || []).find((p) => p.slot === b.owner)?.name ?? `Player ${b.owner + 1}`);
    const r = RULES?.buildings?.find((x) => x.type === b.type);
    const pct = b.maxHp ? Math.max(0, Math.min(100, (b.hp / b.maxHp) * 100)) : 0;
    const stats = [
      r ? ['Armor', r.armor] : null,
      r?.damage ? ['Attack', `${r.damage} damage, range ${r.range}`] : null,
      r?.storageBonus ? ['Stores', `${fmtN(r.storageBonus)} ${ORE}`] : null,
      r?.produces?.length ? ['Trains', r.produces.map((t) => unitName(t)).join(', ')] : null,
      r?.canResearch ? ['Classes', say('researches upgrades', 'teaches the classes')] : null,
    ].filter(Boolean);
    box.hidden = false;
    setHtml(box, html`
      <div class="bb-bcard-head">
        <h2>${buildingName(b.type, b.owner)}</h2>
        <button type="button" class="bb-sq small" data-bclose aria-label="Close">×</button>
      </div>
      <p class="bb-bcard-owner"><span class="bb-bcard-dot" style="--pc:${slotVar(b.owner)}" aria-hidden="true"></span>${mine ? 'Yours' : ownerName}</p>
      ${live ? html`
        <div class="bb-bcard-hp"><span>HP</span><b>${fmtN(b.hp)} / ${fmtN(b.maxHp)}</b></div>
        <span class="bb-clock-bar bb-hpbar${pct < 34 ? ' low' : ''}" aria-hidden="true"><span style="width:${pct.toFixed(1)}%"></span></span>
        ${b.completed ? '' : html`<p>Under construction: ${b.constructionPercent}%</p>`}
        ${b.production?.length ? html`<p>${say('Producing', 'Training')}: ${unitName(b.production[0].unitType)} ${b.production[0].percent}%${b.production.length > 1 ? html` <span class="bb-bcard-q">(+${b.production.length - 1} queued)</span>` : ''}</p>` : ''}
        ${b.research ? html`<p>${say('Researching', 'Studying')}: ${upgradeName(b.research.upgrade)} ${b.research.percent}%</p>` : ''}`
        : html`<p class="bb-bcard-gone">Destroyed.</p>`}
      <p class="bb-bcard-role">${buildingRole(b.type)}</p>
      ${stats.length ? html`<dl class="bb-bcard-stats">${stats.map(([k, v]) => html`<div><dt>${k}</dt><dd>${v}</dd></div>`)}</dl>` : ''}
      ${live ? html`<button type="button" class="bb-bcard-zoom" data-bzoom>Zoom to it</button>` : ''}`);
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

  // ---- actions
  async function apply() {
    if (!strategy || applying) return;
    applying = true;
    renderApply();
    try {
      status = await authed(`/api/v1/blocks/matches/${matchId}/program`, { method: 'PUT', body: strategy.program });
      renderSide();
    } catch (e) {
      applying = false;
      renderApply();
      const err = $('bb-apply-err');
      if (err) err.textContent = e.code === 'INVALID_PROGRAM' ? `Your strategy has a problem: ${e.message}` : e.message;
      return;
    }
    applying = false;
    renderApply();
  }

  async function flip(name) {
    const on = !status?.switches?.[name];
    try {
      status = await authed(`/api/v1/blocks/matches/${matchId}/switches`, { method: 'PUT', body: { name, on } });
      renderSide();
    } catch (e) { setMsg(e.message); later(() => setMsg(''), 3000); }
  }

  function onClick(e) {
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
    else if (b.dataset.zoom) view3d?.zoom(b.dataset.zoom === 'in' ? 0.8 : 1.25);
    else if (b.id === 'bb-quality' && view3d) {
      const order = ['low', 'medium', 'high'];
      view3d.setQuality(order[(order.indexOf(view3d.quality) + 1) % order.length]);
      renderViewTools();
    } else if (b.id === 'bb-sound' && audio) {
      audio.unlock();
      audio.set('muted', !audio.settings.muted);
      renderViewTools();
    }
    else if (b.id === 'bb-code') {
      const on = b.getAttribute('aria-pressed') !== 'true';
      b.setAttribute('aria-pressed', String(on));
      editor?.setCode(on);
    } else if (b.id === 'bb-surrender') {
      if (b.dataset.confirm !== 'yes') { b.dataset.confirm = 'yes'; b.textContent = 'Really give up?'; b.classList.add('danger'); return; }
      authed(`/api/v1/matches/${matchId}/surrender`, { method: 'POST' }).catch((ex) => setMsg(ex.message));
    }
  }
  main.addEventListener('click', onClick);

  // + and - zoom from anywhere on the page, except while typing a number or picking from a list. (With
  // the 3D view focused, the view's own keys do it, smoothly while held.)
  function onKey(e) {
    if (e.ctrlKey || e.metaKey || e.altKey || !view3d) return;
    if (e.target.closest?.('input, select, textarea, [contenteditable]')) return;
    if (e.key === 'Escape' && pickedId != null && !e.target.closest?.('.bb-editor')) { pickedId = null; renderBuildingCard(); return; }
    if ((e.key === 'h' || e.key === 'H' || e.key === 'Home') && !e.target.closest?.('.map3d')) { e.preventDefault(); goHome(); return; }
    const zoom = { '+': 0.8, '=': 0.8, '-': 1.25, '_': 1.25 }[e.key];
    if (!zoom || e.target.closest?.('.map3d')) return;
    e.preventDefault();
    view3d.zoom(zoom);
  }
  document.addEventListener('keydown', onKey);

  (async () => {
    await start3d();
    await pollSummary();
    await pollStatus();
  })();

  return () => {
    alive = false;
    timers.forEach(clearTimeout);
    es?.close();
    view3d?.destroy();
    audio?.setActive(false);
    audio?.dispose?.();
    window.removeEventListener('resize', fitHeight);
    main.removeEventListener('click', onClick);
    document.removeEventListener('keydown', onKey);
    main.classList.remove('wide', 'bb-full');
  };
}

// A league match (preseason, season, championship), played in rounds: the queue filling up, each
// round's heats as the admin arranges them (in seat order around the map), then a live scoreboard
// per heat. Bots can join between rounds. Discord server admins also get the controls to split,
// reshuffle and start each round's heats at once.
import { api, html, setHtml, playerLabel, slotVar, fmt } from './util.js';
import { GAME, say } from './lore.js';

/** Heat sizes for an even split of n players into k heats, e.g. 25 into 3 -> [8, 8, 9]. */
export function evenSplit(n, k) {
  const base = Math.floor(n / k);
  return Array.from({ length: k }, (_, i) => base + (i >= k - (n % k) ? 1 : 0));
}

const lastRound = (m) => Math.max(0, ...(m.heats ?? []).map((h) => h.round ?? 1));
const pendingHeats = (m) => {
  const last = (m.heats ?? []).filter((h) => (h.round ?? 1) === lastRound(m));
  return last.length && last.every((h) => h.status === 'Waiting') ? last : [];
};

/** queueing (before round 1), split (a round is arranged), running, or between (rounds). */
const stageOf = (m) => pendingHeats(m).length ? 'split'
  : (m.heats ?? []).some((h) => h.status === 'Active') ? 'running'
  : m.status === 'Waiting' ? 'queueing' : 'between';

/** Slots in the order they sit around the map: corners go top-left, top-right, bottom-right, bottom-left. */
const aroundTheMap = (n) => (n <= 4 ? [0, 2, 1, 3].filter((i) => i < n) : Array.from({ length: n }, (_, i) => i));

export function mountLeague(root, initial) {
  const matchId = initial.matchId;
  document.title = `${initial.name} · ${GAME}`;
  let alive = true;
  let timer = null;
  let match = initial;
  let standings = null;
  let isAdmin = false;
  let message = null;   // { error, text }
  let busy = false;
  let sizesText = null; // what the admin typed, kept across refreshes
  let evenCount = null;

  api('/api/v1/account').then((a) => { isAdmin = Boolean(a?.account?.isAdmin); if (alive) render(); }).catch(() => {});

  async function load() {
    try {
      match = await api(`/api/v1/matches/${matchId}`);
      standings = match.status !== 'Waiting' ? await api(`/api/v1/matches/${matchId}/standings`) : null;
      if (alive && !busy) render();
    } catch (e) {
      if (!alive) return;
      if (e.status === 404) {
        setHtml(root, html`<div class="league"><p class="small"><a href="#/">← ${say('All matches', 'All brawls')}</a></p>
          <p class="empty">This match has finished and been tidied away, or was called off. Its heats are in the recent results and on the ${initial.category.toLowerCase()} leaderboard.</p></div>`);
        return;
      }
    }
    if (alive) timer = setTimeout(load, 2000);
  }

  function render() {
    const m = match;
    const stage = stageOf(m);
    const byId = new Map(m.players.map((p) => [p.playerId, p]));
    const connected = m.players.filter((p) => p.connected).length;
    const pending = pendingHeats(m);
    const round = lastRound(m);
    if (sizesText === null && pending.length) sizesText = pending.map((h) => h.playerIds.length).join(', ');
    const canEdit = isAdmin && stage !== 'running';
    const stageText = {
      queueing: 'Taking players',
      split: `Round ${round} split into heats, waiting for the start`,
      running: `Round ${round} under way`,
      between: `Round ${round} finished · bots can join before the next round`,
    }[stage];

    setHtml(root, html`<div class="league">
      <p class="small"><a href="#/">← ${say('All matches', 'All brawls')}</a></p>
      <div class="league-head">
        <div>
          <span class="tag cat ${m.category}">${m.category}</span>
          <h1 class="headline">${m.name}</h1>
          <p class="muted" style="margin:6px 0 0">${stageText} · ${fmt(m.tickIntervalMs)} ms per tick · ${fmt(m.maxTicks)} tick limit</p>
        </div>
        <div class="league-count"><b>${m.players.length}</b> ${stage === 'running' ? 'playing' : 'joined'}
          ${stage !== 'running' ? html`<br><span class="muted small">${connected} connected</span>` : ''}</div>
      </div>
      ${message ? html`<p class="${message.error ? 'error' : 'ok'}" role="status">${message.text}</p>` : ''}

      ${stage === 'queueing' || stage === 'between' ? html`<p class="muted">Verified bots join this match like any other
        (<code>POST /api/v1/matches/${m.matchId}/join</code>, or by name: <code>--join-match ${m.name}</code>) and long-poll its state.
        When the admin starts a round, each bot is playing in its own heat.</p>` : ''}

      ${isAdmin ? adminHtml(m, stage, canEdit) : ''}

      ${stage === 'split' ? html`<section aria-labelledby="heats-h">
        <h2 id="heats-h">Round ${round} heats</h2>
        <p class="muted small">In seat order around the map: each bot starts next to the ones beside it, and the last next to the first.</p>
        <div class="league-matches">${pending.map((h) => html`
          <article class="league-match">
            <h3>${h.name}<span class="muted small">${h.playerIds.length} players</span></h3>
            <ol class="league-players">${aroundTheMap(h.playerIds.length).map((slot) =>
              seatHtml(byId.get(h.playerIds[slot]) ?? { playerId: h.playerIds[slot], name: '?' }, slot, canEdit))}</ol>
          </article>`)}
        </div></section>` : ''}

      ${standings ? dashboardHtml(standings, m) : ''}

      ${stage === 'queueing' || stage === 'between' ? html`<section aria-labelledby="queue-h">
        <h2 id="queue-h">${stage === 'between' ? 'In the next round' : 'Joined'}</h2>
        ${m.players.length
          ? html`<ol class="league-players queue">${m.players.map((p, i) => seatHtml(p, i, canEdit))}</ol>`
          : html`<p class="empty">Nobody has joined yet.</p>`}
      </section>` : ''}
    </div>`);
    wire(m, stage, canEdit);
  }

  function seatHtml(p, i, canEdit) {
    return html`<li style="--pc:${slotVar(i)}">
      <span class="dot ${p.connected ? 'on' : 'off'}" title="${p.connected ? 'Connected: made an API call in the last 40 s' : 'Not connected'}"></span>
      <span class="league-name">${playerLabel(p)}</span>
      ${canEdit ? html`<button type="button" class="link-btn" data-remove="${p.playerId}" aria-label="Remove ${p.name}">Remove</button>` : ''}
    </li>`;
  }

  /** One live scoreboard per heat, latest round first, with each heat's seating around the map. */
  function dashboardHtml(heats, m) {
    const seating = new Map((m.heats ?? []).map((h) => [h.matchId, h.playerIds]));
    const rounds = [...new Set(heats.map((h) => h.round ?? 1))].sort((a, b) => b - a);
    return rounds.map((r) => roundHtml(r, heats.filter((h) => (h.round ?? 1) === r && h.status !== 'Waiting'), seating, rounds.length > 1));
  }

  function roundHtml(round, heats, seating, numbered) {
    if (!heats.length) return '';
    return html`<section aria-labelledby="board-h${round}">
      <h2 id="board-h${round}">${numbered || round > 1 ? `Round ${round}` : 'Heats'}</h2>
      <div class="heat-boards">${heats.map((h) => {
        const names = new Map(h.players.map((p) => [p.playerId, p]));
        const seats = seating.get(h.matchId) ?? [];
        const done = h.status === 'Completed';
        const pct = h.maxTicks ? Math.min(100, (h.tick / h.maxTicks) * 100) : 0;
        return html`<article class="heat-board ${done ? 'done' : ''}">
          <div class="heat-top">
            <h3><a href="#/match/${h.matchId}">${h.name}</a></h3>
            <span class="muted small">${done ? 'Final' : html`<span class="status Active">Live</span> tick ${fmt(h.tick)} / ${fmt(h.maxTicks)}`}</span>
          </div>
          ${done ? '' : html`<div class="minibar" aria-hidden="true"><span style="width:${pct.toFixed(1)}%"></span></div>`}
          <table class="ladder heat-table">
            <thead><tr><th scope="col">#</th><th scope="col">Player</th><th scope="col">Score</th>
              <th scope="col"><abbr title="${done ? 'League points earned' : 'League points if it ended now'}">Pts</abbr></th></tr></thead>
            <tbody>${h.players.map((p) => html`
              <tr class="${p.eliminated ? 'out' : ''}"><td>${p.place}</td>
                <td><span class="pname" style="--pc:${slotVar(p.slot)}">${playerLabel(p, { owner: false })}</span>${p.winner ? html`<span class="badge won">Winner</span>` : ''}</td>
                <td>${fmt(p.score)}</td><td class="rating">${p.points ? fmt(p.points) : '–'}</td></tr>`)}
            </tbody>
          </table>
          ${seats.length > 2 ? html`<p class="muted small" style="margin:8px 0 0" title="Seat order around the map; the last sits next to the first">Seating: ${aroundTheMap(seats.length).map((slot, i) =>
            html`${i ? ' → ' : ''}<span class="pname" style="--pc:${slotVar(slot)}">${names.get(seats[slot])?.name ?? '?'}</span>`)}</p>` : ''}
          <p class="small" style="margin:8px 0 0"><a href="#/match/${h.matchId}">${done ? 'Replay and details' : 'Watch this heat'} →</a></p>
        </article>`;
      })}</div>
    </section>`;
  }

  function adminHtml(m, stage, canEdit) {
    if (!canEdit) return '';
    const n = m.players.length;
    const idle = m.players.filter((p) => !p.connected).length;
    const suggested = Math.max(1, Math.round(n / 8));
    const next = stage === 'split' ? lastRound(m) : lastRound(m) + 1;
    return html`<section class="admin league-admin" aria-labelledby="league-admin-h">
      <h2 id="league-admin-h">Run this match</h2>
      <form id="split" class="panel-form" novalidate>
        <div class="form-row">
          <div class="field"><label for="sizes">Heat sizes (must add up to ${n})</label>
            <input id="sizes" name="sizes" value="${sizesText ?? ''}" placeholder="${n >= 2 ? evenSplit(n, suggested).join(', ') : ''}" autocomplete="off" inputmode="numeric"></div>
          <div class="field"><label for="even">Or split evenly into</label>
            <div class="inline"><input id="even" type="number" min="1" max="${Math.max(1, Math.floor(n / 2))}" value="${evenCount ?? suggested}" inputmode="numeric">
              <button type="button" class="btn ghost" id="even-btn">heats</button></div></div>
        </div>
        <div class="league-actions">
          <button class="btn" type="submit">${stage === 'split' ? 'Reshuffle' : `Split round ${next} into heats`}</button>
          ${stage === 'split' ? html`<button type="button" class="btn ghost" id="unsplit">Undo split (reopen to players)</button>` : ''}
        </div>
      </form>
      ${stage === 'split' ? html`<div class="start-box">
        <p>${idle ? html`<b>${idle}</b> of ${n} bots aren't connected. Remove them, or start anyway and they'll sit idle.` : html`All ${n} bots are connected.`}</p>
        <button type="button" class="btn start" id="start">Start round ${next}: all ${pendingHeats(m).length} heats</button>
      </div>` : ''}
      ${m.status === 'Waiting' ? html`<p><button type="button" class="link-btn danger" id="cancel">Call off this match</button></p>` : ''}
    </section>`;
  }

  function wire(m, stage, canEdit) {
    const $ = (id) => root.querySelector('#' + id);
    if (!canEdit) return;
    const base = `/api/v1/admin/matches/${m.matchId}`;
    $('sizes')?.addEventListener('input', (e) => { sizesText = e.target.value; });
    $('even-btn')?.addEventListener('click', () => {
      evenCount = Math.max(1, Number($('even').value) || 1);
      sizesText = evenSplit(m.players.length, evenCount).join(', ');
      $('sizes').value = sizesText;
    });
    $('split')?.addEventListener('submit', (ev) => {
      ev.preventDefault();
      const sizes = (sizesText || $('sizes').placeholder || '').split(/[\s,]+/).filter(Boolean).map(Number);
      act(() => api(`${base}/heats`, { method: 'POST', body: { sizes } }), stage === 'split' ? 'Reshuffled.' : `Split into ${sizes.length} heats.`);
    });
    $('unsplit')?.addEventListener('click', () => act(() => api(`${base}/heats`, { method: 'DELETE' }), 'Reopened to players.'));
    $('start')?.addEventListener('click', (e) => confirmThen(e.currentTarget, 'Start every heat now?',
      () => act(() => api(`${base}/start`, { method: 'POST' }), 'Started. Good luck, everyone.')));
    $('cancel')?.addEventListener('click', (e) => confirmThen(e.currentTarget, 'Really call it off?',
      () => act(() => api(base, { method: 'DELETE' }), null, () => { location.hash = '#/'; })));
    root.querySelectorAll('[data-remove]').forEach((b) => b.addEventListener('click', () => confirmThen(b, 'Remove?',
      () => act(() => api(`${base}/players/${b.dataset.remove}`, { method: 'DELETE' }), 'Removed.'))));
  }

  /** Destructive buttons take a second click (no browser dialogs). */
  function confirmThen(button, prompt, fn) {
    if (button.dataset.armed === 'true') { fn(); return; }
    button.dataset.armed = 'true';
    button.textContent = prompt;
    busy = true;   // don't re-render the confirmation away
    setTimeout(() => { busy = false; }, 4000);
  }

  async function act(fn, ok, then) {
    busy = true;
    try {
      const result = await fn();
      if (result?.matchId) match = result;
      message = ok ? { error: false, text: ok } : null;
      busy = false;
      if (then) { then(); return; }
      if (match.status !== 'Waiting') standings = await api(`/api/v1/matches/${matchId}/standings`).catch(() => null);
    } catch (e) {
      message = { error: true, text: e.message };
    }
    busy = false;
    if (alive) render();
  }

  render();
  load();
  return () => { alive = false; clearTimeout(timer); };
}

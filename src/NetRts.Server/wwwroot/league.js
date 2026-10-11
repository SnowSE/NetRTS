// A league match (preseason, season, championship): the queue filling up, the heats as the admin
// arranges them, then a live scoreboard per heat. Discord server admins also get the controls to
// split, reshuffle and start every heat at once.
import { api, html, setHtml, playerLabel, slotVar, fmt } from './util.js';
import { GAME, say } from './lore.js';

/** Heat sizes for an even split of n players into k heats, e.g. 25 into 3 -> [8, 8, 9]. */
export function evenSplit(n, k) {
  const base = Math.floor(n / k);
  return Array.from({ length: k }, (_, i) => base + (i >= k - (n % k) ? 1 : 0));
}

const stageOf = (m) => (m.status !== 'Waiting' ? 'started' : m.heats?.length ? 'split' : 'queueing');

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
      standings = stageOf(match) === 'started' ? await api(`/api/v1/matches/${matchId}/standings`) : null;
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
    if (sizesText === null && m.heats?.length) sizesText = m.heats.map((h) => h.playerIds.length).join(', ');
    const canEdit = isAdmin && stage !== 'started';
    const stageText = {
      queueing: 'Taking players',
      split: 'Split into heats, waiting for the start',
      started: m.status === 'Completed' ? 'Finished' : 'Under way',
    }[stage];

    setHtml(root, html`<div class="league">
      <p class="small"><a href="#/">← ${say('All matches', 'All brawls')}</a></p>
      <div class="league-head">
        <div>
          <span class="tag cat ${m.category}">${m.category}</span>
          <h1 class="headline">${m.name}</h1>
          <p class="muted" style="margin:6px 0 0">${stageText} · ${fmt(m.tickIntervalMs)} ms per tick · ${fmt(m.maxTicks)} tick limit</p>
        </div>
        <div class="league-count"><b>${m.players.length}</b> ${stage === 'started' ? 'playing' : 'joined'}
          ${stage !== 'started' ? html`<br><span class="muted small">${connected} connected</span>` : ''}</div>
      </div>
      ${message ? html`<p class="${message.error ? 'error' : 'ok'}" role="status">${message.text}</p>` : ''}

      ${stage === 'queueing' ? html`<p class="muted">Verified bots join this match like any other
        (<code>POST /api/v1/matches/${m.matchId}/join</code>, or by name: <code>--join-match ${m.name}</code>) and long-poll its state.
        When the admin starts it, each bot is playing in its own heat.</p>` : ''}

      ${isAdmin ? adminHtml(m, stage, canEdit) : ''}

      ${stage === 'started' && standings ? dashboardHtml(standings) : ''}

      ${stage === 'split' ? html`<section aria-labelledby="heats-h">
        <h2 id="heats-h">Heats</h2>
        <div class="league-matches">${m.heats.map((h) => html`
          <article class="league-match">
            <h3>${h.name}<span class="muted small">${h.playerIds.length} players</span></h3>
            <ol class="league-players">${h.playerIds.map((id, i) => seatHtml(byId.get(id) ?? { playerId: id, name: '?' }, i, canEdit))}</ol>
          </article>`)}
        </div></section>` : ''}

      ${stage === 'queueing' ? html`<section aria-labelledby="queue-h">
        <h2 id="queue-h">Joined</h2>
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

  /** One live scoreboard per heat. */
  function dashboardHtml(heats) {
    return html`<section aria-labelledby="board-h">
      <h2 id="board-h">Heats</h2>
      <div class="heat-boards">${heats.map((h) => {
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
          <button class="btn" type="submit">${stage === 'split' ? 'Reshuffle' : 'Split into heats'}</button>
          ${stage === 'split' ? html`<button type="button" class="btn ghost" id="unsplit">Undo split (reopen to players)</button>` : ''}
        </div>
      </form>
      ${stage === 'split' ? html`<div class="start-box">
        <p>${idle ? html`<b>${idle}</b> of ${n} bots aren't connected. Remove them, or start anyway and they'll sit idle.` : html`All ${n} bots are connected.`}</p>
        <button type="button" class="btn start" id="start">Start all ${m.heats.length} heats</button>
      </div>` : ''}
      <p><button type="button" class="link-btn danger" id="cancel">Call off this match</button></p>
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
      if (stageOf(match) === 'started') standings = await api(`/api/v1/matches/${matchId}/standings`).catch(() => null);
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

// NetRts spectator UI: hash router + home view.
import { api, html, setHtml, slotVar, displayName, timeAgo, fmt, outcomeText, esc } from './util.js';
import { mountSpectate } from './spectate.js';

const main = document.getElementById('main');
let teardown = null;

function route() {
  if (teardown) { try { teardown(); } catch (e) { console.error(e); } }
  teardown = null;
  const hash = location.hash || '#/';
  const m = hash.match(/^#\/match\/([0-9a-fA-F-]{36})\/?$/);
  document.querySelectorAll('.topbar nav a[href^="#"]').forEach((a) => {
    if (m) a.removeAttribute('aria-current'); else a.setAttribute('aria-current', 'page');
  });
  // The battle view uses the whole screen; the home page keeps a readable width.
  main.classList.toggle('wide', Boolean(m));
  if (m) {
    teardown = mountSpectate(main, m[1]);
  } else {
    teardown = mountHome(main);
  }
  window.scrollTo(0, 0);
}

window.addEventListener('hashchange', () => { route(); main.focus({ preventScroll: true }); });

/* ------------------------------------------------------------------ home */

const SPEEDS = [
  { ms: 250, label: 'Fast' },
  { ms: 500, label: 'Normal' },
  { ms: 1000, label: 'Slow' },
];

function mountHome(root) {
  document.title = 'NetRts';
  setHtml(root, html`
    <div class="hero">
      <div>
        <h1 class="headline">Bots command the armies. You watch the war.</h1>
        <p class="lede">NetRts is a tick-based strategy game played entirely over a REST API.
          Programs gather ore, raise barracks and send soldiers across the map; this page lets you
          follow every match live, tile by tile.</p>
        <div class="hero-links">
          <a class="btn ghost" href="/scalar">Read the API docs</a>
          <a class="btn ghost" href="#write-a-bot">Write a bot</a>
        </div>
      </div>
      <form class="launch" id="launch" novalidate>
        <h2>Start an exhibition</h2>
        <p class="muted small" style="margin:0">Pick two house bots and watch them play each other.</p>
        <div class="versus">
          <div class="field">
            <label for="bot-a"><span class="side-swatch" style="background:var(--p0)"></span>First bot</label>
            <select id="bot-a" name="botA" required disabled><option>Loading…</option></select>
          </div>
          <span class="vs" aria-hidden="true">vs</span>
          <div class="field">
            <label for="bot-b"><span class="side-swatch" style="background:var(--p1)"></span>Second bot</label>
            <select id="bot-b" name="botB" required disabled><option>Loading…</option></select>
          </div>
        </div>
        <p class="bot-desc muted small" id="bot-desc" aria-live="polite"></p>
        <fieldset class="field">
          <legend>Tick speed</legend>
          <div class="segmented">
            ${SPEEDS.map((s, i) => html`
              <input type="radio" name="speed" id="speed-${s.ms}" value="${s.ms}" ${i === 1 ? html`checked` : ''}>
              <label for="speed-${s.ms}">${s.label} <span class="muted small ms">${s.ms} ms</span></label>`)}
          </div>
        </fieldset>
        <div><button class="btn" type="submit" id="launch-btn">Start match</button></div>
        <p class="error" id="launch-error" role="alert" hidden></p>
      </form>
    </div>

    <div class="board">
      <div>
        <section aria-labelledby="live-h">
          <div class="section-head">
            <h2 id="live-h">Live matches</h2>
            <span class="muted small" id="live-updated" aria-hidden="true"></span>
          </div>
          <div id="live" aria-live="polite" aria-busy="true"><p class="empty">Loading matches…</p></div>
        </section>
        <section aria-labelledby="hist-h">
          <h2 id="hist-h">Recent results</h2>
          <div id="history"><p class="empty">Loading results…</p></div>
        </section>
      </div>
      <div>
        <section aria-labelledby="ladder-h">
          <h2 id="ladder-h">Leaderboard</h2>
          <div id="ladder"><p class="empty">Loading leaderboard…</p></div>
        </section>
        <section aria-labelledby="bot-h" id="write-a-bot">
          <h2 id="bot-h">Write a bot</h2>
          <ol class="steps">
            <li><p>Register a player and keep the <code>apiKey</code> it returns.</p>
              <pre class="snippet">POST /api/v1/players
{"name": "mybot"}</pre></li>
            <li><p>Create a match against a house bot, sending your key.</p>
              <pre class="snippet">POST /api/v1/matches
Authorization: Bearer &lt;apiKey&gt;
{"houseBots": ["sitter"]}</pre></li>
            <li><p>Long-poll your fog-of-war view, one tick at a time.</p>
              <pre class="snippet">GET /api/v1/matches/{id}/state?waitForTick=N</pre></li>
            <li><p>Send orders: move, gather, build, produce, attack.</p>
              <pre class="snippet">POST /api/v1/matches/{id}/commands</pre></li>
          </ol>
          <p class="small muted">Full schemas and every command are in the <a href="/scalar">API docs</a>.</p>
        </section>
      </div>
    </div>`);

  let alive = true;
  let timer = null;
  let cycle = 0;
  const $ = (id) => root.querySelector('#' + id);

  // ---- exhibition form
  let bots = [];
  const selA = $('bot-a'), selB = $('bot-b'), desc = $('bot-desc'), errEl = $('launch-error'), btn = $('launch-btn');
  const showDesc = () => {
    const a = bots.find((b) => b.name === selA.value), b = bots.find((x) => x.name === selB.value);
    desc.innerHTML = [a, b].filter(Boolean).map((x) => `<b>${esc(x.name)}</b>: ${esc(x.description)}`).join('<br>');
  };
  selA.addEventListener('change', showDesc);
  selB.addEventListener('change', showDesc);

  api('/api/v1/bots').then((list) => {
    if (!alive) return;
    bots = list || [];
    const opts = bots.map((b) => `<option value="${esc(b.name)}">${esc(b.name)}</option>`).join('');
    selA.innerHTML = opts; selB.innerHTML = opts;
    const pick = (n, fallback) => (bots.some((b) => b.name === n) ? n : fallback);
    selA.value = pick('rusher', bots[0]?.name);
    selB.value = pick('economist', bots[1]?.name ?? bots[0]?.name);
    selA.disabled = selB.disabled = bots.length === 0;
    showDesc();
  }).catch((e) => {
    if (!alive) return;
    selA.innerHTML = selB.innerHTML = '<option>Unavailable</option>';
    showError(`Could not load house bots. ${e.message}`);
  });

  function showError(msg) { errEl.textContent = msg; errEl.hidden = !msg; }

  $('launch').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    showError('');
    if (!selA.value || !selB.value || selA.disabled) { showError('Choose two house bots first.'); return; }
    if (selA.value === selB.value) { showError('Pick two different bots. Each house bot can take only one seat in a match.'); return; }
    const speed = Number(new FormData(ev.target).get('speed')) || 500;
    btn.disabled = true;
    btn.textContent = 'Starting…';
    try {
      const summary = await api('/api/v1/exhibitions', {
        method: 'POST',
        body: { bots: [selA.value, selB.value], settings: { tickIntervalMs: speed } },
      });
      location.hash = `#/match/${summary.matchId}`;
    } catch (e) {
      showError(e.code === 'TOO_MANY_EXHIBITIONS'
        ? `Too many exhibitions are running right now. Watch one below, or try again when one finishes.`
        : e.status === 429
          ? `Too many requests from this address. Wait a minute, then start the match again.`
          : `Could not start the match: ${e.message}`);
      btn.disabled = false;
      btn.textContent = 'Start match';
    }
  });

  // ---- lists
  async function refresh() {
    const jobs = [loadLive()];
    if (cycle % 5 === 0) jobs.push(loadHistory(), loadLadder());
    cycle++;
    await Promise.allSettled(jobs);
    if (alive) timer = setTimeout(refresh, 3000);
  }

  async function loadLive() {
    const el = $('live');
    try {
      const all = await api('/api/v1/matches');
      if (!alive) return;
      // Finished matches move to "Recent results"; keep this list to what you can watch live.
      const list = all.filter((m) => m.status !== 'Completed');
      const order = { Active: 0, Waiting: 1, Completed: 2 };
      list.sort((a, b) => (order[a.status] - order[b.status]) || (Date.parse(b.createdAt) - Date.parse(a.createdAt)));
      setHtml(el, list.length
        ? html`<ul class="match-list">${list.map(matchRow)}</ul>`
        : html`<p class="empty">No matches running. Start an exhibition above, or point a bot at the API.</p>`);
      $('live-updated').textContent = `updated ${new Date().toLocaleTimeString()}`;
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load live matches: ${e.message}</p>`);
    } finally {
      el.setAttribute('aria-busy', 'false');
    }
  }

  async function loadHistory() {
    const el = $('history');
    try {
      const list = await api('/api/v1/matches/history?limit=10');
      if (!alive) return;
      setHtml(el, list.length
        ? html`<ul class="match-list">${list.map(matchRow)}</ul>`
        : html`<p class="empty">No finished matches yet.</p>`);
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load results: ${e.message}</p>`);
    }
  }

  async function loadLadder() {
    const el = $('ladder');
    try {
      const list = await api('/api/v1/leaderboard');
      if (!alive) return;
      setHtml(el, list.length ? html`
        <table class="ladder">
          <caption class="visually-hidden">Players ranked by rating</caption>
          <thead><tr><th scope="col">#</th><th scope="col">Player</th><th scope="col">Rating</th>
            <th scope="col"><abbr title="Wins">W</abbr></th><th scope="col"><abbr title="Losses">L</abbr></th><th scope="col"><abbr title="Draws">D</abbr></th></tr></thead>
          <tbody>${list.map((p) => html`
            <tr><td>${p.rank}</td>
              <td>${displayName(p.name)}${p.isHouseBot ? html`<span class="tag">house</span>` : ''}</td>
              <td class="rating">${Math.round(p.rating)}</td><td>${p.wins}</td><td>${p.losses}</td><td>${p.draws}</td></tr>`)}
          </tbody>
        </table>` : html`<p class="empty">No ranked players yet.</p>`);
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load the leaderboard: ${e.message}</p>`);
    }
  }

  refresh();

  return () => { alive = false; clearTimeout(timer); };
}

function matchRow(m) {
  const players = [...(m.players || [])].sort((a, b) => a.slot - b.slot);
  const names = players.length
    ? players.map((p, i) => html`${i ? html`<span class="vs-sep">vs</span>` : ''}<span class="pname" style="--pc:${slotVar(p.slot)}">${displayName(p.name)}</span>`)
    : html`<span class="muted">no players yet</span>`;
  const open = m.maxPlayers - players.length;
  let meta;
  if (m.status === 'Completed' && m.outcome) {
    meta = html`${outcomeText(m.outcome, m.players)}<br>${timeAgo(m.createdAt)}`;
  } else if (m.status === 'Waiting') {
    meta = html`${open} seat${open === 1 ? '' : 's'} open`;
  } else {
    const pct = m.maxTicks ? Math.min(100, (m.tick / m.maxTicks) * 100) : 0;
    meta = html`tick ${fmt(m.tick)} / ${fmt(m.maxTicks)}<div class="minibar" aria-hidden="true"><span style="width:${pct.toFixed(1)}%"></span></div>`;
  }
  return html`<li><a class="match-row" href="#/match/${m.matchId}" aria-label="${m.status} match: ${players.map((p) => displayName(p.name)).join(' versus ')}">
    <span class="status ${m.status}">${m.status === 'Active' ? 'Live' : m.status}</span>
    <span class="who">${names}<span class="muted small" style="margin-left:8px">${m.mapWidth}×${m.mapHeight}</span></span>
    <span class="meta">${meta}</span></a></li>`;
}

route();

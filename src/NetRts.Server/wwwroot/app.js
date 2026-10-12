// Spectator UI: hash router + home view.
import { api, html, raw, setHtml, slotVar, displayName, playerLabel, verifiedBadge, timeAgo, fmt, outcomeHtml, esc } from './util.js';
import { GAME, SNOW, say, nickname } from './lore.js';
import { mountSpectate } from './spectate.js';
import { mountAccount } from './account.js';
import { mountLeague } from './league.js';

const main = document.getElementById('main');
let teardown = null;

function route() {
  if (teardown) { try { teardown(); } catch (e) { console.error(e); } }
  teardown = null;
  const hash = location.hash || '#/';
  // A match id, or the name a bot gave its match.
  const m = hash.match(/^#\/match\/([0-9a-fA-F-]{36}|[A-Za-z0-9_-]{3,32})\/?$/);
  const account = /^#\/account(\/|\?|$)/.test(hash);
  const section = m ? 'match' : account ? 'account' : 'home';
  document.querySelectorAll('.topbar nav a[data-section]').forEach((a) => {
    if (a.dataset.section === section) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
  });
  // The battle view uses the whole screen; the home page keeps a readable width.
  main.classList.toggle('wide', Boolean(m));
  if (m) {
    teardown = mountMatch(main, m[1]);
  } else if (account) {
    teardown = mountAccount(main, showAccountLink);
  } else {
    teardown = mountHome(main);
  }
  window.scrollTo(0, 0);
}

window.addEventListener('hashchange', () => { route(); main.focus({ preventScroll: true }); });

/**
 * A match page: league matches (played in heats) get the queue / heats / scoreboard page, every other
 * match the battle view.
 */
function mountMatch(root, key) {
  let inner = null;
  let alive = true;
  const byId = /^[0-9a-fA-F-]{36}$/.test(key);
  api(byId ? `/api/v1/matches/${key}` : `/api/v1/matches/named/${encodeURIComponent(key)}`)
    .then((summary) => summary?.heats ? mountLeague(root, summary) : mountSpectate(root, key))
    .catch(() => mountSpectate(root, key))   // the battle view explains a missing match
    .then((teardown) => { if (alive) inner = teardown; else teardown?.(); });
  return () => { alive = false; inner?.(); };
}

/** The top bar's account link: who is signed in (with their blue check), or an invitation to sign in. */
function showAccountLink(info) {
  const link = document.getElementById('account-link');
  if (!info?.discordEnabled) { link.hidden = true; return; }
  link.hidden = false;
  const a = info.account;
  setHtml(link, info.signedIn && a
    ? html`${a.avatarUrl ? html`<img class="avatar" src="${a.avatarUrl}" alt="" width="22" height="22">` : ''}${a.displayName}${verifiedBadge({ verified: a.verified })}`
    : html`Sign in with Discord`);
}
api('/api/v1/account').then(showAccountLink).catch(() => {});

/* ------------------------------------------------------------------ home */

/** Each category is its own leaderboard, in the order a year plays out. */
const CATEGORIES = [
  { key: 'Practice', label: say('Practice', 'Scrimmage'), note: 'Every match bots start themselves. Open to everyone.' },
  { key: 'Preseason', label: say('Preseason', 'Preseason'), note: 'Warm-up league matches (beta testing). Ranked by points; they don\'t count toward the season.' },
  { key: 'Season', label: say('Season', 'Season'), note: 'Regular-season matches run by the admins in heats; Discord-verified bots only. Ranked by points: each heat\'s top four score their margins over the players below them.' },
  { key: 'Championship', label: say('Championship', 'Championship'), note: 'Championship matches; Discord-verified bots only. Ranked by points, like the season.' },
];

/**
 * The tab to open on: the latest phase of this season that has had a match (a league ladder only
 * has rows once one of its matches is recorded), else Practice. Labelling the season in the
 * database for next year empties them all again.
 */
async function currentPhase() {
  const later = CATEGORIES.slice(1).reverse();
  const counts = await Promise.all(later.map((c) =>
    api(`/api/v1/leaderboard?category=${c.key}&limit=1`).then((l) => l.length, () => 0)));
  return later.find((_, i) => counts[i] > 0)?.key ?? 'Practice';
}

const SPEEDS = [
  { ms: 250, label: say('Fast', 'Honey badger') },
  { ms: 500, label: say('Normal', 'Brisk') },
  { ms: 1000, label: say('Slow', 'Hibernating') },
];

function mountHome(root) {
  document.title = GAME;
  setHtml(root, html`
    <div class="hero">
      <div>
        ${SNOW ? html`<span class="go-badgers">Snow College · Est. 1888 · Go Badgers</span>` : ''}
        <h1 class="headline">${raw(say('Bots command the armies. You watch the war.', 'Bots command the badgers. You watch the <em>brawl</em>.'))}</h1>
        <p class="lede">${say(`NetRTS ("Net R-T-S", a Network Real-Time Strategy game) is a tick-based strategy game played entirely over a REST API.
          Programs gather ore, raise barracks and send soldiers across the map; this page lets you
          follow every match live, tile by tile.`, `Badger Brawl is Snow College's tick-based strategy game, played entirely over a REST API.
          Programs send badger workers after grubs, move into the Suites at Academy Square, study Organic
          Chemistry and Code Review in the GRSC Makerspace, and march soldiers and archers across the Sanpete
          Valley at a rival's Noyes Building; this page lets you follow every brawl live, tile by tile.
          It runs on NetRTS ("Net R-T-S"), a Network Real-Time Strategy engine.`)}</p>
        <div class="hero-links">
          <a class="btn ghost" href="/scalar">Read the API docs</a>
          <a class="btn ghost" href="#write-a-bot">${say('Write a bot', 'Train your badger (Get Started)')}</a>
        </div>
      </div>
      <div class="promo">
        <button type="button" class="promo-play" id="promo-play">
          <img src="/images/trailer-poster.jpg" alt="" width="1280" height="720">
          <span class="promo-icon" aria-hidden="true"></span>
          <span class="promo-label">${say('Watch the trailer', 'Watch the trailer')} <span class="muted">· 2:00</span></span>
        </button>
      </div>
    </div>

    <div class="board">
      <div>
        <section aria-labelledby="live-h">
          <div class="section-head">
            <h2 id="live-h">${say('Live matches', 'Live brawls')}</h2>
            <span class="muted small" id="live-updated" aria-hidden="true"></span>
          </div>
          <div id="live" aria-live="polite" aria-busy="true"><p class="empty">${say('Loading matches…', 'Waking the badgers…')}</p></div>
        </section>
        <section aria-labelledby="hist-h">
          <h2 id="hist-h">${say('Recent results', 'Recent brawls')}</h2>
          <div id="history"><p class="empty">Loading results…</p></div>
        </section>
      </div>
      <div>
        <section>
          <form class="launch" id="launch" novalidate>
            <h2>${say('Start an exhibition', 'Start a scrimmage')}</h2>
            <p class="muted small" style="margin:0">${say('Pick 2 to 16 house bots (as many copies of each as you like) and watch them play each other.', 'Pick 2 to 16 house badgers (as many copies of each as you like) and watch them have it out.')}</p>
            <fieldset class="field">
              <legend>${say('House bots', 'House badgers')}</legend>
              <div class="bot-counts" id="bot-counts"><p class="muted small" style="margin:0">Loading…</p></div>
              <p class="muted small" id="bot-total" aria-live="polite" style="margin:0"></p>
            </fieldset>
            <p class="bot-desc muted small" id="bot-desc" aria-live="polite"></p>
            <fieldset class="field">
              <legend>${say('Tick speed', 'Pace')}</legend>
              <div class="segmented">
                ${SPEEDS.map((s, i) => html`
                  <input type="radio" name="speed" id="speed-${s.ms}" value="${s.ms}" ${i === 1 ? html`checked` : ''}>
                  <label for="speed-${s.ms}">${s.label} <span class="muted small ms">${s.ms} ms</span></label>`)}
              </div>
            </fieldset>
            <div><button class="btn" type="submit" id="launch-btn">${say('Start match', 'Let them brawl')}</button></div>
            <p class="error" id="launch-error" role="alert" hidden></p>
          </form>
        </section>
        <section aria-labelledby="ladder-h">
          <h2 id="ladder-h">${say('Leaderboard', 'Top Badgers')}</h2>
          <fieldset class="ladder-tabs">
            <legend class="visually-hidden">Leaderboard</legend>
            <div class="segmented">
              ${CATEGORIES.map((c) => html`
                <input type="radio" name="ladder-cat" id="cat-${c.key}" value="${c.key}" ${c.key === 'Practice' ? html`checked` : ''}>
                <label for="cat-${c.key}">${c.label}</label>`)}
            </div>
          </fieldset>
          <p class="muted small" id="ladder-note" style="margin:8px 0 0"></p>
          <div id="ladder"><p class="empty">Loading leaderboard…</p></div>
        </section>
        <section aria-labelledby="bot-h" id="write-a-bot">
          <h2 id="bot-h">${say('Write a bot', 'Train your badger (Getting Started)')}</h2>
          <ol class="steps">
            <li><p>Register a player and keep the <code>apiKey</code> it returns.</p>
              <pre class="snippet">POST /api/v1/players
{"name": "${say('mybot', 'buster-the-badger')}"}</pre></li>
            <li><p>${say('Create a match against a house bot, sending your key.', 'Challenge a house badger, sending your key.')}</p>
              <pre class="snippet">POST /api/v1/matches
Authorization: Bearer &lt;apiKey&gt;
{"houseBots": ["sitter"]}</pre></li>
            <li><p>Long-poll your fog-of-war view, one tick at a time.</p>
              <pre class="snippet">GET /api/v1/matches/{id}/state?waitForTick=N</pre></li>
            <li><p>${say('Send orders: move, gather, build, produce, attack.', 'Send orders: move, dig for grubs, build, train, attack.')}</p>
              <pre class="snippet">POST /api/v1/matches/{id}/commands</pre></li>
          </ol>
          <p class="small muted">Full schemas and every command are in the <a href="/scalar">API docs</a>.</p>
        </section>
      </div>
    </div>`);

  let alive = true;
  let timer = null;
  let cycle = 0;
  let liveIds = null;   // the matches in the live list last time, to spot ones that just finished
  const $ = (id) => root.querySelector('#' + id);

  // ---- exhibition form
  let bots = [];
  const desc = $('bot-desc'), errEl = $('launch-error'), btn = $('launch-btn'), countsEl = $('bot-counts'), totalEl = $('bot-total');
  const MAX_SEATS = 16;
  const counts = new Map();   // bot name -> how many copies take a seat
  const picked = () => bots.flatMap((b) => Array(counts.get(b.name) || 0).fill(b.name));
  const label = (name) => (nickname(name) ? `${nickname(name)} (${name})` : name);
  const showCounts = () => {
    const total = picked().length;
    totalEl.textContent = `${total} ${say('bot', 'badger')}${total === 1 ? '' : 's'} · ${total > 4 ? 'ring map' : 'corner map'}`;
    desc.innerHTML = bots.filter((b) => counts.get(b.name))
      .map((x) => (nickname(x.name) ? esc(x.description) : `<b>${esc(x.name)}</b>: ${esc(x.description)}`)).join('<br>');
  };
  countsEl.addEventListener('input', (e) => {
    const n = Math.max(0, Math.min(MAX_SEATS, Math.floor(Number(e.target.value) || 0)));
    counts.set(e.target.dataset.bot, n);
    showCounts();
  });

  api('/api/v1/bots').then((list) => {
    if (!alive) return;
    bots = list || [];
    for (const b of bots) counts.set(b.name, 0);
    if (counts.has('rusher')) counts.set('rusher', 1); else if (bots[0]) counts.set(bots[0].name, 1);
    if (counts.has('economist')) counts.set('economist', 1); else if (bots[1]) counts.set(bots[1].name, 1);
    countsEl.innerHTML = bots.map((b, i) => `
      <label class="bot-count"><input type="number" min="0" max="${MAX_SEATS}" step="1" inputmode="numeric"
        id="count-${i}" data-bot="${esc(b.name)}" value="${counts.get(b.name)}"> ${esc(label(b.name))}</label>`).join('');
    showCounts();
  }).catch((e) => {
    if (!alive) return;
    countsEl.innerHTML = '<p class="muted small" style="margin:0">Unavailable</p>';
    showError(`${say('Could not load house bots.', 'Could not wake the house badgers.')} ${e.message}`);
  });

  function showError(msg) { errEl.textContent = msg; errEl.hidden = !msg; }

  $('launch').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    showError('');
    const seats = picked();
    if (seats.length < 2 || seats.length > MAX_SEATS) {
      showError(say(`Pick 2 to ${MAX_SEATS} house bots in all.`, `Pick 2 to ${MAX_SEATS} house badgers in all.`));
      return;
    }
    const speed = Number(new FormData(ev.target).get('speed')) || 500;
    btn.disabled = true;
    btn.textContent = say('Starting…', 'Digging in…');
    try {
      const summary = await api('/api/v1/exhibitions', {
        method: 'POST',
        body: { bots: seats, settings: { tickIntervalMs: speed } },
      });
      location.hash = `#/match/${summary.matchId}`;
    } catch (e) {
      showError(e.code === 'TOO_MANY_EXHIBITIONS'
        ? `Too many ${say('exhibitions', 'scrimmages')} are running right now. Watch one below, or try again when one finishes.`
        : e.status === 429
          ? `Too many requests from this address. Wait a minute, then start the match again.`
          : `Could not start the ${say('match', 'brawl')}: ${e.message}`);
      btn.disabled = false;
      btn.textContent = say('Start match', 'Let them brawl');
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
      const list = all.filter((m) => m.status !== 'Completed' && !m.parentMatchId);
      // A brawl that just finished belongs in the results and may have moved the ladder: refresh them now.
      const ids = new Set(list.map((m) => m.matchId));
      if (liveIds && [...liveIds].some((id) => !ids.has(id))) {
        loadHistory();
        loadLadder();
      }
      liveIds = ids;
      const order = { Active: 0, Waiting: 1, Completed: 2 };
      list.sort((a, b) => (order[a.status] - order[b.status]) || (Date.parse(b.createdAt) - Date.parse(a.createdAt)));
      setHtml(el, list.length
        ? html`<ul class="match-list">${list.map((m) => matchRow(m))}</ul>`
        : html`<p class="empty">${say('No matches running. Start an exhibition, or point a bot at the API.', 'The field is empty. Start a scrimmage, or point a bot at the API.')}</p>`);
      $('live-updated').textContent = `updated ${new Date().toLocaleTimeString()}`;
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load live ${say('matches', 'brawls')}: ${e.message}</p>`);
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
        ? html`<ul class="match-list">${list.map((m) => matchRow(m, { status: false }))}</ul>`
        : html`<p class="empty">${say('No finished matches yet.', "No finished brawls yet. The season hasn't started.")}</p>`);
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load results: ${e.message}</p>`);
    }
  }

  // The trailer is a still until it's played: YouTube (and its channel banner) only loads on click.
  $('promo-play').addEventListener('click', (e) => {
    const frame = document.createElement('iframe');
    frame.src = 'https://www.youtube-nocookie.com/embed/sVGlA3aRt90?autoplay=1&rel=0';
    frame.title = say('NetRTS in action', 'Badger Brawl: the trailer');
    frame.allow = 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share';
    frame.referrerPolicy = 'strict-origin-when-cross-origin';
    frame.allowFullscreen = true;
    e.currentTarget.replaceWith(frame);
    frame.focus();
  });

  // Open on the current phase unless they've already picked a tab.
  let tabChosen = false;
  root.querySelector('.ladder-tabs').addEventListener('change', () => {
    tabChosen = true;
    loadLadder();
  });
  const phase = currentPhase().then((key) => {
    if (alive && !tabChosen) root.querySelector(`#cat-${key}`).checked = true;
  });

  let shownCategory = null;
  async function loadLadder() {
    await phase;
    const el = $('ladder');
    const category = root.querySelector('input[name="ladder-cat"]:checked')?.value || 'Practice';
    const info = CATEGORIES.find((c) => c.key === category);
    $('ladder-note').textContent = info?.note ?? '';
    // A different tab: never leave the previous tab's table showing while this one loads.
    if (category !== shownCategory) {
      shownCategory = category;
      setHtml(el, html`<p class="empty">Loading ${info?.label ?? ''}…</p>`);
    }
    try {
      const list = await api(`/api/v1/leaderboard?category=${category}`);
      if (!alive || category !== root.querySelector('input[name="ladder-cat"]:checked')?.value) return;
      setHtml(el, list.length ? html`
        <div class="table-scroll"><table class="ladder">
          <caption class="visually-hidden">${say('Players ranked by rating', 'Badgers ranked by rating')}</caption>
          <thead><tr><th scope="col">#</th><th scope="col">Player</th>${category !== 'Practice' ? html`<th scope="col"><abbr title="League points: score margins over the rest of each match's top four">Pts</abbr></th>` : ''}<th scope="col">Rating</th>
            <th scope="col"><abbr title="Wins">W</abbr></th><th scope="col"><abbr title="Losses">L</abbr></th><th scope="col"><abbr title="Draws">D</abbr></th></tr></thead>
          <tbody>${list.map((p) => html`
            <tr><td>${p.rank}</td>
              <td>${playerLabel(p)}${p.isHouseBot ? html`<span class="tag">house</span>` : ''}</td>
              ${category !== 'Practice' ? html`<td class="rating">${fmt(p.points)}</td>` : ''}<td class="${category === 'Practice' ? 'rating' : ''}">${Math.round(p.rating)}</td><td>${p.wins}</td><td>${p.losses}</td><td>${p.draws}</td></tr>`)}
          </tbody>
        </table></div>` : category === 'Practice'
        ? html`<p class="empty">${say('No ranked players yet.', 'No ranked badgers yet.')}</p>`
        : html`<div class="coming-soon">
            <p class="coming-soon-title">Coming soon</p>
            <p class="muted small">The ${info.label.toLowerCase()} leaderboard opens after the first ${info.label.toLowerCase()} match is played.</p>
          </div>`);
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load the leaderboard: ${e.message}</p>`);
    }
  }

  refresh();

  return () => { alive = false; clearTimeout(timer); };
}

/** One row of a match list. Finished matches leave out the status column: they all say "Completed". */
function matchRow(m, { status = true } = {}) {
  const players = [...(m.players || [])].sort((a, b) => a.slot - b.slot);
  // A league match lists who has joined (they're split into heats later), not a matchup.
  const shown = m.heats ? players.slice(0, 12) : players;
  const names = !players.length
    ? html`<span class="muted">${say('no players yet', 'no badgers yet')}</span>`
    : m.heats
      ? html`${shown.map((p, i) => html`${i ? ', ' : ''}${playerLabel(p, { owner: false })}`)}${players.length > shown.length ? html` <span class="muted">+${players.length - shown.length} more</span>` : ''}`
      : players.map((p, i) => html`${i ? html`<span class="vs-sep">vs</span>` : ''}<span class="pname" style="--pc:${slotVar(p.slot)}">${playerLabel(p, { owner: false })}</span>`);
  const open = m.maxPlayers - players.length;
  let meta;
  if (m.heats) {
    // A league match, played in heats.
    meta = m.status === 'Waiting'
      ? (m.heats.length ? html`${m.heats.length} heats · starting soon` : html`<b>${players.length}</b> joined · verified bots, join now`)
      : html`${m.heats.filter((h) => h.status !== 'Completed').length} of ${m.heats.length} heats live`;
  } else if (m.status === 'Completed' && m.outcome) {
    meta = html`${outcomeHtml(m.outcome, m.players)}<br>${timeAgo(m.createdAt)}`;
  } else if (m.status === 'Waiting') {
    meta = html`${open} seat${open === 1 ? '' : 's'} open`;
  } else {
    const pct = m.maxTicks ? Math.min(100, (m.tick / m.maxTicks) * 100) : 0;
    meta = html`tick ${fmt(m.tick)} / ${fmt(m.maxTicks)}<div class="minibar" aria-hidden="true"><span style="width:${pct.toFixed(1)}%"></span></div>`;
  }
  // A name keeps pointing at the newest match that uses it, so finished matches link by id.
  const key = m.name && m.status !== 'Completed' ? m.name : m.matchId;
  return html`<li><a class="match-row ${status ? '' : 'no-status'}" href="#/match/${key}" aria-label="${m.status} ${say('match', 'brawl')}: ${players.map((p) => displayName(p.name)).join(' versus ')}">
    ${status ? html`<span class="status ${m.status}">${m.status === 'Active' ? 'Live' : m.status}</span>` : ''}
    <span class="who">${m.category && m.category !== 'Practice' ? html`<span class="tag cat ${m.category}">${m.category}</span>` : ''}${m.name ? html`<b style="margin-right:8px">${m.name}</b>` : ''}${names}${m.heats ? '' : html`<span class="muted small" style="margin-left:8px">${m.mapWidth}×${m.mapHeight}</span>`}</span>
    <span class="meta">${meta}</span></a></li>`;
}

route();

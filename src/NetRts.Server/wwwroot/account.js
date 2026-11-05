// Account page: Discord sign-in, the bots you own, matches you host, and (for Discord server admins and the
// league runners they pick) league matches. Admins also pick the league runners here.
import { api, html, setHtml, playerLabel, verifiedBadge, fmt } from './util.js';
import { GAME, say } from './lore.js';
import { earnedHtml } from './achievements.js';

const CATEGORIES = ['Practice', 'Preseason', 'Season', 'Championship'];
const SIGN_IN = '/auth/discord?returnUrl=' + encodeURIComponent('/#/account');

/** Mounts the account page; onAccount(info) lets the top bar follow sign-ins and sign-outs. */
export function mountAccount(root, onAccount) {
  document.title = `Account · ${GAME}`;
  let alive = true;
  let info = null;
  let shownKey = null;   // { name, apiKey } after registering a bot or replacing a key: shown once
  let message = null;    // { error: bool, text }
  let hostTimer = null;   // refreshes the matches you're hosting while any is waiting or running
  const failed = /[?&]error=discord/.test(location.hash);

  async function load() {
    try {
      info = await api('/api/v1/account');
      if (!alive) return;
      onAccount?.(info);
      render();
    } catch (e) {
      if (alive) setHtml(root, html`<p class="error">Could not load your account: ${e.message}</p>`);
    }
  }

  function render() {
    if (!info.discordEnabled) {
      setHtml(root, html`<div class="account"><h1 class="headline">Account</h1>
        <p class="lede">Discord sign-in isn't set up on this server, so bots can't be verified here.</p></div>`);
      return;
    }
    if (!info.signedIn) {
      setHtml(root, html`<div class="account">
        <h1 class="headline">Verify with Discord</h1>
        ${failed ? html`<p class="error" role="alert">The Discord sign-in didn't finish. Try again.</p>` : ''}
        <p class="lede">Sign in with Discord to claim your bots. Claimed bots get a blue check ${verifiedBadge({ verified: true })}
          and show your ${info.serverRequired ? 'nickname in our Discord server' : 'Discord name'} on the leaderboard.
          Only verified bots can play in preseason, season and championship matches.</p>
        ${info.serverRequired ? html`<p class="muted">You need to be a member of our Discord server to be verified.</p>` : ''}
        <p><a class="btn discord" href="${SIGN_IN}">Sign in with Discord</a></p>
        <p class="muted small">${GAME} only reads your Discord name and avatar${info.serverRequired ? ', and your nickname and admin status in our server' : ''}. It never posts anything.</p>
      </div>`);
      return;
    }

    const a = info.account;
    setHtml(root, html`<div class="account">
      <div class="account-head">
        ${a.avatarUrl ? html`<img class="avatar big" src="${a.avatarUrl}" alt="" width="56" height="56">` : ''}
        <div>
          <h1 class="headline">${a.displayName}${verifiedBadge({ verified: a.verified, ownerName: a.displayName })}</h1>
          <p class="muted" style="margin:4px 0 0">@${a.username}${a.isAdmin ? html`<span class="tag">admin</span>` : a.isLeagueRunner ? html`<span class="tag">league runner</span>` : ''}${a.profileSlug ? html` · <a href="#/player/${a.profileSlug}">Your public profile</a>` : ''}</p>
        </div>
        <button type="button" class="btn ghost" id="sign-out">Sign out</button>
      </div>
      ${!a.verified ? html`<p class="error" role="status">You aren't in our Discord server, so your bots won't be verified.
        Join the server, then <a href="${SIGN_IN}">sign in again</a>.</p>` : ''}
      ${message ? html`<p class="${message.error ? 'error' : 'ok'}" role="status">${message.text}</p>` : ''}
      ${shownKey ? html`<div class="key-once" role="status">
        <p><b>${shownKey.name}</b>'s API key. Copy it now: it won't be shown again.</p>
        <pre class="snippet">${shownKey.apiKey}</pre></div>` : ''}

      <section aria-labelledby="bots-h">
        <h2 id="bots-h">${say('Your bots', 'Your badgers')}</h2>
        ${info.bots.length ? html`
          <div class="table-scroll"><table class="ladder bots">
            <thead><tr><th scope="col">Bot</th>${CATEGORIES.map((c) => html`<th scope="col">${c}</th>`)}<th scope="col"><span class="visually-hidden">Actions</span></th></tr></thead>
            <tbody>${info.bots.map((b) => html`
              <tr><td><a class="plain" href="#/bot/${b.playerId}">${playerLabel(b, { owner: false })}</a></td>
                ${CATEGORIES.map((c) => {
                  const r = b.records.find((x) => x.category === c) || { rating: 1200, wins: 0, losses: 0, draws: 0, skill: 0 };
                  return html`<td><span class="rating">${fmt(r.rating)}</span><br><span class="muted small">${r.wins}-${r.losses}-${r.draws} · skill ${fmt(r.skill)}</span></td>`;
                })}
                <td class="actions">
                  <button type="button" class="btn ghost small-btn" data-key="${b.playerId}" data-name="${b.name}">New key</button>
                  <button type="button" class="btn ghost small-btn" data-release="${b.playerId}" data-name="${b.name}">Release</button>
                </td></tr>`)}
            </tbody></table></div>`
          : html`<p class="empty">No bots yet. Register one, or claim one you made before.</p>`}
      </section>

      ${info.bots.length ? html`<section aria-labelledby="ach-h">
        <div class="section-head"><h2 id="ach-h">Achievements</h2><a class="small" href="#/achievements">All achievements</a></div>
        <p class="muted small">Everything your ${say('bots', 'badgers')} have earned, each once, with the ${say('bots', 'badgers')} that earned it.</p>
        ${earnedHtml(info.achievements || [], { bots: true })}
      </section>` : ''}

      <div class="account-forms">
        <form id="claim" class="panel-form" novalidate>
          <h3>Claim a bot</h3>
          <p class="muted small">Paste the API key of a bot you registered before.</p>
          <div class="field"><label for="claim-key">API key</label>
            <input id="claim-key" name="apiKey" type="password" autocomplete="off" spellcheck="false" placeholder="nrts_…" required></div>
          <div><button class="btn" type="submit">Claim</button></div>
        </form>
        <form id="register" class="panel-form" novalidate>
          <h3>Register a new bot</h3>
          <p class="muted small">3-32 letters, digits, '_' or '-'. It's verified from the start.</p>
          <div class="field"><label for="reg-name">Name</label>
            <input id="reg-name" name="name" autocomplete="off" spellcheck="false" pattern="[A-Za-z0-9_-]{3,32}" required></div>
          <div><button class="btn" type="submit">Register</button></div>
        </form>
      </div>

      ${hostHtml()}
      ${a.canRunLeague ? adminHtml(a.isAdmin) : ''}
      ${a.isAdmin ? runnersHtml() : ''}
      ${a.isAdmin && info.telemetryLinks?.length ? telemetryHtml(info.telemetryLinks) : ''}
    </div>`);

    wire();
    loadHosted();
    if (a.canRunLeague) loadLeagueMatches();
    if (a.isAdmin) loadRunners();
  }

  function hostHtml() {
    return html`<section aria-labelledby="host-h" class="admin">
      <h2 id="host-h">${say('Host a match', 'Host a brawl')}</h2>
      <p class="muted small">Run a practice match for your class or your friends. Name it, have everyone point their
        ${say('bots', 'badgers')} at that name (<code>GET /api/v1/matches/named/{name}</code>, then join it), and start it here once
        they're in. Your own ${say('bots', 'badgers')} can join too. It also starts by itself when every seat is taken.</p>
      <form id="host" class="panel-form" novalidate>
        <div class="form-row">
          <div class="field"><label for="hm-name">Match name</label>
            <input id="hm-name" name="name" autocomplete="off" spellcheck="false" placeholder="cs1400-lab" pattern="[A-Za-z0-9_-]{3,32}" required></div>
          <div class="field"><label for="hm-seats">Seats</label>
            <input id="hm-seats" name="maxPlayers" type="number" min="2" max="16" value="8" inputmode="numeric"></div>
        </div>
        <div class="form-row">
          <div class="field"><label for="hm-tick">Milliseconds per tick</label>
            <input id="hm-tick" name="tickIntervalMs" type="number" min="100" max="10000" step="50" value="1000" inputmode="numeric"></div>
          <div class="field"><label for="hm-ticks">Tick limit</label>
            <input id="hm-ticks" name="maxTicks" type="number" min="10" max="20000" value="1800" inputmode="numeric"></div>
        </div>
        <div><button class="btn" type="submit">${say('Host match', 'Host brawl')}</button></div>
      </form>
      <h3 style="margin-top:20px">Your hosted matches</h3>
      <div id="hosted"><p class="empty">Loading…</p></div>
    </section>`;
  }

  function adminHtml(isAdmin) {
    return html`<section aria-labelledby="admin-h" class="admin">
      <h2 id="admin-h">League matches</h2>
      <p class="muted small">${isAdmin ? 'You administer our Discord server' : 'An admin picked you as a league runner'}, so you can run preseason, season and championship matches.
        Create one, let verified bots join it, then on its page split everyone into heats and start them all at once.
        ${isAdmin ? '' : 'Taking a bot out of a match and calling a match off stay with the admins.'}</p>
      <form id="league" class="panel-form" novalidate>
        <fieldset class="field"><legend>Category</legend>
          <div class="segmented">
            <input type="radio" name="category" id="lc-pre" value="Preseason"><label for="lc-pre">Preseason</label>
            <input type="radio" name="category" id="lc-season" value="Season" checked><label for="lc-season">Season</label>
            <input type="radio" name="category" id="lc-champ" value="Championship"><label for="lc-champ">Championship</label>
          </div>
        </fieldset>
        <div class="field"><label for="lm-name">Match name</label>
          <input id="lm-name" name="name" autocomplete="off" spellcheck="false" placeholder="season-week3" pattern="[A-Za-z0-9_-]{3,28}" required></div>
        <div class="form-row">
          <div class="field"><label for="lm-tick">Milliseconds per tick</label>
            <input id="lm-tick" name="tickIntervalMs" type="number" min="100" max="10000" step="50" value="1000" inputmode="numeric"></div>
          <div class="field"><label for="lm-ticks">Tick limit</label>
            <input id="lm-ticks" name="maxTicks" type="number" min="10" max="20000" value="1800" inputmode="numeric"></div>
        </div>
        <div><button class="btn" type="submit">Create match</button></div>
      </form>
      <h3 style="margin-top:20px">Current league matches</h3>
      <div id="league-matches"><p class="empty">Loading…</p></div>
    </section>`;
  }

  function runnersHtml() {
    return html`<section aria-labelledby="runners-h" class="admin">
      <h2 id="runners-h">League runners</h2>
      <p class="muted small">Pick someone to run league matches when you can't be there: they can create them, split them into
        heats, start each round and close them, but not take bots out or call matches off. It takes effect at once, and so does
        taking it away.</p>
      <div id="runners"><p class="empty">Loading…</p></div>
    </section>`;
  }

  async function loadRunners() {
    const el = root.querySelector('#runners');
    if (!el) return;
    let people;
    try {
      people = await api('/api/v1/admin/runners');
    } catch (e) {
      if (alive && el.isConnected) setHtml(el, html`<p class="error">Could not load the league runners: ${e.message}</p>`);
      return;
    }
    if (!alive || !el.isConnected) return;
    const runners = people.filter((p) => p.isLeagueRunner && !p.isAdmin);
    const candidates = people.filter((p) => !p.isLeagueRunner && !p.isAdmin);
    setHtml(el, html`
      ${runners.length ? html`<ul class="match-list">${runners.map((p) => html`
        <li class="league-row"><span>${p.avatarUrl ? html`<img class="avatar" src="${p.avatarUrl}" alt="" width="24" height="24"> ` : ''}<b>${p.displayName}</b>
          <span class="muted small">@${p.username}</span></span>
          <button type="button" class="link-btn danger" data-unrunner="${p.accountId}" data-name="${p.displayName}">Remove</button></li>`)}</ul>`
        : html`<p class="empty">No league runners. Only the admins can run league matches.</p>`}
      ${candidates.length ? html`<form id="add-runner" class="panel-form" novalidate>
        <div class="form-row">
          <div class="field"><label for="runner-pick">Person</label>
            <select id="runner-pick" name="accountId" required>${candidates.map((p) => html`
              <option value="${p.accountId}">${p.displayName} (@${p.username})</option>`)}</select></div>
        </div>
        <div><button class="btn" type="submit">Make league runner</button></div>
      </form>` : ''}`);
    el.querySelector('#add-runner')?.addEventListener('submit', (ev) => act(ev, async (form) => {
      const p = await api(`/api/v1/admin/runners/${form.get('accountId')}`, { method: 'PUT' });
      return `${p.displayName} can run league matches now.`;
    }));
    el.querySelectorAll('[data-unrunner]').forEach((b) => b.addEventListener('click', async () => {
      if (b.dataset.armed !== 'true') {
        b.dataset.armed = 'true';
        b.textContent = 'Stop them running matches?';
        return;
      }
      b.disabled = true;
      try {
        await api(`/api/v1/admin/runners/${b.dataset.unrunner}`, { method: 'DELETE' });
        message = { error: false, text: `${b.dataset.name} can't run league matches any more.` };
      } catch (e) {
        message = { error: true, text: e.message };
      }
      load();
    }));
  }

  function telemetryHtml(links) {
    const group = (name, heading) => html`<h3>${heading}</h3>
      <ul class="telemetry-links">${links.filter((l) => l.group === name).map((l) => html`
        <li><a href="${l.url}" target="_blank" rel="noopener">${l.label}</a> <span class="muted small">${l.description}</span></li>`)}
      </ul>`;
    return html`<section aria-labelledby="telemetry-h" class="admin">
      <h2 id="telemetry-h">Telemetry</h2>
      <p class="muted small">The server's logs, traces, errors and metrics in Application Insights (Azure portal; sign in with the
        account that owns the subscription). Queries open in Logs over the last 24 hours.</p>
      ${group('Pages', 'Application Insights')}
      ${group('Queries', 'Saved queries')}
    </section>`;
  }

  function wire() {
    const $ = (id) => root.querySelector('#' + id);
    $('sign-out').addEventListener('click', async () => {
      await fetch('/auth/signout', { method: 'POST' }).catch(() => {});
      shownKey = null;
      message = null;
      load();
    });

    $('claim').addEventListener('submit', (ev) => act(ev, async (form) => {
      const bot = await api('/api/v1/account/bots/claim', { method: 'POST', body: { apiKey: form.get('apiKey').trim() } });
      return `Claimed ${bot.name}.`;
    }));

    $('register').addEventListener('submit', (ev) => act(ev, async (form) => {
      const bot = await api('/api/v1/account/bots', { method: 'POST', body: { name: form.get('name').trim() } });
      shownKey = { name: bot.name, apiKey: bot.apiKey };
      return null;
    }));

    // Replacing a key or releasing a bot takes a second click to confirm.
    root.querySelectorAll('[data-key], [data-release]').forEach((b) => b.addEventListener('click', async () => {
      if (b.dataset.armed !== 'true') {
        root.querySelectorAll('[data-armed="true"]').forEach((x) => { x.dataset.armed = 'false'; x.textContent = x.dataset.key ? 'New key' : 'Release'; });
        b.dataset.armed = 'true';
        b.textContent = b.dataset.key ? 'Old key stops working. Sure?' : 'Lose the check. Sure?';
        return;
      }
      b.disabled = true;
      try {
        if (b.dataset.key) {
          const bot = await api(`/api/v1/account/bots/${b.dataset.key}/key`, { method: 'POST' });
          shownKey = { name: bot.name, apiKey: bot.apiKey };
          message = null;
        } else {
          await api(`/api/v1/account/bots/${b.dataset.release}`, { method: 'DELETE' });
          shownKey = null;
          message = { error: false, text: `Released ${b.dataset.name}.` };
        }
      } catch (e) {
        message = { error: true, text: e.message };
      }
      load();
    }));

    $('host').addEventListener('submit', (ev) => act(ev, async (form) => {
      const name = form.get('name').trim();
      await api('/api/v1/account/matches', {
        method: 'POST',
        body: {
          name,
          maxPlayers: Number(form.get('maxPlayers')) || undefined,
          settings: {
            tickIntervalMs: Number(form.get('tickIntervalMs')) || undefined,
            maxTicks: Number(form.get('maxTicks')) || undefined,
          },
        },
      });
      return `You're hosting ${name}. Bots join it by that name; start it under Your hosted matches when they're in.`;
    }));

    $('league')?.addEventListener('submit', (ev) => {
      ev.preventDefault();
      const form = new FormData(ev.target);
      act(ev, async () => {
        const match = await api('/api/v1/admin/matches', {
          method: 'POST',
          body: {
            category: form.get('category'),
            name: form.get('name').trim(),
            tickIntervalMs: Number(form.get('tickIntervalMs')) || undefined,
            maxTicks: Number(form.get('maxTicks')) || undefined,
          },
        });
        location.hash = `#/match/${match.matchId}`;
        return null;
      });
    });
  }

  /** Runs a form's action, then reloads the page with its message (or the error). */
  async function act(ev, fn) {
    ev.preventDefault?.();
    const button = ev.target.querySelector('button[type="submit"]');
    button.disabled = true;
    try {
      const text = await fn(new FormData(ev.target));
      message = text ? { error: false, text } : null;
      if (text) shownKey = null;
    } catch (e) {
      message = { error: true, text: e.message };
    }
    load();
  }

  async function loadHosted() {
    clearTimeout(hostTimer);
    const el = root.querySelector('#hosted');
    if (!el) return;
    let list;
    try {
      list = await api('/api/v1/account/matches');
    } catch (e) {
      if (alive && el.isConnected) setHtml(el, html`<p class="error">Could not load your hosted matches: ${e.message}</p>`);
      return;
    }
    if (!alive || !el.isConnected) return;
    setHtml(el, list.length ? html`<ul class="match-list hosted">${list.map(hostedRow)}</ul>`
      : html`<p class="empty">None right now. Matches you host show up here until a while after they finish.</p>`);
    el.querySelectorAll('[data-start], [data-calloff]').forEach((b) => b.addEventListener('click', () => hostAction(b)));
    if (list.some((m) => m.status !== 'Completed')) hostTimer = setTimeout(loadHosted, 3000);
  }

  function hostedRow(m) {
    const real = m.players.filter((p) => !p.isHouseBot).length;
    const ready = m.players.length >= 2 && real >= 1;
    const status = m.status === 'Waiting' ? `waiting · ${m.players.length} of ${m.maxPlayers} seats taken`
      : m.status === 'Active' ? `under way · tick ${fmt(m.tick)}` : 'finished';
    return html`<li>
      <div class="league-row">
        <a href="#/match/${m.status === 'Completed' ? m.matchId : m.name}"><b>${m.name}</b></a>
        <span class="muted small">${status}</span>
      </div>
      <p class="hosted-players small">${m.players.length
        ? m.players.map((p, i) => html`${i ? ', ' : ''}${playerLabel(p)}`)
        : html`<span class="muted">No ${say('bots', 'badgers')} yet. They join with the name <code>${m.name}</code>.</span>`}</p>
      ${m.status === 'Waiting' ? html`<div class="league-actions">
        <button type="button" class="btn start" data-start="${m.matchId}" ${ready ? '' : 'disabled'}>Start with ${m.players.length}</button>
        <button type="button" class="link-btn danger" data-calloff="${m.matchId}">Call off</button>
        ${ready ? '' : html`<span class="muted small">Needs two players, one of them a real bot.</span>`}
      </div>` : ''}
    </li>`;
  }

  // Calling a match off takes a second click to confirm.
  async function hostAction(b) {
    if (b.dataset.calloff && b.dataset.armed !== 'true') {
      b.dataset.armed = 'true';
      b.textContent = 'Call it off for everyone. Sure?';
      return;
    }
    b.disabled = true;
    try {
      if (b.dataset.start) await api(`/api/v1/account/matches/${b.dataset.start}/start`, { method: 'POST' });
      else await api(`/api/v1/account/matches/${b.dataset.calloff}`, { method: 'DELETE' });
    } catch (e) {
      message = { error: true, text: e.message };
      render();
      return;
    }
    loadHosted();
  }

  async function loadLeagueMatches() {
    const el = root.querySelector('#league-matches');
    try {
      const list = (await api('/api/v1/matches')).filter((m) => m.heats);
      if (!alive || !el.isConnected) return;
      setHtml(el, list.length ? html`<ul class="match-list">${list.map((l) => html`
        <li class="league-row"><a href="#/match/${l.matchId}"><span class="tag cat ${l.category}">${l.category}</span><b>${l.name}</b></a>
          <span class="muted small">${l.status === 'Waiting' ? (l.heats.length ? `${l.heats.length} heats, not started` : 'taking players') : l.status.toLowerCase()} · ${l.players.length} bots</span></li>`)}</ul>`
        : html`<p class="empty">No league matches right now.</p>`);
    } catch (e) {
      if (alive) setHtml(el, html`<p class="error">Could not load league matches: ${e.message}</p>`);
    }
  }

  load();
  return () => { alive = false; clearTimeout(hostTimer); };
}

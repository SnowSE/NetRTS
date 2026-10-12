// Account page: Discord sign-in, the bots you own, and (for Discord server admins) league matches.
import { api, html, setHtml, playerLabel, verifiedBadge, fmt } from './util.js';
import { GAME, say } from './lore.js';

const CATEGORIES = ['Practice', 'Preseason', 'Season', 'Championship'];
const SIGN_IN = '/auth/discord?returnUrl=' + encodeURIComponent('/#/account');

/** Mounts the account page; onAccount(info) lets the top bar follow sign-ins and sign-outs. */
export function mountAccount(root, onAccount) {
  document.title = `Account · ${GAME}`;
  let alive = true;
  let info = null;
  let shownKey = null;   // { name, apiKey } after registering a bot or replacing a key: shown once
  let message = null;    // { error: bool, text }
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
          <p class="muted" style="margin:4px 0 0">@${a.username}${a.isAdmin ? html`<span class="tag">admin</span>` : ''}</p>
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
              <tr><td>${playerLabel(b, { owner: false })}</td>
                ${CATEGORIES.map((c) => {
                  const r = b.records.find((x) => x.category === c) || { rating: 1200, wins: 0, losses: 0, draws: 0 };
                  return html`<td><span class="rating">${fmt(r.rating)}</span><br><span class="muted small">${r.wins}-${r.losses}-${r.draws}</span></td>`;
                })}
                <td class="actions">
                  <button type="button" class="btn ghost small-btn" data-key="${b.playerId}" data-name="${b.name}">New key</button>
                  <button type="button" class="btn ghost small-btn" data-release="${b.playerId}" data-name="${b.name}">Release</button>
                </td></tr>`)}
            </tbody></table></div>`
          : html`<p class="empty">No bots yet. Register one, or claim one you made before.</p>`}
      </section>

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

      ${a.isAdmin ? adminHtml() : ''}
      ${a.isAdmin && info.telemetryLinks?.length ? telemetryHtml(info.telemetryLinks) : ''}
    </div>`);

    wire();
    if (a.isAdmin) loadLeagueMatches();
  }

  function adminHtml() {
    return html`<section aria-labelledby="admin-h" class="admin">
      <h2 id="admin-h">League matches</h2>
      <p class="muted small">You administer our Discord server, so you can run preseason, season and championship matches.
        Create one, let verified bots join it, then on its page split everyone into heats and start them all at once.</p>
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
  return () => { alive = false; };
}

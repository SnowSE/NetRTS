// People: the Trophy Room (#/trophy-room, everyone ranked by what their bots have earned) and public
// profiles (#/player/<id>: their bots, achievements and brawls).
import { api, html, setHtml, botAndOwner, displayName, timeAgo, fmt, slotVar } from './util.js';
import { GAME, say } from './lore.js';
import { earnedHtml, achievementTabs } from './achievements.js';

const TIERS = ['Platinum', 'Gold', 'Silver', 'Bronze'];
const avatar = (url, size) => (url ? html`<img class="avatar${size > 40 ? ' big' : ''}" src="${url}" alt="" width="${size}" height="${size}">` : '');

/** #/trophy-room: verified people ranked by trophy points (1 Bronze, 2 Silver, 4 Gold, 8 Platinum). */
export function mountTrophyRoom(root) {
  document.title = `Trophy Room · ${GAME}`;
  let alive = true;
  setHtml(root, html`<div class="achievements">
    <h1 class="headline">Trophy Room</h1>
    ${achievementTabs('trophy-room')}
    <p class="lede">Everyone with a verified ${say('bot', 'badger')}, ranked by what their ${say('bots', 'badgers')} have earned between them.
      Each achievement counts once per person: 1 point for Bronze, 2 for Silver, 4 for Gold, 8 for Platinum.</p>
    <div id="trophies" aria-busy="true"><p class="empty">Loading…</p></div>
  </div>`);

  api('/api/v1/people').then((list) => {
    if (!alive) return;
    const el = root.querySelector('#trophies');
    el.setAttribute('aria-busy', 'false');
    setHtml(el, list.length ? html`<div class="table-scroll"><table class="ladder trophy-table">
      <caption class="visually-hidden">People ranked by trophy points</caption>
      <thead><tr><th scope="col">#</th><th scope="col">Player</th><th scope="col">Points</th>
        ${TIERS.map((t) => html`<th scope="col"><span class="tier-dot tier-${t}" aria-hidden="true"></span>${t}</th>`)}
        <th scope="col">${say('Bots', 'Badgers')}</th><th scope="col">Latest</th></tr></thead>
      <tbody>${list.map((p) => html`<tr>
        <td>${p.rank}</td>
        <td><a class="person" href="#/player/${p.accountId}">${avatar(p.avatarUrl, 24)}<b>${p.displayName}</b></a></td>
        <td class="rating">${fmt(p.points)}</td>
        ${TIERS.map((t) => html`<td>${p[t.toLowerCase()] || ''}</td>`)}
        <td>${p.bots}</td>
        <td class="muted small">${p.latestAt ? timeAgo(p.latestAt) : '–'}</td>
      </tr>`)}</tbody></table></div>`
      : html`<p class="empty">Nobody yet. Claim your ${say('bot', 'badger')} on your account page to get on the board.</p>`);
  }).catch((e) => {
    if (alive) setHtml(root.querySelector('#trophies'), html`<p class="error">Could not load the Trophy Room: ${e.message}</p>`);
  });
  return () => { alive = false; };
}

/** #/player/<id>: a person's bots, everything they've earned, and the brawls they played. */
export function mountPerson(root, id) {
  let alive = true;
  setHtml(root, html`<p class="empty">Loading…</p>`);
  api(`/api/v1/people/${id}`).then((p) => {
    if (!alive) return;
    document.title = `${p.displayName} · ${GAME}`;
    setHtml(root, html`<div class="account person-page">
      <div class="account-head">
        ${avatar(p.avatarUrl, 56)}
        <div>
          <h1 class="headline">${p.displayName}</h1>
          <p class="muted" style="margin:4px 0 0">${fmt(p.points)} trophy points · ${p.achievements.length} achievement${p.achievements.length === 1 ? '' : 's'}
            · playing since ${new Date(p.memberSince).toLocaleDateString()} · <a href="#/trophy-room">Trophy Room</a></p>
        </div>
      </div>

      <section aria-labelledby="bots-h">
        <h2 id="bots-h">${say('Bots', 'Badgers')}</h2>
        ${p.bots.length ? html`<div class="table-scroll"><table class="ladder bots">
          <thead><tr><th scope="col">${say('Bot', 'Badger')}</th>${p.bots[0].records.map((r) => html`<th scope="col">${r.category}</th>`)}</tr></thead>
          <tbody>${p.bots.map((b) => html`<tr><td>${botAndOwner(b, { owner: false })}</td>
            ${b.records.map((r) => html`<td><span class="rating">${fmt(r.rating)}</span><br><span class="muted small">${r.wins}-${r.losses}-${r.draws}</span></td>`)}</tr>`)}
          </tbody></table></div>` : html`<p class="empty">No ${say('bots', 'badgers')}.</p>`}
      </section>

      <section aria-labelledby="ach-h">
        <h2 id="ach-h">Achievements</h2>
        ${earnedHtml(p.achievements, { bots: true })}
      </section>

      <section aria-labelledby="brawls-h">
        <h2 id="brawls-h">${say('Recent matches', 'Recent brawls')}</h2>
        ${p.brawls.length ? html`<ul class="match-list">${p.brawls.map((m) => brawlRow(m))}</ul>`
          : html`<p class="empty">No ${say('matches', 'brawls')} yet.</p>`}
      </section>
    </div>`);
  }).catch((e) => {
    if (alive) setHtml(root, html`<p class="error">${e.status === 404 ? 'No such player.' : `Could not load this player: ${e.message}`}</p>`);
  });
  return () => { alive = false; };
}

const RESULT = { Win: 'Won', Loss: 'Lost', Draw: 'Drew' };

function brawlRow(m) {
  const others = m.opponents.map(displayName);
  const extra = m.playerCount - 1 - others.length;
  const against = [...others, ...(extra > 0 ? [`${extra} more`] : [])];
  return html`<li><a class="match-row no-status" href="#/match/${m.matchId}">
    <span class="who">${m.category !== 'Practice' ? html`<span class="tag cat ${m.category}">${m.category}</span>` : ''}
      <span class="pname" style="--pc:${slotVar(0)}">${m.bot}</span> <span class="muted">vs</span> ${against.join(', ')}</span>
    <span class="meta"><strong class="result-${m.result}">${RESULT[m.result] ?? m.result}</strong>${m.playerCount > 2 ? ` · ${ordinal(m.place)} of ${m.playerCount}` : ''}
      · ${fmt(m.score)} pts<br>${timeAgo(m.completedAt)}</span></a></li>`;
}

const ordinal = (n) => `${n}${['th', 'st', 'nd', 'rd'][(n % 100 > 10 && n % 100 < 14) || n % 10 > 3 ? 0 : n % 10]}`;

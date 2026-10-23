// Achievements: the catalog (#/achievements), one bot's page (#/bot/<id>), and the badge list the
// account and profile pages use for everything a person's bots have earned.
import { api, html, setHtml, botAndOwner, timeAgo, fmt, SKILL_TITLE } from './util.js';
import { GAME, say } from './lore.js';

const GROUPS = ['First steps', 'Winning', 'Economy', 'Combat', 'Speed and precision', 'Showing up', 'Ladder and league', 'Just for fun'];
const TIERS = ['Bronze', 'Silver', 'Gold', 'Platinum'];

/** One achievement as a badge: tier stripe, name, what earns it, a footnote, and who has it. */
function badge(a, note, who = '') {
  return html`<li class="ach tier-${a.tier}${a.locked ? ' locked' : ''}">
    <span class="ach-tier">${a.tier}</span>
    <span class="ach-name">${a.name}</span>
    <span class="ach-desc">${a.description}</span>
    ${note ? html`<span class="ach-note muted small">${note}</span>` : ''}
    ${who}
  </li>`;
}

/** The two views of achievements: by achievement, and by person. */
export function achievementTabs(active) {
  return html`<nav class="tabs" aria-label="Achievements">
    <a href="#/achievements" ${active === 'all' ? html`aria-current="page"` : ''}>All achievements</a>
    <a href="#/trophy-room" ${active === 'trophy-room' ? html`aria-current="page"` : ''}>Trophy Room</a>
  </nav>`;
}

/** Who has an achievement: the first few bots (and owners) up front, the rest a click away. */
function earnersHtml(a) {
  if (!a.earners?.length) return '';
  const person = (e) => botAndOwner({ playerId: e.playerId, name: e.bot, ownerName: e.ownerName, ownerSlug: e.ownerSlug, verified: Boolean(e.ownerName) });
  const shown = a.earners.slice(0, 4);
  const rest = a.earners.slice(4);
  const unlisted = a.earnedBy - a.earners.length;
  return html`<ol class="earners">
    ${shown.map((e, i) => html`<li${i === 0 ? html` class="first"` : ''}>${i === 0 ? html`<span class="first-tag">First</span>` : ''}${person(e)}</li>`)}
    ${rest.length || unlisted > 0 ? html`<li class="more"><details><summary>${rest.length + Math.max(0, unlisted)} more</summary>
      <ol class="earners">${rest.map((e) => html`<li>${person(e)}</li>`)}${unlisted > 0 ? html`<li class="muted small">…and ${unlisted} others</li>` : ''}</ol>
    </details></li>` : ''}
  </ol>`;
}

/** Earned achievements (newest first), e.g. for the account page; with bots, says which earned each. */
export function earnedHtml(list, { bots = false } = {}) {
  if (!list.length) return html`<p class="empty">No achievements yet. Play a match to earn your first.</p>`;
  return html`<ul class="achs">${list.map((a) => badge(a,
    html`${bots && a.bots?.length ? html`${a.bots.join(', ')} · ` : ''}${timeAgo(a.earnedAt)}${a.matchId ? html` · <a href="#/match/${a.matchId}">match</a>` : ''}`))}</ul>`;
}

/** #/achievements: every achievement by group, with how many bots have it. */
export function mountAchievements(root) {
  document.title = `Achievements · ${GAME}`;
  let alive = true;
  setHtml(root, html`<div class="achievements">
    <h1 class="headline">Achievements</h1>
    ${achievementTabs('all')}
    <p class="lede">${say('Bots', 'Badgers')} earn these by playing. Only the first steps count against house bots; everything else
      needs a real opponent (someone else's ${say('bot', 'badger')}). Some are secret until someone finds them.
      The <a href="#/trophy-room">Trophy Room</a> ranks everyone by what they've earned.</p>
    <div id="catalog" aria-busy="true"><p class="empty">Loading…</p></div>
  </div>`);

  api('/api/v1/achievements').then((list) => {
    if (!alive) return;
    const el = root.querySelector('#catalog');
    el.setAttribute('aria-busy', 'false');
    setHtml(el, html`${GROUPS.map((g) => {
      const items = list.filter((a) => a.group === g);
      return items.length ? html`<section class="ach-group"><h2>${g}</h2><ul class="achs">${items.map((a) => badge(
        { ...a, locked: a.earnedBy === 0 },
        a.earnedBy
          ? html`${fmt(a.earnedBy)} ${say('bot', 'badger')}${a.earnedBy === 1 ? ' has' : 's have'} it${a.houseBotsCount ? ' · house bots count' : ''}`
          : html`Nobody yet${a.houseBotsCount ? ' · house bots count' : ''}`,
        earnersHtml(a)))}</ul></section>` : '';
    })}`);
  }).catch((e) => {
    if (alive) setHtml(root.querySelector('#catalog'), html`<p class="error">Could not load achievements: ${e.message}</p>`);
  });
  return () => { alive = false; };
}

/** #/bot/<id>: a bot's record in each category and its achievements. */
export function mountBot(root, id) {
  let alive = true;
  setHtml(root, html`<p class="empty">Loading…</p>`);
  Promise.all([api(`/api/v1/players/${id}`), api(`/api/v1/players/${id}/achievements`), api('/api/v1/achievements')])
    .then(([bot, earned, catalog]) => {
      if (!alive) return;
      document.title = `${bot.name} · ${GAME}`;
      const counts = TIERS.map((t) => [t, earned.filter((a) => a.tier === t).length]);
      setHtml(root, html`<div class="account bot-page">
        <div class="account-head"><div>
          <h1 class="headline">${botAndOwner(bot)}</h1>
          <p class="muted" style="margin:4px 0 0">${bot.isHouseBot ? say('House bot', 'House badger') : `Registered ${new Date(bot.createdAt).toLocaleDateString()}`}</p>
        </div></div>
        <div class="table-scroll"><table class="ladder bots">
          <thead><tr><th scope="col">Category</th><th scope="col">Points</th><th scope="col"><abbr title="${SKILL_TITLE}">Skill</abbr></th><th scope="col">Rating</th>
            <th scope="col"><abbr title="Wins">W</abbr></th><th scope="col"><abbr title="Losses">L</abbr></th><th scope="col"><abbr title="Draws">D</abbr></th></tr></thead>
          <tbody>${bot.records.map((r) => html`<tr><td style="text-align:left">${r.category}</td><td class="rating">${fmt(r.points)}</td>
            <td>${fmt(r.skill)}</td><td>${fmt(r.rating)}</td><td>${r.wins}</td><td>${r.losses}</td><td>${r.draws}</td></tr>`)}</tbody>
        </table></div>
        <section aria-labelledby="ach-h">
          <div class="section-head"><h2 id="ach-h">Achievements</h2>
            <span class="muted small">${earned.length} of ${catalog.length} · ${counts.filter(([, n]) => n).map(([t, n]) => `${n} ${t}`).join(' · ')}</span></div>
          ${earnedHtml(earned)}
        </section>
      </div>`);
    })
    .catch((e) => {
      if (alive) setHtml(root, html`<p class="error">${e.status === 404 ? `No such ${say('bot', 'badger')}.` : `Could not load this ${say('bot', 'badger')}: ${e.message}`}</p>`);
    });
  return () => { alive = false; };
}

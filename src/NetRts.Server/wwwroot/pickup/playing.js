// Now playing (phase "playing"): the step your bot is on (big, with the one before and after dimmed),
// your grubs, workers, fighters and buildings as the Block Bot HUD's trend tiles (from this phone's own
// polls of `me`), every player in the match, and Attack now! / Pull back.
import { api, esc, mountScope, slotVar } from '../util.js';
import { icon } from './cards.js';
import { stepText, stepItem, foreverText } from './items.js';

/** The pick-up treaty, in seconds (MatchManager.PickupTreatyTicks at PickupTickMs). */
export const TREATY_S = 30;
const KEEP_S = 60;   // the trend tiles show the last minute
const TILES = [['Grubs', 'grubs', 'var(--hud-accent, #f2c14e)'], ['Workers', 'workers', 'var(--hud-workers, #7cc7f0)'],
  ['Fighters', 'fighters', 'var(--hud-fighters, #ff8a7a)'], ['Buildings', 'buildings', 'var(--hud-buildings, #c7b2f0)']];
const COLS = [...TILES, ['Score', 'score', '#ffffff']];
const fmtN = (n) => (typeof n === 'number' ? n.toLocaleString('en-US') : '–');

/** A Block Bot cockpit trend tile: the value, its change per minute, and the last minute as a filled line. */
function trendTile(label, hist, now, tone) {
  const a = hist.filter((p) => p.t >= hist[hist.length - 1]?.t - KEEP_S * 1000);
  const W = 112, H = 22;
  let line = '', area = '';
  if (a.length >= 2) {
    const vs = a.map((p) => p.v), lo = Math.min(...vs), range = Math.max(1, Math.max(...vs) - lo);
    line = vs.map((v, i) => `${((i / (vs.length - 1)) * W).toFixed(1)},${(H - 2 - ((v - lo) / range) * (H - 4)).toFixed(1)}`).join(' ');
    area = `0,${H} ${line} ${W},${H}`;
  }
  const secs = a.length >= 5 ? (a[a.length - 1].t - a[0].t) / 1000 : 0;
  const perMin = secs >= 4 ? Math.round(((a[a.length - 1].v - a[0].v) * 60) / secs) : null;
  const rate = perMin == null ? '' : perMin === 0 ? 'steady' : `${perMin > 0 ? '▲ +' : '▼ −'}${fmtN(Math.abs(perMin))}/min`;
  const rc = perMin > 0 ? ' up' : perMin < 0 ? ' down' : '';
  return `<div class="t" style="--tile:${tone}"><small>${label}</small><span class="row"><b>${fmtN(now)}</b><span class="rate${rc}">${rate}</span></span>
    <svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" aria-hidden="true">${line ? `<polyline points="${area}" fill="currentColor" fill-opacity="0.14" stroke="none"/><polyline points="${line}" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke"/>` : ''}</svg></div>`;
}

/**
 * Every player: their numbers, the leader of each column lit, a crown on the champion, you highlighted.
 * only: the columns to show (keys), when not all of them.
 */
export function playersTable(players, only = null) {
  const cols = only ? COLS.filter(([, k]) => only.includes(k)) : COLS;
  const best = Object.fromEntries(cols.map(([, k]) => [k, Math.max(...players.map((x) => x[k] ?? 0))]));
  return `<table class="opp"><thead><tr><th scope="col"><span class="vh">Player</span></th>${cols.map(([l, , c]) => `<th scope="col" style="color:${c}">${l}</th>`).join('')}</tr></thead>
    <tbody>${players.map((x) => `<tr class="${x.you ? 'me' : ''}"><th scope="row" ${x.slot != null ? `class="pc" style="--pc:${slotVar(x.slot)}"` : ''}>${x.champion ? '<span class="crown" title="Champion">♛</span> ' : ''}${x.slot != null ? '<span class="dot" aria-hidden="true"></span>' : ''}${x.you ? 'You' : esc(x.name)}${x.house ? ' <span class="house">house</span>' : ''}</th>${cols.map(([, k, c]) => `<td class="${players.length > 1 && x[k] === best[k] && best[k] > 0 ? 'top' : ''}" style="--c:${c}">${fmtN(x[k])}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
}

/** "Alice (champion) and Bob": the others in the match. */
export const othersText = (players) => {
  const names = players.filter((p) => !p.you).map((p) => `${p.name}${p.champion ? ' (champion)' : ''}`);
  return names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
};

/**
 * The overlay for one match. el: the overlay element; plan: the plan that's playing; token: this phone's.
 * update(me) with each poll; dispose() when the phase moves on.
 */
export function mountPlaying(el, { plan, token, matchId }) {
  const scope = mountScope();
  const hist = Object.fromEntries(TILES.map(([, k]) => [k, []]));
  let startedAt = Date.now();   // the match's tick 0, from its summary when that loads
  let flashT = 0;
  el.className = 'ov playing';
  el.hidden = false;
  el.innerHTML = `<div class="np-h"><small>On the big screen now</small><div class="np-me" id="np-me"></div><b id="np-who">You</b></div>
    <div class="np-steps" id="np-steps"></div>
    <div class="hudp hud-dark"><div class="hud" id="np-hud"></div><div class="score" id="np-score"></div></div>
    <p class="np-flash" id="np-flash" role="status"></p>
    <div class="acts"><button type="button" class="atk" data-order="attack">Attack now!</button><button type="button" class="pull" data-order="pullback">Pull back</button></div>
    <button type="button" class="edit-live" data-a="edit-live">Change my plan <small>while it plays</small></button>`;
  const $ = (id) => el.querySelector(`#${id}`);

  if (matchId) {
    scope.wait(api(`/api/v1/matches/${matchId}`)).then((m) => {
      if (m?.tick != null && m.tickIntervalMs) startedAt = Date.now() - m.tick * m.tickIntervalMs;
    }).catch(() => {});
  }

  function flash(text) {
    $('np-flash').textContent = text;
    scope.clear(flashT);
    flashT = scope.later(() => { $('np-flash').textContent = ''; }, 2500);
  }

  scope.on(el, 'click', async (e) => {
    const b = e.target.closest('button[data-order]');
    if (!b) return;
    const order = b.dataset.order;
    const treaty = Math.ceil(TREATY_S - (Date.now() - startedAt) / 1000);
    flash(order === 'pullback' ? 'Pulling back to base' : treaty > 0 ? `Treaty: attacks can start in ${treaty} seconds` : 'Attacking now!');
    try {
      await scope.wait(api('/api/v1/pickup/order', { method: 'POST', body: { token, order } }));
    } catch (ex) { flash(ex.message); }
  });

  function stepsHTML(ni) {
    const n = plan.steps.length;
    const label = (i) => (i < 0 ? 'Starting army is out' : i < n ? stepText(plan.steps[i]) : i === n ? `Forever: ${foreverText(plan)}` : '');
    const tag = (i) => (i < 0 ? 'Start' : i < n ? `Step ${i + 1} of ${n}` : 'After the steps');
    const ic = (i) => (i >= 0 && i < n ? icon(stepItem(plan.steps[i]), 28) : i === n && plan.forever[0] ? icon(plan.forever[0], 28) : '');
    const row = (i, cls) => (label(i) ? `<div class="ps ${cls}">${ic(i)}<span><small>${tag(i)}${cls === 'cur' ? ' · now' : ''}</small><b>${esc(label(i))}</b></span></div>` : '');
    return row(ni - 1, 'prev') + row(ni, 'cur') + row(ni + 1, 'next');
  }

  function update(me) {
    const players = me.players || [];
    const you = players.find((p) => p.you) ?? { grubs: 0, workers: 0, fighters: 0, buildings: 0 };
    const t = Date.now();
    for (const [, k] of TILES) {
      const h = hist[k];
      h.push({ t, v: you[k] ?? 0 });
      while (h.length && h[0].t < t - KEEP_S * 1000) h.shift();
    }
    // Your name in your color: the color of your badgers and your name tag on the big screen.
    $('np-me').innerHTML = you.slot != null
      ? `<span class="np-badge" style="--pc:${slotVar(you.slot)}">${esc(me.name)}</span><span class="np-look">Look for your color on the big screen</span>`
      : `<span class="np-badge">${esc(me.name)}</span>`;
    $('np-who').textContent = players.length > 1 ? `vs ${othersText(players)}` : '';
    $('np-steps').innerHTML = stepsHTML(Math.min(me.step ?? 0, plan.steps.length));
    $('np-hud').innerHTML = TILES.map(([l, k, c]) => trendTile(l, hist[k], you[k], c)).join('');
    $('np-score').innerHTML = players.length ? playersTable(players) : '';
  }

  /** The plan changed during the match (Change my plan): the steps shown follow the new one. */
  const setPlan = (p) => { plan = p; };

  return { update, setPlan, dispose: () => { scope.dispose(); el.hidden = true; el.innerHTML = ''; } };
}

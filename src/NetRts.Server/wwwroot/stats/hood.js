// "Under the hood": how the server held up, from GET /api/v1/stats/server (its own telemetry, read each
// morning through the end of yesterday): the traffic clock, the tick budget, status codes and uptime.
import { esc } from '../util.js';
import { when, num, labelDay, dayRange } from './data.js';

const BINS = [100, 500, 1000, 2000, 3000, 4500, Infinity];
const BIN_LABEL = ['<100', '100', '500', '1k', '2k', '3k', '4.5k+'];
const CODE_NAME = { 200: 'OK', 201: 'Created', 204: 'No content', 302: 'Redirect', 304: 'Not modified', 400: 'Bad request', 401: 'Not signed in', 404: 'Not found', 409: 'Conflict (e.g. match already full)', 429: 'Slow down (rate limit)', 500: 'Server error', 503: 'Unavailable' };
const SPEEDS = [[100, 'Standard · 100 ms'], [250, 'Pick-up · 250 ms'], [1000, 'Slow · 1 s']];
const day = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' });

export function hoodHtml(server) {
  if (!server) {
    return `<section class="st-block"><p class="st-eyebrow">Under the hood</p><h2>How the server held up</h2>
      <p class="st-note">The server's own numbers (requests, tick times, uptime) come from its telemetry, which isn't connected here.</p></section>`;
  }
  const through = day.format(new Date(+new Date(server.to) - 1));
  return `
  <section class="st-block" aria-labelledby="st-clock-h">
    <p class="st-eyebrow">The bots never sleep</p>
    <h2 id="st-clock-h">Every hour of every day</h2>
    <p class="st-lede" id="st-clock-lede"></p>
    <div class="st-panel" style="display:grid;gap:10px">
      <div class="st-chart"><svg id="st-heat" class="st-heat" viewBox="0 0 980 330" role="img" aria-label="Requests by day and hour"></svg></div>
      <div class="st-ramp">${'requests per hour '}${BIN_LABEL.map((lab, k) => `<span><i style="background:var(--q${k + 1})"></i> ${lab}</span>`).join(' ')}</div>
    </div>
    <div class="st-src">Source: server telemetry, traced requests, Mountain time · through ${esc(through)}</div>
  </section>
  <section class="st-block" aria-labelledby="st-hood-h">
    <p class="st-eyebrow">Under the hood</p>
    <h2 id="st-hood-h">How the server held up</h2>
    <div class="st-cards">
      <div class="st-panel st-card">
        <h3>The engine barely breaks a sweat</h3>
        <p>An average tick takes ${server.tickAverageMs.toFixed(2)} ms across ${num(server.ticks)} ticks. Pick a game speed to see how much of each tick's time that uses.</p>
        <div class="st-seg" role="group" aria-label="Game speed" id="st-speeds">${SPEEDS.map(([ms, lab], k) => `<button type="button" data-ms="${ms}" aria-pressed="${k === 0}">${lab}</button>`).join('')}</div>
        <div class="st-budget" aria-hidden="true"><div class="used" id="st-used"></div></div>
        <div class="st-scale"><span>0</span><span id="st-budget-max">100 ms</span></div>
        <p id="st-budget-text"></p>
        <p>${server.tickOverruns ? `Only ${num(server.tickOverruns)} ticks ran over time, about 1 in ${num(Math.round(server.ticks / server.tickOverruns))}.` : 'Not one tick ran over time.'} The most matches running at once was ${num(server.mostLiveMatches)}.</p>
      </div>
      <div class="st-panel st-card">
        <h3 id="st-codes-h"></h3>
        <table class="st-codes" aria-label="Responses by status code"><tbody id="st-codes"></tbody></table>
        <p id="st-peak"></p>
      </div>
      <div class="st-panel st-card">
        <h3 id="st-health-h"></h3>
        <p id="st-health-text"></p>
        <div class="st-health" id="st-health"></div>
        <div class="st-mini">
          <div><b>${num(server.exceptions)}</b><span>exceptions${server.databaseExceptions ? ` (${num(server.databaseExceptions)} were database connection errors)` : ''}</span></div>
          <div><b>${num(server.restarts)}</b><span>restarts, almost all of them deploys</span></div>
          <div><b>${num(server.databaseQueries)}</b><span>database queries, ${server.databaseAverageMs.toFixed(1)} ms on average</span></div>
          <div><b>${Math.round(server.memoryAverageMb)} MB</b><span>average memory (peak ${Math.round(server.memoryPeakMb)} MB)</span></div>
        </div>
      </div>
    </div>
    <div class="st-src">Source: server telemetry (Application Insights), ${esc(day.format(new Date(server.from)))} through ${esc(through)}, refreshed each morning</div>
  </section>`;
}

export function mountHood(root, server, tip, scope) {
  if (!server) return;
  const $ = (id) => root.querySelector(`#${id}`);

  // The traffic clock: one column per day, one row per hour.
  const hours = new Map(server.hours.map((h) => [`${h.day}|${h.hour}`, h.requests]));
  const seen = server.hours.map((h) => h.day).sort();
  const days = seen.length ? dayRange(seen[0], seen.at(-1)) : [];
  const byHour = new Array(24).fill(0);
  for (const h of server.hours) byHour[h.hour] += h.requests;
  const lo = byHour.indexOf(Math.min(...byHour)), hi = byHour.indexOf(Math.max(...byHour));
  const ampm = (h) => `${h % 12 || 12} ${h < 12 ? 'a.m.' : 'p.m.'}`;
  $('st-clock-lede').textContent = `Requests per hour, one column per day. Bots keep polling all night: the quietest hour (${ampm(lo)}) still brings in ${Math.round((100 * byHour[lo]) / Math.max(1, byHour[hi]))}% as much as the busiest (${ampm(hi)}). The strongest color marks the busiest hours, when classes and tournaments run.`;
  const W = 980, H = 330, l = 44, t = 8, b = 26;
  const cw = (W - l - 6) / Math.max(1, days.length), ch = (H - t - b) / 24;
  let s = '';
  for (let h = 0; h < 24; h += 3) s += `<text x="${l - 8}" y="${t + ch * h + ch / 2 + 4}" text-anchor="end">${['12a', '3a', '6a', '9a', '12p', '3p', '6p', '9p'][h / 3]}</text>`;
  const every = Math.max(1, Math.round(days.length / 7));
  days.forEach((d, i) => {
    for (let h = 0; h < 24; h++) {
      const n = hours.get(`${d}|${h}`) || 0;
      s += `<rect x="${(l + i * cw + 1).toFixed(1)}" y="${(t + h * ch + 1).toFixed(1)}" width="${Math.max(1, cw - 2).toFixed(1)}" height="${(ch - 2).toFixed(1)}" rx="1.5" fill="var(--q${BINS.findIndex((x) => n < x) + 1})" data-d="${d}" data-h="${h}" data-n="${n}"/>`;
    }
    if (i % every === 0) s += `<text x="${l + i * cw + cw / 2}" y="${H - 8}" text-anchor="middle">${labelDay(d)}</text>`;
  });
  $('st-heat').innerHTML = s;
  scope.on($('st-heat'), 'pointermove', (e) => {
    const c = e.target.dataset; if (!c?.d) { tip.hide(); return; }
    tip.show(`<b>${labelDay(c.d)}, ${ampm(+c.h)}</b><br>${num(+c.n)} requests`, e.clientX, e.clientY);
  });
  scope.on($('st-heat'), 'pointerleave', () => tip.hide());

  // The tick budget at each game speed.
  function budget(ms) {
    const pct = (server.tickAverageMs / ms) * 100;
    $('st-used').style.width = `${Math.max(pct, 0.4)}%`;
    $('st-budget-max').textContent = ms >= 1000 ? `${ms / 1000} s` : `${ms} ms`;
    $('st-budget-text').innerHTML = `<b>${pct < 1 ? pct.toFixed(2) : pct.toFixed(1)}%</b> of each tick is spent simulating; the rest is spent waiting for the bots' next moves.`;
  }
  scope.on($('st-speeds'), 'click', (e) => {
    const btn = e.target.closest('button[data-ms]'); if (!btn) return;
    $('st-speeds').querySelectorAll('button').forEach((x) => x.setAttribute('aria-pressed', String(x === btn)));
    budget(+btn.dataset.ms);
  });
  budget(100);

  // Status codes: the common ones, everything else together, and the server errors last.
  const codes = new Map(server.statusCodes.map((c) => [c.name, c.count]));
  // A 500 is the server failing; a rare 503 is a restart in progress, and goes with the rest.
  const errors = codes.get('500') || 0;
  const shown = [...codes].filter(([c]) => c !== '500').sort((a, b2) => b2[1] - a[1]);
  const head = shown.slice(0, 6), rest = shown.slice(6).reduce((n, [, v]) => n + v, 0);
  $('st-codes-h').textContent = `${num(server.requests)} answers, ${errors ? `${num(errors)} server error${errors === 1 ? '' : 's'}` : 'zero crashes'}`;
  $('st-codes').innerHTML = head.map(([c, v]) => `<tr><td>${esc(c)} ${esc(CODE_NAME[c] || '')}</td><td>${num(v)}</td></tr>`).join('')
    + (rest ? `<tr><td>Everything else</td><td>${num(rest)}</td></tr>` : '')
    + `<tr class="key"><td>500 Server errors</td><td>${num(errors)}</td></tr>`;
  $('st-peak').textContent = server.peakMinute
    ? `The busiest minute was ${when.format(new Date(server.peakMinute))}: ${num(server.peakMinuteRequests)} requests, about ${num(Math.round(server.peakMinuteRequests / 60))} a second. Half of all traced requests were answered in ${server.medianMs.toFixed(2)} ms. Bots sent ${num(server.commands)} moves and read the board ${num(server.statePolls)} times.`
    : '';

  // Uptime checks by day.
  const checks = server.health.reduce((n, d) => n + d.checks, 0), passed = server.health.reduce((n, d) => n + d.passed, 0);
  $('st-health-h').textContent = checks === passed ? 'Every health check passed' : `${num(passed)} of ${num(checks)} health checks passed`;
  $('st-health-text').textContent = server.health.length
    ? `${num(checks)} pings from ${server.healthLocations} region${server.healthLocations === 1 ? '' : 's'} since ${labelDay(server.health[0].day)}.`
    : 'No uptime checks have run yet.';
  $('st-health').innerHTML = server.health.slice(-14).map((d) => `<div title="${d.passed} of ${d.checks} passed"><b class="${d.passed < d.checks ? 'miss' : ''}">${d.passed < d.checks ? '✕' : '✓'} ${num(d.passed)}</b><span>${labelDay(d.day)}</span></div>`).join('');
}

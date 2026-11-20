// The race for most wins (a line chart you can play) and the highlight reel: the longest streak, the
// fastest knockouts, the free-for-all king and the first student win over the house.
import { esc } from '../util.js';
import { when, clock, num, labelDay, winnerOf, recordOf, winCounts, baseName, barList } from './data.js';

const SER = ['s1', 's2', 's3', 's4', 's5'];
const short = (n) => (n.length > 16 ? `${n.slice(0, 6)}…${n.slice(-3)}` : n);

export const recordsHtml = `
  <section class="st-block" aria-labelledby="st-race-h">
    <p class="st-eyebrow">The race for most wins</p>
    <h2 id="st-race-h">Who piled up wins, and when</h2>
    <p class="st-lede">Total wins over time for the five winningest student bots. Press play to watch it unfold, or drag the slider.</p>
    <div class="st-panel" style="display:grid;gap:12px">
      <div class="st-race-controls">
        <button type="button" class="st-btn primary" id="st-play">▶ Play</button>
        <input type="range" id="st-scrub" min="0" max="0" value="0" aria-label="Show wins through this day">
        <span class="st-race-date" id="st-race-date"></span>
      </div>
      <div class="st-legend" id="st-race-legend"></div>
      <div class="st-chart"><svg id="st-race" viewBox="0 0 960 360" role="img" aria-label="Total wins by day for the top five student bots"></svg></div>
    </div>
  </section>
  <section class="st-block" aria-labelledby="st-rec-h">
    <p class="st-eyebrow">Records</p>
    <h2 id="st-rec-h">The highlight reel</h2>
    <div class="st-cards two">
      <div class="st-panel st-card" id="st-streak-card">
        <h3 id="st-streak-h">The longest win streak</h3>
        <p id="st-streak-text"></p>
        <div class="st-chart"><svg id="st-streak" viewBox="0 0 320 70" role="img" aria-label="Each win in the streak along its timeline"></svg></div>
      </div>
      <div class="st-panel st-card">
        <h3>Fastest knockouts</h3>
        <p>Game time from the first tick to the last enemy building falling. Hit race to replay the top five at 5× speed.</p>
        <div class="st-bars" id="st-ko"></div>
        <div><button type="button" class="st-btn primary" id="st-race-ko">▶ Race</button> <span class="mono" id="st-ko-clock" style="font-size:13px;color:var(--muted)"></span></div>
      </div>
      <div class="st-panel st-card">
        <h3>King of the free-for-all</h3>
        <p id="st-ffa-text"></p>
        <div class="st-bars" id="st-ffa"></div>
      </div>
      <div class="st-panel st-card">
        <h3>The house fell fast</h3>
        <div class="big" id="st-first-big"></div>
        <p id="st-first-win"></p>
        <div class="st-bars" id="st-house"></div>
        <p style="font-size:13px">House bot win rate (every copy of each counted together).</p>
      </div>
    </div>
    <div class="st-src">Source: the match archive</div>
  </section>`;

export function mountRecords(root, data, tip, scope) {
  const { bots, matches } = data;
  const $ = (id) => root.querySelector(`#${id}`);
  const name = (b) => bots[b].name;
  const wins = winCounts(data);
  mountRace($, data, wins, tip, scope);

  // The longest streak by a student bot, each win along its timeline.
  let star = null, starRec = null;
  for (const [b] of wins) {
    if (bots[b].kind === 'house') continue;
    const r = recordOf(data, b);
    if (!starRec || r.best > starRec.best) { star = b; starRec = r; }
  }
  if (star != null && starRec.best > 1) {
    const a = +starRec.bestStart.at, z = +starRec.bestEnd.at;
    const run = matches.filter((m) => m.players.includes(star) && +m.at >= a && +m.at <= z);
    const span = z - a, mins = Math.round(span / 60000);
    const lasted = mins < 120 ? `${mins} minutes` : `${Math.round(span / 864e5) || 1} day${Math.round(span / 864e5) > 1 ? 's' : ''}`;
    const sameDay = starRec.bestStart.day === starRec.bestEnd.day;
    $('st-streak-h').textContent = `${starRec.best} wins in a row`;
    $('st-streak-text').innerHTML = `<span class="st-who">${esc(name(star))}</span> won ${starRec.best} straight, from ${esc(when.format(starRec.bestStart.at))} to ${esc(sameDay ? clock.format(starRec.bestEnd.at) : when.format(starRec.bestEnd.at))}: ${lasted}. Each dot is one win.`;
    const W = 320, L = 8, R = 8;
    let s = `<line class="st-base" x1="${L}" x2="${W - R}" y1="34" y2="34"/>`;
    run.forEach((m, k) => {
      const x = L + ((W - L - R) * (+m.at - a)) / Math.max(1, span);
      s += `<circle cx="${x.toFixed(1)}" cy="${24 - (k % 3) * 8}" r="4" fill="var(--s1)" stroke="var(--panel)" stroke-width="2"><title>Win ${k + 1}: ${esc(when.format(m.at))}</title></circle>`;
    });
    const fmtEnd = (d) => (sameDay ? clock : when).format(d);
    s += `<text x="${L}" y="56">${esc(fmtEnd(starRec.bestStart.at))}</text><text x="${W - R}" y="56" text-anchor="end">${esc(fmtEnd(starRec.bestEnd.at))}</text>`;
    $('st-streak').innerHTML = s;
  } else {
    $('st-streak-card').hidden = true;
  }

  // Fastest knockouts, one per winner, raced at 5x speed.
  const seen = new Set(), ko = [];
  for (const m of matches.filter((x) => x.reason === 'E' && x.win >= 0).sort((x, y) => x.gameS - y.gameS)) {
    const w = winnerOf(m); if (seen.has(w)) continue; seen.add(w); ko.push(m); if (ko.length === 5) break;
  }
  const koMax = Math.max(1, ...ko.map((m) => m.gameS));
  $('st-ko').innerHTML = barList(ko.map((m) => [name(winnerOf(m)), m.gameS, `${m.gameS.toFixed(1)} s`]), { max: koMax });
  let koTimer = 0;
  scope.on($('st-race-ko'), 'click', () => {
    scope.clear(koTimer);
    const fills = [...$('st-ko').querySelectorAll('.fl')];
    const t0 = performance.now();
    const step = () => {
      const t = ((performance.now() - t0) / 1000) * 5;
      fills.forEach((f, k) => { const s = ko[k].gameS; f.style.width = `${((100 * Math.min(t, s)) / koMax).toFixed(1)}%`; f.classList.toggle('hl', k === 0 && t >= s); });
      $('st-ko-clock').textContent = `${Math.min(t, koMax).toFixed(1)} s of game time`;
      if (t < koMax) koTimer = scope.later(step, 40);
    };
    step();
  });

  // Free-for-all winners: the biggest fields (15 or more bots).
  const big = matches.filter((m) => m.players.length >= 15);
  const ffa = new Map();
  for (const m of big) { const w = winnerOf(m); const k = w < 0 ? 'No winner' : baseName(name(w)); ffa.set(k, (ffa.get(k) || 0) + 1); }
  const ffaTop = [...ffa].sort((a, b) => b[1] - a[1]).slice(0, 6);
  $('st-ffa-text').textContent = `Winners of the ${num(big.length)} biggest brawls (15 or more bots on the map).`;
  $('st-ffa').innerHTML = barList(ffaTop.map(([n, c]) => [n, c]), { highlight: 0 });

  // The first student win over a house bot, and how each house bot has fared.
  const firstWin = matches.find((m) => { const w = winnerOf(m); return w >= 0 && bots[w].kind !== 'house' && m.players.some((p) => bots[p].kind === 'house'); });
  if (firstWin && matches.length) {
    const hrs = (firstWin.at - matches[0].at) / 3.6e6;
    $('st-first-big').textContent = hrs < 48 ? `${Math.round(hrs)} hrs` : `${Math.round(hrs / 24)} days`;
    const w = winnerOf(firstWin);
    $('st-first-win').innerHTML = `The first match ran ${esc(when.format(matches[0].at))}. At ${esc(when.format(firstWin.at))}, <span class="st-who">${esc(name(w))}</span> beat ${esc(firstWin.players.filter((p) => p !== w).map(name).join(', '))} in ${num(firstWin.ticks)} ticks: the first student win over the house.`;
  }
  const house = new Map();
  for (const m of matches) for (const p of m.players) if (bots[p].kind === 'house') {
    const k = baseName(name(p)); const e = house.get(k) || [0, 0]; e[0]++; if (winnerOf(m) === p) e[1]++; house.set(k, e);
  }
  const hRows = [...house].filter(([, e]) => e[0] >= 20).sort((a, b) => b[1][1] / b[1][0] - a[1][1] / a[1][0]);
  $('st-house').innerHTML = barList(hRows.map(([n, [p, w]]) => [n, (100 * w) / p, `${Math.round((100 * w) / p)}%`]), { max: 100 });
}

/** Total wins by day for the five winningest student bots: a line chart with play, scrub and hover. */
function mountRace($, data, wins, tip, scope) {
  const { bots, matches, days } = data;
  const top = [...wins].filter(([b]) => bots[b].kind !== 'house').sort((a, b) => b[1] - a[1]).slice(0, 5).map(([b]) => b);
  const dayAt = new Map(days.map((d, i) => [d, i]));
  const cum = top.map(() => new Array(days.length).fill(0));
  for (const m of matches) { const k = top.indexOf(winnerOf(m)); if (k >= 0) cum[k][dayAt.get(m.day)]++; }
  cum.forEach((row) => { for (let d = 1; d < row.length; d++) row[d] += row[d - 1]; });
  $('st-race-legend').innerHTML = top.map((b, k) => `<span><i style="background:var(--${SER[k]})"></i>${esc(bots[b].name)}</span>`).join('');
  const W = 960, H = 360, l = 44, r = 140, t = 12, b = 30;
  const last = Math.max(0, days.length - 1);
  const step = Math.max(10, Math.ceil(Math.max(1, ...cum.map((row) => row[last] ?? 0)) / 6 / 10) * 10);
  const yMax = Math.max(step, Math.ceil(Math.max(1, ...cum.map((row) => row[last] ?? 0)) / step) * step);
  const x = (d) => l + ((W - l - r) * d) / Math.max(1, last);
  const y = (v) => t + (H - t - b) * (1 - v / yMax);
  const scrub = $('st-scrub');
  scrub.max = String(last);
  let cut = last, hover = null, timer = 0;

  function draw() {
    let s = '';
    for (let v = 0; v <= yMax; v += step) s += `<line class="st-gridl" x1="${l}" x2="${W - r}" y1="${y(v)}" y2="${y(v)}"/><text x="${l - 8}" y="${y(v) + 4}" text-anchor="end">${v}</text>`;
    s += `<line class="st-base" x1="${l}" x2="${W - r}" y1="${y(0)}" y2="${y(0)}"/>`;
    const every = Math.max(1, Math.round(days.length / 7));
    for (let d = 0; d < days.length; d += every) s += `<text x="${x(d)}" y="${H - 8}" text-anchor="middle">${labelDay(days[d])}</text>`;
    const ends = cum.map((row, k) => ({ k, y: y(row[cut]) })).sort((a, c) => a.y - c.y);
    for (let j = 1; j < ends.length; j++) if (ends[j].y - ends[j - 1].y < 15) ends[j].y = ends[j - 1].y + 15;
    cum.forEach((row, k) => {
      const pts = row.slice(0, cut + 1).map((v, d) => `${x(d).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
      s += `<polyline points="${pts}" fill="none" stroke="var(--${SER[k]})" stroke-width="2" stroke-linejoin="round"/>`;
      s += `<circle cx="${x(cut)}" cy="${y(row[cut])}" r="4.5" fill="var(--${SER[k]})" stroke="var(--panel)" stroke-width="2"/>`;
    });
    for (const e of ends) s += `<text class="lab" x="${x(cut) + 9}" y="${e.y + 4}">${esc(short(bots[top[e.k]].name))} ${cum[e.k][cut]}</text>`;
    if (hover != null) s += `<line class="st-cross" x1="${x(hover)}" x2="${x(hover)}" y1="${t}" y2="${y(0)}"/>`;
    $('st-race').innerHTML = s;
    $('st-race-date').textContent = days.length ? `through ${labelDay(days[cut])}` : '';
    scrub.value = String(cut);
  }
  function stop() { scope.clear(timer); timer = 0; $('st-play').textContent = '▶ Play'; }
  function tick() { cut++; draw(); if (cut >= last) stop(); else timer = scope.later(tick, 180); }
  scope.on($('st-play'), 'click', () => {
    if (timer) { stop(); return; }
    if (cut >= last) cut = 0;
    $('st-play').textContent = '❚❚ Pause';
    draw();
    timer = scope.later(tick, 180);
  });
  scope.on(scrub, 'input', () => { stop(); cut = +scrub.value; draw(); });
  scope.on($('st-race'), 'pointermove', (e) => {
    const box = $('st-race').getBoundingClientRect();
    const d = Math.round(((((e.clientX - box.left) * W) / box.width - l) / (W - l - r)) * last);
    if (d < 0 || d > cut) { hover = null; tip.hide(); draw(); return; }
    hover = d; draw();
    const rows = top.map((bot, k) => [cum[k][d], k]).sort((a, c) => c[0] - a[0]);
    tip.show(`<b>${labelDay(days[d])}</b><br>${rows.map(([v, k]) => `${esc(short(bots[top[k]].name))}: ${v}`).join('<br>')}`, e.clientX, e.clientY);
  });
  scope.on($('st-race'), 'pointerleave', () => { hover = null; tip.hide(); draw(); });
  if (!top.length) $('st-race').closest('.st-block').hidden = true;
  else draw();
}

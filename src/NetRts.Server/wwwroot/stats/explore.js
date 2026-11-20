// "Every match ever played": one square per match, colored three ways, with a bot finder that lights up
// one bot's matches and shows its record. Tapping a square shows who played and how it ended.
import { esc } from '../util.js';
import { when, clock, num, gameTime, REASON, KIND, winnerOf, recordOf, winCounts } from './data.js';

const SIZE = [[2, 2, 'o1', '2 bots'], [3, 4, 'o2', '3–4'], [5, 8, 'o3', '5–8'], [9, 99, 'o4', '9–16']];
const LEGENDS = {
  end: [['s1', 'Elimination'], ['s3', 'Time limit'], ['s2', 'Surrender']],
  who: [['s1', 'Student code won'], ['s2', 'Block Bot won'], ['st-neutral', 'House bot won']],
  size: SIZE.map((s) => [s[2], s[3]]),
  focus: [['s1', 'Won'], ['s2', 'Lost'], ['st-faint', 'Not in it']],
};

export function exploreHtml(count) {
  return `<section class="st-block" aria-labelledby="st-every">
    <p class="st-eyebrow">Every match ever played</p>
    <h2 id="st-every">${num(count)} squares, one per match</h2>
    <p class="st-lede">Oldest at the top left, newest at the bottom right. Hover for the matchup, tap a square for the details, or type a bot's name to light up its matches and see its record.</p>
    <div class="st-controls">
      <div class="st-seg" role="group" aria-label="Color the squares by" id="st-mode">
        <button type="button" data-mode="end" aria-pressed="true">How it ended</button>
        <button type="button" data-mode="who" aria-pressed="false">Who won</button>
        <button type="button" data-mode="size" aria-pressed="false">How many bots</button>
      </div>
      <div class="st-find">
        <input id="st-bot" list="st-bots" placeholder="Find a bot…" autocomplete="off" aria-label="Find a bot">
        <datalist id="st-bots"></datalist>
        <button type="button" class="st-btn" id="st-clear" hidden>Clear</button>
      </div>
    </div>
    <div class="st-legend" id="st-legend"></div>
    <div class="st-explore">
      <div class="st-panel">
        <div class="st-waffle" id="st-waffle" role="img" aria-label="A grid of every match, colored by the selected measure"></div>
        <div class="st-axis"><span id="st-first"></span><span id="st-last"></span></div>
      </div>
      <aside class="st-panel st-detail" id="st-detail" aria-live="polite"></aside>
    </div>
    <div class="st-src">Source: the match archive, refreshed every few minutes</div>
  </section>`;
}

export function mountExplore(root, data, tip, scope) {
  const { bots, matches } = data;
  const $ = (id) => root.querySelector(`#${id}`);
  const name = (b) => bots[b].name;
  const waffle = $('st-waffle');
  waffle.innerHTML = matches.map((m) => `<span data-i="${m.i}"></span>`).join('');
  const cells = [...waffle.children];
  if (matches.length) {
    $('st-first').textContent = when.format(matches[0].at).replace(/,.*/, '');
    $('st-last').textContent = `${when.format(matches.at(-1).at).replace(/,.*/, '')} →`;
  }
  let mode = 'end', focus = null, picked = null;

  function colorOf(m) {
    if (focus != null) return m.players.includes(focus) ? (winnerOf(m) === focus ? 's1' : 's2') : null;
    if (mode === 'end') return { E: 's1', T: 's3', S: 's2' }[m.reason] || 'st-neutral';
    if (mode === 'who') { const w = winnerOf(m); return w < 0 ? 'st-faint' : { code: 's1', block: 's2', house: 'st-neutral' }[bots[w].kind]; }
    const n = m.players.length;
    return SIZE.find((s) => n >= s[0] && n <= s[1])[2];
  }
  function paint() {
    for (const m of matches) {
      const c = colorOf(m), el = cells[m.i];
      el.style.background = c ? `var(--${c})` : 'var(--st-neutral)';
      el.classList.toggle('dim', c == null);
    }
    $('st-legend').innerHTML = LEGENDS[focus != null ? 'focus' : mode].map(([c, t]) => `<span><i style="background:var(--${c})"></i>${t}</span>`).join('');
  }
  scope.on($('st-mode'), 'click', (e) => {
    const b = e.target.closest('button[data-mode]'); if (!b) return;
    mode = b.dataset.mode;
    $('st-mode').querySelectorAll('button').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    if (focus != null) clearFocus(); else paint();
  });

  scope.on(waffle, 'pointermove', (e) => {
    const i = e.target.dataset?.i; if (i == null) return tip.hide();
    const m = matches[+i], w = winnerOf(m);
    tip.show(`<b>${esc(when.format(m.at))}</b><br>${m.players.length} bots · ${REASON[m.reason]}<br>${w >= 0 ? `Won by ${esc(name(w))}` : 'No winner'}`, e.clientX, e.clientY);
  });
  scope.on(waffle, 'pointerleave', () => tip.hide());
  scope.on(waffle, 'click', (e) => { const i = e.target.dataset?.i; if (i != null) pickMatch(+i); });

  function pickMatch(i) {
    if (picked != null) cells[picked].classList.remove('sel');
    picked = i; cells[i].classList.add('sel');
    const m = matches[i], w = winnerOf(m);
    $('st-detail').innerHTML = `
      <p class="st-eyebrow">Match ${num(i + 1)} of ${num(matches.length)}</p>
      <h3>${esc(when.format(m.at))}</h3>
      <dl class="st-kv">
        <dt>Ended by</dt><dd>${REASON[m.reason]}</dd>
        <dt>Game time</dt><dd>${gameTime(m.gameS)} (${num(m.ticks)} ticks)</dd>
        <dt>Bots</dt><dd>${m.players.length}</dd>
        <dt>Kind</dt><dd>${esc(m.category)}</dd>
      </dl>
      <ul class="st-roster">${m.players.map((p) => `<li><button type="button" data-bot="${p}" class="${p === w ? 'won' : ''}">${esc(name(p))}</button>${p === w ? '<span class="crown">winner</span>' : ''}<span class="tag">${KIND[bots[p].kind]}</span></li>`).join('')}</ul>
      <a href="#/match/${m.id}">Open this match</a>`;
  }
  scope.on($('st-detail'), 'click', (e) => {
    const b = e.target.closest('[data-bot]'); if (b) setFocus(+b.dataset.bot);
  });

  function setFocus(b) {
    focus = b; $('st-bot').value = name(b); $('st-clear').hidden = false; paint();
    const s = recordOf(data, b);
    const pct = s.played ? Math.round((100 * s.wins) / s.played) : 0;
    const end = s.bestEnd && (s.bestEnd.day === s.bestStart.day ? clock.format(s.bestEnd.at) : when.format(s.bestEnd.at));
    const streak = s.best > 1 ? `${s.best} straight (${esc(when.format(s.bestStart.at))} – ${esc(end)})` : s.best ? '1' : '—';
    $('st-detail').innerHTML = `
      <p class="st-eyebrow">${KIND[bots[b].kind]}</p>
      <h3>${esc(name(b))}</h3>
      <div class="st-big3">
        <div><b>${num(s.played)}</b><span>played</span></div>
        <div><b>${num(s.wins)}</b><span>won</span></div>
        <div><b>${pct}%</b><span>win rate</span></div>
      </div>
      <dl class="st-kv">
        <dt>Best streak</dt><dd>${streak}</dd>
        <dt>First match</dt><dd>${s.first ? esc(when.format(s.first.at)) : '—'}</dd>
        <dt>Most beaten</dt><dd>${s.victim ? `${esc(name(s.victim[0]))} ×${s.victim[1]}` : '—'}</dd>
        <dt>Nemesis</dt><dd>${s.nemesis ? `${esc(name(s.nemesis[0]))} ×${s.nemesis[1]}` : '—'}</dd>
        <dt>Biggest win</dt><dd>${s.biggestWin ? `against ${s.biggestWin - 1} other bot${s.biggestWin > 2 ? 's' : ''}` : '—'}</dd>
        <dt>Fastest KO</dt><dd>${s.fastest ? gameTime(s.fastest.gameS) : '—'}</dd>
      </dl>
      <p>Its matches are lit up in the grid: blue for wins, orange for losses. Tap one to see who else played.</p>`;
  }
  function clearFocus() { focus = null; $('st-bot').value = ''; $('st-clear').hidden = true; paint(); intro(); }
  scope.on($('st-clear'), 'click', clearFocus);

  // The finder lists every bot by name, the most-played first.
  const played = new Map();
  for (const m of matches) for (const p of m.players) played.set(p, (played.get(p) || 0) + 1);
  const byName = new Map();
  [...played].sort((a, b) => b[1] - a[1]).forEach(([p]) => { if (!byName.has(name(p))) byName.set(name(p), p); });
  $('st-bots').innerHTML = [...byName.keys()].map((n) => `<option value="${esc(n)}"></option>`).join('');
  scope.on($('st-bot'), 'input', (e) => { const b = byName.get(e.target.value.trim()); if (b != null) setFocus(b); });
  scope.on($('st-bot'), 'keydown', (e) => {
    if (e.key !== 'Enter') return;
    const q = e.target.value.trim().toLowerCase(); if (!q) { clearFocus(); return; }
    const names = [...byName.keys()];
    const hit = names.find((n) => n.toLowerCase().startsWith(q)) || names.find((n) => n.toLowerCase().includes(q));
    if (hit) setFocus(byName.get(hit));
  });

  const tries = [...winCounts(data)].filter(([b]) => bots[b].kind !== 'house').sort((a, b) => b[1] - a[1]).slice(0, 4).map(([b]) => b);
  function intro() {
    if (picked != null) { cells[picked].classList.remove('sel'); picked = null; }
    $('st-detail').innerHTML = `
      <p class="st-eyebrow">Explore</p>
      <h3>Pick a square or a bot</h3>
      <p>Tap any square to see who played, how it ended and how long it took.${tries.length ? ' Or try one of these:' : ''}</p>
      <div class="st-chips">${tries.map((b) => `<button type="button" class="st-chip" data-bot="${b}">${esc(name(b))}</button>`).join('')}</div>`;
  }
  paint();
  intro();
}

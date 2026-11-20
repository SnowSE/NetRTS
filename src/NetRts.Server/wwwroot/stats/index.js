// #/stats: the game by the numbers. Every match from the archive (live, refreshed every few minutes) to
// explore by bot, the race for most wins, the records, and how the server held up (its telemetry, read
// each morning through the end of yesterday).
import { api, esc, mountScope, setTitle } from '../util.js';
import { shape, num, labelDay, makeTip } from './data.js';
import { exploreHtml, mountExplore } from './explore.js';
import { recordsHtml, mountRecords } from './records.js';
import { hoodHtml, mountHood } from './hood.js';

function loadStyles() {
  if (document.querySelector('link[data-stats-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL('./stats.css', import.meta.url).href;
  link.dataset.statsCss = '';
  document.head.append(link);
}

export function mountStats(main) {
  const scope = mountScope();
  loadStyles();
  setTitle('Stats');
  main.innerHTML = '<div class="st"><p class="st-note">Counting every match…</p></div>';
  const root = main.querySelector('.st');

  Promise.all([
    scope.wait(api('/api/v1/stats/matches')),
    scope.wait(api('/api/v1/stats/server').catch(() => null)),   // not connected (local runs, tests): leave it out
  ]).then(([raw, server]) => {
    const data = shape(raw);
    const students = new Set();
    for (const m of data.matches) for (const p of m.players) if (data.bots[p].kind !== 'house') students.add(p);
    const errors = server ? server.statusCodes.find((c) => c.name === '500')?.count ?? 0 : null;
    const since = data.days.length ? labelDay(data.days[0]) : '';
    const tiles = [
      [num(data.matches.length), 'matches played'],
      [num(students.size), 'student bots'],
      server && [server.requests >= 1e6 ? `${(server.requests / 1e6).toFixed(2)}M` : num(server.requests), 'requests answered'],
      server && [num(errors), 'server errors (HTTP 500)'],
      server && [`${server.tickAverageMs.toFixed(1)} ms`, 'average game tick'],
    ].filter(Boolean);
    root.innerHTML = `
      <header class="st-mast">
        <div>
          <p class="kicker">${since ? `Since ${esc(since)}` : 'Stats'}</p>
          <h1>By the <em>numbers</em></h1>
          <p class="dek">Every match since the game went live. Look up any bot, scrub through the race for most wins, and see when the server is busiest.</p>
        </div>
        <div class="st-tiles">${tiles.map(([b, s]) => `<div class="st-tile"><b>${esc(b)}</b><span>${esc(s)}</span></div>`).join('')}</div>
      </header>
      ${exploreHtml(data.matches.length)}
      ${recordsHtml}
      ${hoodHtml(server)}`;
    const tip = makeTip(scope);
    mountExplore(root, data, tip, scope);
    mountRecords(root, data, tip, scope);
    mountHood(root, server, tip, scope);
  }).catch((e) => {
    root.innerHTML = `<p class="error" role="alert">The stats couldn't load: ${esc(e.message)}</p>`;
  });

  return () => scope.dispose();
}

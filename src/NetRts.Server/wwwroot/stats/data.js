// The Stats page's data and the arithmetic on it: matches from GET /api/v1/stats/matches turned into
// something easy to count, the per-bot record, and the shared formatting and tooltip.
import { esc } from '../util.js';

export const TZ = 'America/Denver';
export const when = new Intl.DateTimeFormat('en-US', { timeZone: TZ, month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
export const clock = new Intl.DateTimeFormat('en-US', { timeZone: TZ, hour: 'numeric', minute: '2-digit' });
const dayKey = new Intl.DateTimeFormat('en-CA', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit' });
const shortDay = new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', month: 'short', day: 'numeric' });

export const num = (n) => Number(n).toLocaleString('en-US');
/** "Nov 19" for a "2026-11-19" day. */
export const labelDay = (d) => shortDay.format(new Date(`${d}T12:00:00Z`));
export const REASON = { E: 'Elimination', T: 'Time limit', S: 'Surrender', O: 'Other' };
export const KIND = { house: 'house bot', block: 'Block Bot', code: 'student code' };

/** Game time, as "20.2 s" or "3 min 05 s". */
export function gameTime(s) {
  return s < 60 ? `${s.toFixed(1)} s` : `${Math.floor(s / 60)} min ${String(Math.round(s % 60)).padStart(2, '0')} s`;
}

/** The days from first to last, inclusive, as "yyyy-mm-dd". */
export function dayRange(first, last) {
  const out = [];
  for (let t = Date.parse(`${first}T12:00:00Z`); t <= Date.parse(`${last}T12:00:00Z`); t += 864e5) out.push(new Date(t).toISOString().slice(0, 10));
  return out;
}

/** The archive, shaped for counting: each match with its time, local day, end and players by bot index. */
export function shape(raw) {
  const bots = raw.bots.map((b) => ({ name: b.name, kind: b.kind }));
  const matches = raw.matches.map((m, i) => {
    const at = new Date(m.at);
    const reason = { Elimination: 'E', TimeLimit: 'T', Surrender: 'S' }[m.reason] || 'O';
    return { i, id: m.id, at, day: dayKey.format(at), reason, ticks: m.ticks, tickMs: m.tickMs, win: m.winner, players: m.players, category: m.category, gameS: (m.ticks * m.tickMs) / 1000 };
  });
  const days = matches.length ? dayRange(matches[0].day, matches.at(-1).day) : [];
  return { bots, matches, days };
}

export const winnerOf = (m) => (m.win >= 0 ? m.players[m.win] : -1);

/** One bot's record: played, won, best streak, its favorite victim and its nemesis. */
export function recordOf(data, bot) {
  let played = 0, wins = 0, cur = 0, best = 0, bestStart = null, bestEnd = null, start = null, first = null, biggestWin = 0, fastest = null;
  const beat = new Map(), lostTo = new Map();
  for (const m of data.matches) {
    if (!m.players.includes(bot)) continue;
    played++; first ??= m;
    const w = winnerOf(m);
    if (w === bot) {
      wins++; if (!cur) start = m; cur++;
      if (cur > best) { best = cur; bestStart = start; bestEnd = m; }
      for (const p of m.players) if (p !== bot) beat.set(p, (beat.get(p) || 0) + 1);
      biggestWin = Math.max(biggestWin, m.players.length);
      if (m.reason === 'E' && (!fastest || m.gameS < fastest.gameS)) fastest = m;
    } else {
      cur = 0;
      if (w >= 0) lostTo.set(w, (lostTo.get(w) || 0) + 1);
    }
  }
  const top = (mp) => [...mp].sort((a, b) => b[1] - a[1])[0];
  return { played, wins, best, bestStart, bestEnd, first, victim: top(beat), nemesis: top(lostTo), biggestWin, fastest };
}

/** Wins by bot index, every bot that won anything. */
export function winCounts(data) {
  const wins = new Map();
  for (const m of data.matches) { const w = winnerOf(m); if (w >= 0) wins.set(w, (wins.get(w) || 0) + 1); }
  return wins;
}

/** The base name of a house bot copy ("house-balanced 3" is house-balanced). */
export const baseName = (name) => name.replace(/ \d+$/, '');

/** A horizontal bar list: [label, value, shown] rows, scaled to the largest (or to max). */
export function barList(rows, { max, highlight = -1 } = {}) {
  const top = max ?? Math.max(1, ...rows.map((r) => r[1]));
  return rows.map(([label, value, shown], k) => `<div class="st-bar"><span class="nm" title="${esc(label)}">${esc(label)}</span><span class="tr"><span class="fl${k === highlight ? ' hl' : ''}" style="width:${((100 * value) / top).toFixed(1)}%"></span></span><span class="v">${esc(shown ?? num(value))}</span></div>`).join('');
}

/** One floating tooltip for the page, kept clear of the window's edges. */
export function makeTip(scope) {
  const tip = document.createElement('div');
  tip.className = 'st-tip';
  tip.hidden = true;
  document.body.append(tip);
  scope.cleanup(() => tip.remove());
  return {
    show(htmlText, x, y) {
      tip.innerHTML = htmlText; tip.hidden = false;
      const r = tip.getBoundingClientRect();
      let left = x + 14, top = y + 14;
      if (left + r.width > innerWidth - 8) left = x - r.width - 14;
      if (top + r.height > innerHeight - 8) top = y - r.height - 14;
      tip.style.left = `${Math.max(8, left)}px`; tip.style.top = `${Math.max(8, top)}px`;
    },
    hide() { tip.hidden = true; },
  };
}

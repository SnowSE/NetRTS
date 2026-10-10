// Badger Brawl commercials: a frame-exact timeline. window.seek(t) renders the frame at t seconds, so the
// same page plays live in a browser (?cut=short|long) or renders to video one frame at a time (render.mjs).
import { Battle3D, unitPos, frameAt, TEAM } from './battle3d.js';
import { MapView } from '/src/NetRts.Server/wwwroot/mapview.js';

const $ = (s) => document.querySelector(s);
const M = window.MATCHES;
const params = new URLSearchParams(location.search);
const CUT = params.get('cut') === 'long' ? 'long' : 'short';
const BEAT = 0.5; // 120 bpm

// ---------- maths

const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const lerp = (a, b, t) => a + (b - a) * t;
const prog = (t, a, b) => clamp((t - a) / (b - a));
const eOut = (t) => 1 - Math.pow(1 - t, 3);
const eIn = (t) => t * t * t;
const eIO = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const eExpo = (t) => (t >= 1 ? 1 : 1 - Math.pow(2, -10 * t));
const eBack = (t) => { const c = 1.9; return 1 + (c + 1) * Math.pow(t - 1, 3) + c * Math.pow(t - 1, 2); };
const mix3 = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));

/** Fade in over [a, a+i], hold, fade out over [b-o, b]. */
const inout = (t, a, b, i = 0.25, o = 0.25) => Math.min(prog(t, a, a + i), 1 - prog(t, b - o, b));

// ---------- tiny C# / shell highlighter

const KW = new Set(['var', 'new', 'return', 'if', 'null', 'object', 'int', 'string', 'async', 'await', 'while', 'foreach', 'in', 'bool', 'true', 'false', 'class', 'record', 'using']);
function hl(code) {
  const re = /(\/\/.*$)|(\$?"(?:[^"\\]|\\.)*")|(\b\d+\b)|(\b[A-Za-z_]\w*\b)|(=>|&&|\|\||[{}()[\];.,=<>!+\-*/?|])/gm;
  let out = '', last = 0, m;
  while ((m = re.exec(code))) {
    out += esc(code.slice(last, m.index));
    const [tok, cmt, str, num, word] = m;
    if (cmt) out += `<span class="c">${esc(tok)}</span>`;
    else if (str) out += `<span class="s">${esc(tok)}</span>`;
    else if (num) out += `<span class="n">${tok}</span>`;
    else if (word) {
      const next = code.slice(re.lastIndex).match(/^\s*\(/);
      if (KW.has(word)) out += `<span class="k">${word}</span>`;
      else if (next) out += `<span class="m">${word}</span>`;
      else if (/^[A-Z]/.test(word)) out += `<span class="ty">${word}</span>`;
      else out += word;
    } else out += `<span class="p">${esc(tok)}</span>`;
    last = re.lastIndex;
  }
  return out + esc(code.slice(last));
}

/** Highlighted code lines with line numbers; lineClass(i) adds a class per line. */
function codeBlock(lines, firstNo, { lineClass = () => '', caretAt = null, typed = null } = {}) {
  let chars = typed ?? Infinity;
  const out = [];
  for (let i = 0; i < lines.length; i++) {
    let text = lines[i];
    let caret = false;
    if (chars <= 0) { if (typed != null) break; }
    if (chars < text.length) { text = text.slice(0, chars); caret = true; }
    chars -= lines[i].length + 1;
    const cls = lineClass(i);
    const body = hl(text) + (caret || (caretAt === i) ? '<span class="caret"></span>' : '');
    out.push(`<span class="${cls}"><span class="ln">${firstNo + i}</span>${body || ' '}</span>`);
    if (caret) break;
  }
  if (typed != null && chars > 0 && chars >= 0 && out.length === lines.length) out[out.length - 1] = out[out.length - 1].replace(/<\/span>$/, '<span class="caret"></span></span>');
  return out.join('\n');
}

function win({ x, y, w, h, tabs, body, scale = 1, alpha = 1, rot = 0, ry = 0, blur = 0, z = 0 }) {
  const tabHtml = tabs.map((t, i) => `<div class="tab ${i === 0 ? 'on' : ''}"><span class="dot ${t.dot || ''}"></span>${esc(t.name)}</div>`).join('');
  return `<div class="win" style="left:${x}px;top:${y}px;width:${w}px;height:${h}px;opacity:${alpha};transform:perspective(2200px) translateZ(${z}px) rotateY(${ry}deg) rotateZ(${rot}deg) scale(${scale});filter:blur(${blur}px)">
    <div class="tabs">${tabHtml}<div class="ctl"><span>—</span><span>☐</span><span>✕</span></div></div><pre>${body}</pre></div>`;
}

/** A line of text that reveals word by word with a blur-and-rise. */
function words(text, t, { at = 0, step = 0.08, dur = 0.45, cls = '' } = {}) {
  return text.split(' ').map((w, i) => {
    const p = eOut(prog(t, at + i * step, at + i * step + dur));
    return `<span class="word ${cls}" style="opacity:${p};transform:translateY(${(1 - p) * 40}px);filter:blur(${(1 - p) * 12}px)">${w}</span>`;
  }).join(' ');
}

// ---------- layers

const b3 = new Battle3D($('#gl'), $('#tags'));
const mv = new MapView($('#map'));
mv.destroy();
const ui = $('#ui');
const flashEl = $('#fx .flash');
const barsT = $('#fx .bars.top'), barsB = $('#fx .bars.bot');

function show3d(on) { $('#gl').style.display = on ? 'block' : 'none'; if (!on) b3.hideTags(); }
function show2d(on) { $('#map').style.display = on ? 'block' : 'none'; }

/** Draw a recorded match with the game's own spectator renderer at a fractional tick. */
function map2d(match, tick, { zoom = 1, cx, cy, vision = null, alpha = 1, tilt = 0, spin = 0 } = {}) {
  const fr = match.frames;
  if (mv.map !== match.map) { mv.setMap(match.map); mv.oreMax = new Map(); }
  const W = match.map.width, H = match.map.height;
  mv.T = Math.max(2, Math.floor(Math.min(1920 / W, 1080 / H)));
  mv.terrainCache = null;
  const k = Math.max(0, Math.min(fr.length - 2, Math.floor(tick)));
  mv.state = null;
  mv.setState(fr[k]);
  mv.setState(fr[k + 1]);
  for (const a of mv.anim.values()) { a.t0 = 0; a.dur = 1; }
  const cover = Math.max(1, 1920 / (W * mv.T), 1080 / (H * mv.T));
  mv.zoom = zoom * cover;
  mv.cam = { x: cx ?? W / 2, y: cy ?? H / 2 };
  mv.clampCamera();
  mv.vision = vision;
  mv.fogCache = null;
  mv.draw(clamp(tick - k));
  $('#map').style.opacity = alpha;
  $('#map').style.transform = tilt ? `perspective(1900px) rotateX(${tilt}deg) rotateZ(${spin}deg) scale(${1 + tilt / 60})` : 'none';
}

/** Centroid of a set of units at a tick. */
function centroid(match, ids, tick) {
  let x = 0, z = 0, n = 0;
  for (const id of ids) { const p = unitPos(match, id, tick); if (p) { x += p.x; z += p.z; n++; } }
  return n ? { x: x / n, z: z / n } : null;
}
const unitsOf = (match, tick, pred) => frameAt(match, tick).a.units.filter(pred);
const ent = (match, tick, id) => { const { a } = frameAt(match, tick); return a._u.get(id) || a._b.get(id); };

/** Orbit camera: around (cx, cz) at radius r and height h, angle in degrees. */
const orbit = (cx, cz, r, h, deg, lookY = 0.4) => {
  const a = (deg * Math.PI) / 180;
  return { pos: [cx + Math.sin(a) * r, h, cz + Math.cos(a) * r], look: [cx, lookY, cz] };
};

// ---------- sound cues (read by music.mjs)

const cues = [];
const cue = (t, type, extra = {}) => cues.push({ t: +t.toFixed(3), type, ...extra });
/** Key clicks for a typing span: chars typed linearly over [a, b]. */
function typeCues(a, b, chars, every = 2) {
  const n = Math.floor(chars / every);
  for (let i = 0; i < n; i++) cue(a + ((b - a) * i) / n, 'key', { v: 0.5 + ((i * 7919) % 10) / 20 });
}

// ---------- the shared pieces

const SERVER = 'https://netrts.snowse.io';
const RUN = 'dotnet run StarterBot.cs --player-name Frank';
const CURL = 'curl -O https://raw.githubusercontent.com/SnowSE/NetRTS/main/samples/csharp/StarterBot.cs';

const MOB_CODE = [
  '// 6. MobAttack: the moment an enemy shows up, EVERYBODY charges it.',
  'object MobAttack(State s)',
  '{',
  '    var hq = MyBuilding(s, "CommandCenter");',
  '    var enemy = s.Units',
  '        .Where(u => u.Owner != s.You.Slot)',
  '        .OrderBy(u => Math.Abs(u.X - hq.X) + Math.Abs(u.Y - hq.Y))',
  '        .FirstOrDefault();',
  '    if (enemy == null)',
  '    {',
  '        return null;',
  '    }',
  '',
  '    Console.WriteLine($"MOB ATTACK! Everybody on {enemy.Type} #{enemy.Id}");',
  '    return new { type = "Attack", units = "all", targetId = enemy.Id };',
  '}',
];
const MOB_FIRST_LINE = 180;
const FIX_LINE = '    if (enemy == null || Math.Abs(enemy.X - hq.X) + Math.Abs(enemy.Y - hq.Y) > 12)';

const DECIDE_CODE = [
  '// Your strategy goes here. Look at the state, return a list of commands.',
  'List<object> Decide(State s)',
  '{',
  '    var commands = new List<object>();',
  '    var ore = s.You.Resources;  // what we can still spend this tick',
  '',
  '    var gather = GatherOre(s);',
  '    if (gather != null)',
  '    {',
  '        commands.Add(gather);',
  '    }',
  '',
  '    var worker = TrainWorker(s, ore);',
  '    var barracks = BuildBarracks(s, ore);',
  '    var soldier = TrainSoldier(s, ore);',
  '    var attack = Attack(s);',
  '    // ...your ideas here',
  '    return commands;',
  '}',
];

// Matches used in the story (Frank is slot 0, orange; the Honey Badger is slot 1, blue).
const BEFORE = M.before, AFTER = M.after, FIXED = M.fixed, DUEL = M.duel, FOUR = M.four, RING = M.ring16;
const SCOUT = 93;
const myIds = (match, tick) => unitsOf(match, tick, (u) => u.owner === 0).map((u) => u.id);
const HQ0 = 65; // Frank's HQ id in the story matches

// ---------- scene library: each returns {dur, render(t)} where t is local seconds

/** Black screen, a terminal prompt typing a command or comment. */
function sceneColdType(lines, { dur, start, perLine = 1.3, gap = 0.35, prompt = 'PS C:\\badgers>', comment = false }) {
  const spans = [];
  let at = 0.35;
  for (const l of lines) { spans.push([at, at + perLine, l]); typeCues(start + at, start + at + perLine, l.length); at += perLine + gap; }
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const html = spans.map(([a, b, l], i) => {
        if (t < a && i > 0) return '';
        const n = Math.floor(l.length * prog(t, a, b));
        const done = t >= b;
        const isLast = i === spans.length - 1 || t < spans[i + 1][0];
        const caret = isLast && (Math.floor(t * 2.4) % 2 === 0 || !done) ? '<span class="caret"></span>' : '';
        const body = comment ? `<span class="c" style="font-style:normal">${esc(l.slice(0, n))}</span>` : esc(l.slice(0, n));
        return `<div>${comment ? '' : `<span class="prompt">${prompt}</span> `}${body}${caret}</div>`;
      }).join('');
      const glow = 0.35 + 0.1 * Math.sin(t * 3);
      const zoom = 1 + t * 0.012;
      ui.innerHTML = `<div style="position:absolute;inset:0;background:#03060b"></div>
        <div style="position:absolute;left:180px;top:440px;font:44px/1.6 var(--code);color:#d6e2f0;transform:scale(${zoom});transform-origin:0 50%;text-shadow:0 0 22px rgba(124,192,255,${glow})">${html}</div>`;
    },
  };
}

/** 3D shot with a camera function, optional tags and a caption. */
function shot3d({ dur, match, tick, cam, tags = () => [], caption = null, bars = 0, highlight = null, bloom }) {
  return {
    dur,
    render(t) {
      show2d(false); show3d(true);
      const u = t / dur;
      const tk = tick(u, t);
      b3.render({ match, tick: tk, cam: cam(u, tk, t), tags: tags(tk, u, t), highlight: highlight?.(tk), bloom });
      ui.innerHTML = caption ? caption(t, u, tk) : '';
      barsT.style.height = barsB.style.height = `${bars}px`;
    },
  };
}

function shot2d({ dur, match, tick, view, caption = null }) {
  return {
    dur,
    render(t) {
      show3d(false); show2d(true);
      const u = t / dur;
      map2d(match, tick(u), { tilt: 24, spin: lerp(-3, 3, u), ...view(u, t) });
      ui.innerHTML = caption ? caption(t, u) : '';
    },
  };
}

const lower = (t, l1, l2, { at = 0.15, out = 99 } = {}) => {
  const p = eOut(prog(t, at, at + 0.45)), q = 1 - prog(t, out - 0.25, out);
  return `<div style="position:absolute;left:0;right:0;bottom:0;height:420px;background:linear-gradient(transparent,rgba(2,6,12,0.75));opacity:${Math.min(p, q)}"></div><div class="lower" style="opacity:${Math.min(p, q)};transform:translateX(${(1 - p) * -60}px)"><div class="bar" style="width:${120 * p}px"></div>
    <div class="l1">${l1}</div>${l2 ? `<div class="l2">${l2}</div>` : ''}</div>`;
};

const bigCenter = (t, html, { at = 0, y = 400, cls = 'big', out = 99 } = {}) => {
  const q = 1 - prog(t, out - 0.2, out);
  return `<div class="title ${cls}" style="top:${y}px;opacity:${q}">${typeof html === 'function' ? html(t - at) : html}</div>`;
};

function logoCard({ dur, sub = 'A Snow College bot-programming game · Ephraim, Utah', kicker = 'Est. 1888 · Go Badgers' }) {
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const p = eExpo(prog(t, 0, 0.6));
      const s = 1.25 - 0.25 * p + t * 0.015;
      const sub1 = eOut(prog(t, 0.45, 1.0));
      const shine = prog(t, 0.2, 1.4);
      ui.innerHTML = `<div class="bg"></div>
        <div style="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%) scale(${s});text-align:center;opacity:${p};white-space:nowrap">
          <img src="/src/NetRts.Server/wwwroot/images/snow-s-white.png" style="height:150px;filter:drop-shadow(0 0 30px rgba(124,192,255,0.6))">
          <div style="font:700 190px/0.95 var(--head);letter-spacing:-0.03em;margin-top:18px;background:linear-gradient(100deg,#fff 0%,#fff ${shine * 100 - 12}%,#ffd9b8 ${shine * 100}%,#fff ${shine * 100 + 12}%,#fff 100%);-webkit-background-clip:text;color:transparent;filter:drop-shadow(0 0 40px rgba(124,192,255,0.45))">BADGER <span style="color:var(--orange);-webkit-text-fill-color:var(--orange)">BRAWL</span></div>
          <div style="font:600 40px var(--head);color:#b8c7d8;margin-top:26px;opacity:${sub1};transform:translateY(${(1 - sub1) * 20}px)">${sub}</div>
          <div class="kicker" style="margin-top:22px;opacity:${sub1}">${kicker}</div>
        </div>`;
    },
  };
}

/** The two rules. */
function rulesCard({ dur }) {
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const r1 = eOut(prog(t, 0.1, 0.6)), r2 = eOut(prog(t, dur * 0.36, dur * 0.36 + 0.5)), r3 = eOut(prog(t, dur * 0.66, dur * 0.66 + 0.5));
      ui.innerHTML = `<div class="bg"></div>
        <div class="kicker" style="position:absolute;left:150px;top:110px;opacity:${r1}">House rules · just two</div>
        <div class="rule" style="position:absolute;left:150px;top:200px;width:1620px;opacity:${r1};transform:translateX(${(1 - r1) * 80}px)">
          <div class="num">1</div><div><h3>Write the code you run.</h3><p>Use AI as a teaching assistant, not a ghostwriter. The whole point is that <b style="color:#fff">you</b> write the code, and have fun doing it.</p></div></div>
        <div class="rule" style="position:absolute;left:150px;top:480px;width:1620px;opacity:${r2};transform:translateX(${(1 - r2) * 80}px)">
          <div class="num">2</div><div><h3>Don't mess with anyone else's game.</h3><p>Find a loophole? Use it on the field, not on the server.</p></div></div>
        <div style="position:absolute;left:150px;right:150px;top:780px;display:flex;gap:40px;opacity:${r3};transform:translateY(${(1 - r3) * 30}px)">
          <div class="card" style="position:relative;flex:1;padding:28px 36px;border-color:rgba(255,138,122,0.5)"><div style="font:700 44px var(--head)" class="no">✕ The Thanos exploit</div><div style="font:30px var(--head);color:#b8c7d8;margin-top:8px">Snap, and the other players vanish. Not cool.</div></div>
          <div class="card" style="position:relative;flex:1;padding:28px 36px;border-color:rgba(95,211,168,0.5)"><div style="font:700 44px var(--head)" class="yes">✓ The Captain America exploit</div><div style="font:30px var(--head);color:#b8c7d8;margin-top:8px">Super-soldier serum for your badgers. Game on.</div></div>
        </div>`;
    },
  };
}

function scheduleCard({ dur }) {
  const dates = ['Oct 22', 'Nov 5', 'Nov 19', 'Dec 3', 'Jan 14', 'Jan 28', 'Feb 11', 'Feb 25', 'Mar 11', 'Mar 25', 'Apr 8', 'Apr 22'];
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const h = eOut(prog(t, 0, 0.5));
      const cells = dates.map((d, i) => { const p = eBack(prog(t, 0.4 + i * 0.07, 0.75 + i * 0.07)); return `<div class="date ${i === 0 ? 'first' : ''}" style="opacity:${clamp(p)};transform:scale(${0.6 + 0.4 * p})">${d}</div>`; }).join('');
      const f = eOut(prog(t, 1.6, 2.1)), g = eOut(prog(t, 2.4, 2.9));
      ui.innerHTML = `<div class="bg"></div>
        <div style="position:absolute;left:150px;top:110px;opacity:${h}"><div class="kicker">Competition rounds · every other week</div>
          <div style="font:700 100px/1 var(--head);margin-top:16px">First brawl: <span class="orange">Oct 22</span></div></div>
        <div class="dates" style="position:absolute;left:150px;right:150px;top:340px">${cells}</div>
        <div style="position:absolute;left:150px;right:150px;top:560px;display:flex;gap:30px;opacity:${f};transform:translateY(${(1 - f) * 30}px)">
          <div class="chip">🍕 Pizza at the first round each semester</div><div class="chip">🎧 In person, or remote on Discord</div></div>
        <div style="position:absolute;left:150px;right:150px;top:700px;opacity:${g};transform:translateY(${(1 - g) * 30}px)">
          <div style="font:700 64px var(--head)">🏆 Championship: <span class="winner">Apr 29, graduation afternoon</span></div>
          <div style="font:34px var(--head);color:#b8c7d8;margin-top:14px">Prizes for the top 3 cumulative scores across the season, and the top 3 in the championship.</div></div>`;
    },
  };
}

function endCard({ dur }) {
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const p = eOut(prog(t, 0, 0.7)), q = eOut(prog(t, 0.6, 1.2)), out = 1 - prog(t, dur - 0.6, dur);
      ui.innerHTML = `<div class="bg"></div><div style="position:absolute;inset:0;opacity:${out}">
        <div style="position:absolute;left:50%;top:250px;transform:translate(-50%,0) scale(${1.1 - 0.1 * p});text-align:center;opacity:${p};white-space:nowrap">
          <img src="/src/NetRts.Server/wwwroot/images/snow-s-white.png" style="height:120px;filter:drop-shadow(0 0 26px rgba(124,192,255,0.6))">
          <div style="font:700 150px/1 var(--head);letter-spacing:-0.03em;margin-top:12px">BADGER <span class="orange">BRAWL</span></div></div>
        <div style="position:absolute;left:0;right:0;top:700px;text-align:center;opacity:${q}">
          <div class="codeword" style="font-size:46px;color:var(--sky)">github.com/SnowSE/NetRTS</div>
          <div class="codeword" style="font-size:34px;color:#b8c7d8;margin-top:16px">netrts.snowse.io · first brawl Oct 22</div>
          <div style="font:600 30px var(--head);color:var(--orange);margin-top:30px;letter-spacing:0.3em">GO BADGERS</div></div></div>`;
    },
  };
}

// ---------- shots built from the recordings

/** The hero shots: a two-team clash mid-valley (duel) and a 40-badger siege (four-player match). */
const clashShot = (dur, caption, from = 252, to = 272, deg0 = 200, deg1 = 250) => shot3d({
  dur, match: DUEL, tick: (u) => lerp(from, to, u),
  cam: (u) => { const e = eIO(u); return { ...orbit(50.5, 49.5, lerp(13, 8.5, e), lerp(7.5, 4.2, e), lerp(deg0, deg1, e), 0.5), fov: 40 }; },
  tags: (tk, u) => unitsOf(DUEL, from + 4, (x) => x.type !== 'Worker' && Math.hypot(x.x - 50, x.y - 49) < 4).slice(0, 2).map((x) => ({ id: x.id, alpha: prog(u, 0.25, 0.4) })),
  caption,
});
const siegeShot = (dur, caption, from = 440, to = 470) => shot3d({
  dur, match: FOUR, tick: (u) => lerp(from, to, u),
  cam: (u) => { const e = eIO(u); return { ...orbit(55, 9, lerp(22, 11, e), lerp(14, 5, e), lerp(250, 205, e), 0.5), fov: lerp(34, 42, e) }; },
  tags: (tk, u) => [{ id: 77, kind: 'building', alpha: prog(u, 0.5, 0.65) }],
  caption,
});
const heroFly = (dur, from, to, caption) => siegeShot(dur, caption, from, to);

/** Roster close-up: orbit one entity; tag it. */
function rosterShot(dur, match, tick, id, kind, l1, l2, { r = 5, h = 3, deg0 = 20, deg1 = 60, label } = {}) {
  return shot3d({
    dur, match, tick: (u) => tick + u * dur * 3,
    cam: (u, tk) => {
      const e = ent(match, tick, id);
      const p = kind === 'unit' ? unitPos(match, id, tk) || { x: e.x + 0.5, z: e.y + 0.5 } : { x: e.x + 0.5, z: e.y + 0.5 };
      return { ...orbit(p.x, p.z, lerp(r * 1.15, r, eOut(u)), h, lerp(deg0, deg1, u), kind === 'unit' ? 0.5 : 1.0), fov: 36 };
    },
    tags: () => [{ id, kind, label }],
    caption: (t) => lower(t, l1, l2, { at: 0.1, out: dur }),
  });
}

/** The scout comes in; MobAttack fires; everybody charges. */
const scoutIncoming = (dur, caption) => shot3d({
  dur, match: AFTER, tick: (u) => lerp(141, 147.9, u),
  cam: (u, tk) => {
    const sc = unitPos(AFTER, SCOUT, tk) || { x: 30, z: 30 };
    const e = eIO(u);
    return { pos: [lerp(19, 21, e), lerp(3.2, 2.6, e), lerp(23.5, 22.5, e)], look: [lerp(sc.x, 27, 0.3), 0.7, lerp(sc.z, 27, 0.3)], fov: lerp(46, 34, e) };
  },
  tags: () => [{ id: SCOUT, label: 'Enemy scout', accent: '#ff6b5e', act: 'INCOMING' }],
  highlight: () => new Set([SCOUT]),
  caption, bars: 70,
});

/** MobAttack fires: the workers drop their pickaxes and charge across the valley. */
const WORKERS = [66, 67, 68, 69, 70, 77, 79, 81];
const mobCharge = (dur, caption, from = 155.5, to = 172) => shot3d({
  dur, match: AFTER, tick: (u) => lerp(from, to, u),
  cam: (u, tk) => {
    const c = centroid(AFTER, WORKERS, tk) || { x: 10, z: 10 };
    const e = eIO(u);
    return { pos: [c.x + lerp(4.5, 6, e), lerp(2.4, 3.6, e), c.z - lerp(3, 1, e)], look: [c.x - 0.5, 0.6, c.z + 0.5], fov: 44 };
  },
  tags: () => [67, 79].map((id) => ({ id })),
  highlight: () => new Set(WORKERS),
  caption, bars: 70,
});

/** Code view with MobAttack executing. */
function codeExec({ dur, start }) {
  cue(start + dur * 0.42, 'blip');
  cue(start + dur * 0.62, 'hit');
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const exec = Math.floor(prog(t, 0.1, dur * 0.55) * 7); // walk the highlight down the lines that run
      const order = [1, 3, 4, 5, 6, 7, 13, 14];
      const cur = order[Math.min(order.length - 1, exec)];
      const outP = prog(t, dur * 0.42, dur * 0.48), jsonP = eOut(prog(t, dur * 0.6, dur * 0.75));
      const body = codeBlock(MOB_CODE, MOB_FIRST_LINE, { lineClass: (i) => (i === cur ? 'hl' : '') });
      const s = 1.03 - 0.03 * eOut(prog(t, 0, 0.5));
      ui.innerHTML = `<div class="bg"></div>
        ${win({ x: 90, y: 70, w: 1240, h: 760, tabs: [{ name: 'StarterBot.cs' }, { name: 'bot-guide.md', dot: 'b' }], body, scale: s })}
        ${win({ x: 860, y: 800, w: 1000, h: 250, tabs: [{ name: 'PowerShell', dot: 'b' }], z: 40,
          body: `<span class="mutedc" style="color:#6a8099">Tick 146: 360 ore, 14 units\nTick 147: 360 ore, 14 units</span>\n${outP > 0 ? '<span class="out-mob">MOB ATTACK! Everybody on Scout #93</span>' : ''}\n${jsonP > 0 ? `<span style="opacity:${jsonP}"><span class="mutedc">POST</span> <span class="s">/commands</span> <span class="p">{</span> <span class="s">"type"</span>: <span class="s">"Attack"</span>, <span class="s">"units"</span>: <span class="s">"all"</span>, <span class="s">"targetId"</span>: <span class="n">93</span> <span class="p">}</span></span>` : ''}` })}`;
      ui.querySelectorAll('.win pre')[0].style.fontSize = '25px';
      ui.querySelectorAll('.win pre')[1].style.fontSize = '22px';
    },
  };
}

/** Editor: type MobAttack in. */
function typeMob({ dur, start }) {
  const total = MOB_CODE.join('\n').length;
  const a = 0.5, b = dur - 0.9;
  typeCues(start + a, start + b, total, 3);
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const n = Math.floor(total * eIO(prog(t, a, b)) * 0.15 + total * prog(t, a, b) * 0.85);
      const body = codeBlock(MOB_CODE, MOB_FIRST_LINE, { typed: n, lineClass: () => 'add' });
      const p = eOut(prog(t, 0, 0.4));
      ui.innerHTML = `<div class="bg"></div>
        ${win({ x: 120, y: 90, w: 1680, h: 820, tabs: [{ name: 'StarterBot.cs ●' }, { name: 'PowerShell', dot: 'b' }], body, alpha: p, scale: 0.96 + 0.04 * p, ry: -2 + 2 * eOut(prog(t, 0, dur)) })}
        <div class="lower" style="bottom:40px;left:140px;opacity:${prog(t, 0.6, 1)}"><div class="l2" style="font-size:30px">Idea: when an enemy shows up, everybody charges it.</div></div>`;
      ui.querySelector('.win pre').style.fontSize = '27px';
    },
  };
}

/** Editor: the one-line fix as a diff. */
function fixDiff({ dur, start }) {
  typeCues(start + 0.9, start + 2.2, 54, 3);
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      const old = MOB_CODE[8];
      const tail = ' || Math.Abs(enemy.X - hq.X) + Math.Abs(enemy.Y - hq.Y) > 12)';
      const n = Math.floor(tail.length * prog(t, 0.9, 2.2));
      const lines = MOB_CODE.slice(0, 12).slice();
      const typedLine = n > 0 ? '    if (enemy == null' + tail.slice(0, n) : old;
      lines[8] = typedLine;
      const body = codeBlock(lines, MOB_FIRST_LINE, { lineClass: (i) => (i === 8 && t > 0.8 ? 'add' : ''), caretAt: t > 0.8 && n < tail.length ? 8 : null });
      ui.innerHTML = `<div class="bg"></div>
        ${win({ x: 120, y: 110, w: 1680, h: 610, tabs: [{ name: 'StarterBot.cs ●' }], body })}
        ${lower(t, 'One-line fix:', 'only charge enemies within 12 tiles of home.', { at: 0.3 })}`;
      ui.querySelector('.win pre').style.fontSize = '27px';
    },
  };
}

function terminal({ dur, start, script, title = 'PowerShell', x = 160, y = 150, w = 1600, h = 780, font = 28, caption }) {
  // script: [{at, cmd} | {at, out, cls}]
  for (const s of script) if (s.cmd) typeCues(start + s.at, start + s.at + (s.typeDur ?? 0.9), s.cmd.length, 2), cue(start + s.at + (s.typeDur ?? 0.9) + 0.1, 'enter');
  return {
    dur,
    render(t) {
      show3d(false); show2d(false);
      let html = '';
      for (const s of script) {
        if (t < s.at) break;
        if (s.cmd) {
          const n = Math.floor(s.cmd.length * prog(t, s.at, s.at + (s.typeDur ?? 0.9)));
          const typing = n < s.cmd.length;
          html += `<span class="prompt">PS C:\\badgers&gt;</span> ${esc(s.cmd.slice(0, n))}${typing ? '<span class="caret"></span>' : ''}\n`;
        } else html += `<span class="${s.cls || ''}">${s.out}</span>\n`;
      }
      if (!script.some((s) => s.cmd && t >= s.at && t < s.at + (s.typeDur ?? 0.9))) html += `<span class="prompt">PS C:\\badgers&gt;</span> ${Math.floor(t * 2.4) % 2 ? '' : '<span class="caret"></span>'}`;
      const p = eOut(prog(t, 0, 0.35));
      ui.innerHTML = `<div class="bg"></div>${win({ x, y, w, h, tabs: [{ name: title, dot: 'b' }, { name: 'StarterBot.cs' }], body: html, alpha: p, scale: 0.97 + 0.03 * p })}${caption ? caption(t) : ''}`;
      const pre = ui.querySelector('.win pre');
      pre.style.fontSize = `${font}px`;
      pre.style.whiteSpace = 'pre-wrap';
    },
  };
}

function boxScore({ dur, match, title, sub, win: winSlot }) {
  const r = match.result;
  return {
    dur,
    render(t) {
      show3d(false); show2d(true);
      map2d(match, match.frames.length - 1.01, { zoom: 1, alpha: 0.25 });
      const p = eOut(prog(t, 0, 0.45));
      const rows = r.players.map((pl) => {
        const name = pl.name.startsWith('house-') ? 'Honey Badger <span class="mutedc">(rusher)</span>' : 'Frank <span class="mutedc">(your bot)</span>';
        return `<tr style="color:${TEAM[pl.slot]}"><td style="font-family:var(--head);font-weight:700;font-size:40px">${pl.winner ? '🏆 ' : ''}${name}</td><td>${pl.score.total.toLocaleString()}</td><td>${pl.unitsKilled}</td><td>${pl.unitsLost}</td><td>${pl.buildingsLost}</td></tr>`;
      }).join('');
      ui.innerHTML = `<div class="card" style="left:240px;right:240px;top:${230 + (1 - p) * 40}px;opacity:${p}">
        <div class="kicker">${sub}</div><div style="font:700 76px var(--head);margin:10px 0 24px">${title}</div>
        <table class="boxscore" style="width:100%;border-collapse:collapse"><tr><th>BOT</th><th>SCORE</th><th>BOWLED OVER</th><th>LOST</th><th>BUILDINGS LOST</th></tr>${rows}</table>
        <div style="font:28px var(--code);color:var(--muted);margin-top:22px">${r.outcome.reason === 'Elimination' ? 'Last HQ taken' : 'Time limit'} · tick ${r.outcome.ticks} · full replay: GET /api/v1/matches/{id}/replay</div></div>`;
    },
  };
}

/** The tick loop diagram with live JSON from a recording. */
function loopDiagram({ dur, start }) {
  for (let b = 0; b < dur; b += 1) cue(start + b, 'tick');
  return {
    dur,
    render(t) {
      show3d(false); show2d(true);
      const tick = 200 + Math.floor(t);
      map2d(DUEL, 200 + t, { zoom: 1.6, cx: 20, cy: 20, alpha: 0.18 });
      const phase = t % 1; // read → decide → send, once per tick
      const on = (a, b) => (phase >= a && phase < b ? 1 : 0);
      const box = (x, y, title, sub, lit) => `<div class="loopbox" style="left:${x}px;top:${y}px;border-color:${lit ? 'var(--orange)' : 'rgba(124,192,255,0.35)'};box-shadow:${lit ? '0 0 40px rgba(240,138,60,0.5)' : 'none'}">${title}<small>${sub}</small></div>`;
      const p = eOut(prog(t, 0, 0.5));
      const f = DUEL.frames[tick];
      const me = f.players[0];
      const sample = `{ "tick": ${tick}, "you": { "resources": ${me.resources}, ... },\n  "units": [ { "id": ${f.units[0].id}, "type": "${f.units[0].type}", "x": ${f.units[0].x}, "y": ${f.units[0].y}, "hp": ${f.units[0].hp} }, … ${f.units.length - 1} more ] }`;
      ui.innerHTML = `<div style="position:absolute;inset:0;opacity:${p}">
        <div class="kicker" style="position:absolute;left:120px;top:90px">Every tick · once a second</div>
        <div style="position:absolute;left:120px;top:130px;font:700 84px var(--head)">Read. Decide. <span class="orange">Command.</span></div>
        ${box(120, 330, 'GET /state', '?waitForTick=' + (tick + 1) + ' · long-poll', on(0, 0.33))}
        ${box(720, 330, 'Decide(state)', 'your code · any language', on(0.33, 0.66))}
        ${box(1300, 330, 'POST /commands', 'Move · Attack · Gather · Build · Produce', on(0.66, 1))}
        <div style="position:absolute;left:600px;top:380px;font:60px var(--head);color:var(--sky)">→</div>
        <div style="position:absolute;left:1190px;top:380px;font:60px var(--head);color:var(--sky)">→</div>
        <div class="hud" style="left:120px;top:540px;font-size:26px;color:#8fa3bb;white-space:pre;line-height:1.5">${esc(sample)}</div>
        <div style="position:absolute;right:120px;top:120px;text-align:right" class="stat">${tick}<small>TICK</small></div>
        ${langs(t, 780)}</div>`;
    },
  };
}

function langs(t, y, at = 1.2) {
  const L = [['C#', '#9b7cf6'], ['Python', '#f0d060'], ['JavaScript', '#f7df1e'], ['Go', '#4dd0e1'], ['Rust', '#ff8a5c'], ['Java', '#ff6b5e'], ['Kotlin', '#b39ddb'], ['C++', '#7cc0ff']];
  const p = eOut(prog(t, at, at + 0.4));
  return `<div style="position:absolute;left:120px;right:120px;top:${y}px;opacity:${p}"><div class="kicker" style="margin-bottom:22px">Any language that speaks HTTP</div>
    <div style="display:flex;gap:22px;flex-wrap:wrap">${L.map(([n, c], i) => { const q = eBack(prog(t, at + 0.1 + i * 0.06, at + 0.4 + i * 0.06)); return `<div class="chip lang" style="color:${c};border-color:${c}55;opacity:${clamp(q)};transform:translateY(${(1 - q) * 20}px)">${n}</div>`; }).join('')}</div></div>`;
}

const fogShot = (dur, caption) => shot2d({
  dur, match: AFTER, tick: (u) => lerp(120, 150, u),
  view: (u) => ({ zoom: lerp(1.0, 1.15, u), cx: 26, cy: 24, vision: u < 0.55 ? 0 : null }),
  caption,
});

const ringShot = (dur, caption) => shot2d({
  dur, match: RING, tick: (u) => lerp(420, 520, u),
  view: (u) => ({ zoom: lerp(1, 2.2, eIO(u)), cx: lerp(60, 66, u), cy: lerp(60, 26, u) }),
  caption,
});

// ---------- the two cuts

function buildShort() {
  const S = [];
  let at = 0;
  const add = (make, dur, extra = {}) => { const s = make({ dur, start: at, ...extra }); S.push({ start: at, dur, ...s }); at += dur; return s; };
  const caption3 = (txt, a = 0.1) => (t) => lower(t, txt, null, { at: a });

  add(({ dur, start }) => sceneColdType([RUN], { dur, start, perLine: 1.2 }), 2.0);
  cue(at, 'hit');
  add(({ dur }) => siegeShot(dur, (t) => bigCenter(t, (tt) => words('Write the bot.', tt, { at: 0.1 }), { y: 780, cls: 'big' }), 430, 446), 2.0);
  cue(at, 'whoosh');
  add(({ dur }) => ({
    dur,
    render(t) {
      show3d(false); show2d(false);
      const p = eExpo(prog(t, 0, 0.35));
      ui.innerHTML = `<div class="bg"></div>
        <div style="position:absolute;left:50%;top:250px;transform:translateX(-50%) scale(${1.4 - 0.4 * p});opacity:${p};font:600 46px var(--code);white-space:nowrap;color:#d6e2f0;text-shadow:0 0 30px rgba(124,192,255,0.5)">
        <span class="mutedc">POST</span> <span class="s">/commands</span> <span class="p">{</span> <span class="s">"type"</span>: <span class="s">"Attack"</span>, <span class="s">"units"</span>: <span class="s">"all"</span>, <span class="s">"targetId"</span>: <span class="n">93</span> <span class="p">}</span></div>
        ${bigCenter(t, (tt) => words('Command the badgers.', tt, { at: 0.2 }), { y: 520 })}`;
    },
  }), 1.5);
  cue(at, 'hit');
  add(({ dur }) => clashShot(dur, (t) => bigCenter(t, (tt) => words('Watch them brawl.', tt, { at: 0.1 }), { y: 780 }), 256, 270), 2.0);
  cue(at, 'boom');
  add(({ dur }) => logoCard({ dur }), 2.0);
  cue(at, 'whoosh');
  add(({ dur }) => siegeShot(dur, (t) => lower(t, 'Any language that speaks HTTP.', 'C# · Python · JavaScript · Go · Rust · Java · …', { at: 0.05 }), 452, 474), 2.0);
  cue(at, 'whoosh');
  add(({ dur }) => fogShot(dur, (t) => lower(t, t < dur * 0.55 ? 'Fog of war, enforced by the server.' : 'You see only what your badgers see.', null, { at: 0.05 })), 2.0);
  cue(at, 'whoosh');
  add(({ dur }) => ringShot(dur, (t) => lower(t, 'Up to 16 bots. One valley.', null, { at: 0.05 })), 2.0);
  cue(at, 'whoosh');
  add(({ dur, start }) => {
    typeCues(start + 1.6, start + 2.3, 40);
    return {
      dur,
      render(t) {
        show3d(false); show2d(false);
        const scroll = eIO(prog(t, 0.2, 3.5)) * 120;
        const body = codeBlock(DECIDE_CODE, 64, { lineClass: (i) => (i === 16 ? 'add' : '') });
        ui.innerHTML = `<div class="bg"></div>
          <div style="position:absolute;left:110px;top:120px;width:640px">
            <div class="kicker">Starter code included</div>
            <div style="font:700 92px/1 var(--head);margin-top:18px">Don't start from scratch.</div>
            <div style="font:36px/1.4 var(--head);color:#b8c7d8;margin-top:26px">One C# file (or Python) that already plays. Make <span class="codeword skyc">Decide()</span> smarter.</div></div>
          ${win({ x: 830, y: 90, w: 1000, h: 640, tabs: [{ name: 'StarterBot.cs' }], body: `<div style="transform:translateY(${-scroll}px)">${body}</div>`, ry: -6 })}
          ${win({ x: 110, y: 700, w: 1720, h: 300, tabs: [{ name: 'PowerShell', dot: 'b' }], z: 30, body: `<span class="prompt">PS&gt;</span> curl -O …/samples/csharp/StarterBot.cs\n<span class="prompt">PS&gt;</span> ${esc(RUN.slice(0, Math.floor(RUN.length * prog(t, 1.6, 2.3))))}${t > 2.4 ? `\nIn match 1c042d43-…. Watch it at <span class="skyc">${SERVER}/#/match/1c042d43-…</span>` : ''}` })}`;
        ui.querySelectorAll('.win pre')[0].style.fontSize = '24px';
        ui.querySelectorAll('.win pre')[1].style.fontSize = '27px';
      },
    };
  }, 3.5);
  cue(at, 'whoosh');
  add(({ dur }) => ({
    dur,
    render(t) {
      show3d(false); show2d(true);
      map2d(FOUR, 380 + t * 4, { zoom: 1.4, alpha: 0.25 });
      const a = eOut(prog(t, 0.05, 0.4)), b = eOut(prog(t, 0.9, 1.3)), c = eOut(prog(t, 1.8, 2.2));
      ui.innerHTML = `
        <div style="position:absolute;left:150px;top:150px;opacity:${a};transform:translateX(${(1 - a) * -50}px)"><div class="kicker">The server is already up</div><div class="codeword" style="font-size:84px;margin-top:10px;color:#fff">netrts.snowse.io</div></div>
        <div style="position:absolute;left:150px;top:440px;opacity:${b};transform:translateX(${(1 - b) * -50}px)"><div class="kicker">Or run your own</div><div class="codeword" style="font-size:46px;margin-top:14px;color:#d6e2f0">docker run -p 8080:8080 snowcollege/netrts</div></div>
        <div style="position:absolute;left:150px;top:690px;opacity:${c};transform:translateX(${(1 - c) * -50}px)"><div class="kicker">It's open source</div><div style="font:700 70px var(--head);margin-top:12px">Read the server. Find an <span class="orange">edge</span>.</div></div>`;
    },
  }), 3.0);
  cue(at, 'hit');
  add(({ dur }) => rulesCard({ dur }), 4.5);
  cue(at, 'boom');
  add(({ dur }) => ({
    dur,
    render(t) {
      endCard({ dur }).render(t);
      const p = eOut(prog(t, 0.8, 1.3));
      ui.insertAdjacentHTML('beforeend', `<div style="position:absolute;left:0;right:0;top:130px;text-align:center;opacity:${p * (1 - prog(t, dur - 0.6, dur))};font:600 38px var(--head);color:#b8c7d8">Rounds every other week from Oct 22 · pizza at the first one 🍕</div>`);
    },
  }), 3.5);
  return { scenes: S, duration: at };
}

function buildLong() {
  const S = [];
  let at = 0;
  const add = (make, dur, extra = {}) => { const s = make({ dur, start: at, ...extra }); S.push({ start: at, dur, ...s }); at += dur; return s; };

  // A. Cold open.
  add(({ dur, start }) => sceneColdType(['// Every second, the world ticks forward.', '// Your code decides what happens next.'], { dur, start, perLine: 1.6, gap: 0.6, comment: true }), 5.5);
  cue(at, 'boom');
  // B. Hero flyover and title.
  add(({ dur }) => heroFly(dur, 386, 450, (t) => {
    const p = eExpo(prog(t, 1.2, 1.9)), o = 1 - prog(t, dur - 0.4, dur);
    return `<div style="position:absolute;left:0;right:0;top:330px;text-align:center;opacity:${p * o}">
      <div style="font:700 200px/0.95 var(--head);letter-spacing:-0.03em;transform:scale(${1.15 - 0.15 * p});text-shadow:0 0 50px rgba(0,0,0,0.8),0 0 60px rgba(124,192,255,0.4)">BADGER <span class="orange">BRAWL</span></div>
      <div style="font:600 44px var(--head);margin-top:24px;color:#e6eef7;text-shadow:0 2px 20px #000">${words('A real-time strategy game where nobody touches a mouse.', t, { at: 2.4, step: 0.06 })}</div></div>`;
  }), 7.5);
  cue(at, 'whoosh');

  // C. The roster.
  const RT = 205;
  const roster = [
    [DUEL, 64, 66, 'unit', 'Worker', '<span>digs up grubs, builds the campus</span>', { r: 6.5, h: 3.2 }],
    [DUEL, 259, 105, 'unit', 'Soldier', '110 HP · tough, close-up', { r: 6, h: 2.8, deg0: 200, deg1: 240 }],
    [DUEL, 262, 117, 'unit', 'Archer', 'shoots from four tiles away', { r: 6, h: 3, deg0: 250, deg1: 210 }],
    [FOUR, 112, 126, 'unit', 'Scout', '<span>twice as fast · sees furthest</span>', { r: 7, h: 3.6 }],
    [DUEL, RT, 65, 'building', 'Noyes Building', '<span>your HQ · lose it and you\'re out</span>', { r: 9, h: 5.5, label: 'Noyes Building' }],
    [DUEL, RT, 96, 'building', 'Suites at Academy Square', '<span>housing · trains soldiers, archers, scouts</span>', { r: 8, h: 4.6 }],
    [DUEL, RT, 103, 'building', 'GRSC Makerspace', '<span>upgrades are coursework: Organic Chemistry = +damage</span>', { r: 8, h: 4.4 }],
    [DUEL, RT, 102, 'building', 'The Bell Tower', '<span>shoots anything that wanders too close</span>', { r: 9, h: 5 }],
  ];
  roster.forEach(([m, tick, id, kind, l1, l2, o], i) => {
    add(({ dur }) => rosterShot(dur, m, tick, id, kind, l1, l2, { deg0: 10 + i * 40, deg1: 55 + i * 40, ...o }), 1.75);
    cue(at, i === roster.length - 1 ? 'hit' : 'tick2');
  });

  // D. The loop, E. fog of war.
  add(({ dur, start }) => loopDiagram({ dur, start }), 8);
  cue(at, 'whoosh');
  add(({ dur }) => fogShot(dur, (t) => lower(t, t < dur * 0.55 ? 'Fog of war, enforced by the server.' : 'Your bot sees only what its badgers see.', 'Enemy orders are never revealed.', { at: 0.1 })), 4);
  cue(at, 'hit');

  // F. Let's play.
  add(({ dur, start }) => terminal({
    dur, start, script: [
      { at: 0.3, cmd: CURL, typeDur: 0.8 },
      { at: 1.3, out: '<span class="mutedc">  % Total    % Received   Xferd  …  100  12.4k</span>' },
      { at: 1.6, cmd: RUN + ' --opponent rusher', typeDur: 0.9 },
      { at: 2.8, out: `In match 53135863-ed86-4421-b635-c71784bd7d44. Watch it at <span class="skyc">${SERVER}/#/match/53135863-…</span>` },
      { at: 3.0, out: 'Tick 0: 500 ore, 5 units' }, { at: 3.15, out: 'Tick 1: 450 ore, 5 units' }, { at: 3.3, out: 'Tick 2: 450 ore, 5 units' }, { at: 3.45, out: 'Tick 3: 450 ore, 5 units' },
    ], caption: (t) => `<div class="kicker" style="position:absolute;left:160px;top:70px;opacity:${prog(t, 0, 0.4)}">Let's play · two commands</div>`,
  }), 4);
  cue(at, 'whoosh');
  const beforeWorkers = unitsOf(BEFORE, 40, (u) => u.owner === 0 && u.type === 'Worker').slice(0, 3).map((u) => u.id);
  add(({ dur }) => shot3d({
    dur, match: BEFORE, tick: (u) => lerp(36, 52, u),
    cam: (u) => ({ ...orbit(5.5, 5.5, lerp(9, 7, u), lerp(5.5, 4.5, u), lerp(130, 165, u), 0.4), fov: 40 }),
    tags: () => beforeWorkers.map((id) => ({ id })),
    caption: (t) => lower(t, 'The starter bot already plays.', 'Now make it yours.', { at: 0.2 }),
  }), 3.5);
  cue(at, 'whoosh');
  add(({ dur, start }) => typeMob({ dur, start }), 8);
  cue(at, 'whoosh');
  add(({ dur, start }) => terminal({
    dur, start, x: 260, y: 220, w: 1400, h: 600, script: [
      { at: 0.1, out: 'Tick 88: 480 ore, 9 units' },
      { at: 0.3, out: '<span class="mutedc">^C</span>' },
      { at: 0.5, cmd: RUN + ' --opponent rusher', typeDur: 0.5 },
      { at: 1.2, out: `In match 9927ca32-34b4-466f-8134-323d957dbc30. Watch it at <span class="skyc">${SERVER}/#/match/9927ca32-…</span>` },
      { at: 1.4, out: 'Tick 0: 500 ore, 5 units' },
    ], caption: (t) => `<div class="kicker" style="position:absolute;left:260px;top:150px;opacity:${prog(t, 0, 0.3)}">Stop it. Run it again.</div>`,
  }), 2);
  cue(at, 'riser');
  add(({ dur }) => scoutIncoming(dur, (t) => lower(t, 'Here comes a scout…', null, { at: 0.3 })), 3.5);
  cue(at, 'glitch');
  add(({ dur, start }) => codeExec({ dur, start }), 3);
  cue(at, 'boom');
  add(({ dur }) => mobCharge(dur, (t) => bigCenter(t, (tt) => words('EVERYBODY!', tt, { at: 0.05, step: 0 }), { y: 780, cls: 'big orange', out: 2.0 }) + lower(t, '…even the workers.', 'MobAttack sends units = "all"', { at: 2.1 })), 4.5);
  cue(at, 'whoosh');
  add(({ dur }) => shot3d({
    dur, match: AFTER, tick: (u) => lerp(172, 192, u),
    cam: (u, tk) => { const c = centroid(AFTER, WORKERS, tk) || { x: 25, z: 25 }; return { pos: [c.x - 9 + u * 2, lerp(9, 7, u), c.z + 6], look: [c.x + 1.5, 0.4, c.z + 1.5], fov: 38 }; },
    tags: () => [69].map((id) => ({ id })),
    caption: (t) => lower(t, '…all the way across the valley.', 'nobody left at home digging grubs', { at: 0.2 }),
    bars: 70,
  }), 3);
  cue(at, 'riser');
  const raiders = unitsOf(AFTER, 395, (u) => u.owner === 1 && u.x < 20 && u.y < 20).slice(0, 2).map((u) => u.id);
  add(({ dur }) => shot3d({
    dur, match: AFTER, tick: (u) => lerp(384, 412.6, eIn(u) * 0.35 + u * 0.65),
    cam: (u) => ({ ...orbit(7.5, 7.5, lerp(13, 10, u), lerp(6.5, 5, u), lerp(40, 80, u), 1.4), fov: 40 }),
    tags: (tk) => [{ id: HQ0, kind: 'building', label: 'Noyes Building' }, ...raiders.map((id) => ({ id }))],
    caption: (t) => lower(t, 'Meanwhile, back home…', null, { at: 0.2 }),
    bars: 70,
  }), 3.5);
  cue(at, 'boom');
  add(({ dur }) => boxScore({ dur, match: AFTER, title: 'Oops. The Honey Badger wins.', sub: 'Every brawl ends with a box score' }), 3);
  cue(at, 'whoosh');
  add(({ dur, start }) => fixDiff({ dur, start }), 4);
  cue(at, 'whoosh');
  const defenders = myIds(FIXED, 212);
  add(({ dur }) => shot3d({
    dur, match: FIXED, tick: (u) => lerp(204, 227, u),
    cam: (u, tk) => { const e = eIO(u); return { pos: [lerp(4, 5, e), lerp(4.5, 5.5, e), lerp(17, 18, e)], look: [lerp(13, 11.5, e), 0.4, lerp(13, 10.5, e)], fov: 40 }; },
    tags: (tk) => [96, 98].map((id) => ({ id, label: 'Raider', accent: '#ff6b5e' })).concat(defenders.slice(0, 1).map((id) => ({ id }))),
    highlight: () => new Set(defenders),
    caption: (t) => lower(t, 'Run it again.', 'Now they defend home, and only home.', { at: 0.2 }),
    bars: 70,
  }), 5);
  cue(at, 'boom');
  add(({ dur }) => ({
    dur,
    render(t) {
      boxScore({ dur, match: FIXED, title: 'Frank wins. In 404 ticks.', sub: 'Same map · same opponent · one line changed' }).render(t);
      const p = eOut(prog(t, 1.2, 1.7));
      ui.insertAdjacentHTML('beforeend', `<div class="title" style="top:800px;font-size:64px;opacity:${p}">Edit. Run. Watch. <span class="orange">Learn.</span></div>`);
    },
  }), 3.5);
  cue(at, 'hit');

  // G. Starter code, servers, source, rules, schedule.
  add(({ dur, start }) => {
    typeCues(start + 1.2, start + 2.0, 40);
    return {
      dur,
      render(t) {
        show3d(false); show2d(false);
        const a = eOut(prog(t, 0, 0.4));
        const body = codeBlock(DECIDE_CODE, 64, { lineClass: (i) => (i === 16 ? 'add' : '') });
        ui.innerHTML = `<div class="bg"></div>
          <div style="position:absolute;left:110px;top:110px;width:680px;opacity:${a}">
            <div class="kicker">Starter code included</div>
            <div style="font:700 90px/1 var(--head);margin-top:18px">You don't have to start from scratch.</div>
            <div style="font:34px/1.45 var(--head);color:#b8c7d8;margin-top:28px">• One C# file: <span class="codeword skyc">samples/csharp/StarterBot.cs</span><br>• A Python bot, no dependencies<br>• A step-by-step tutorial in both<br>• Live API docs at <span class="codeword skyc">/scalar</span></div></div>
          ${win({ x: 860, y: 90, w: 960, h: 900, tabs: [{ name: 'StarterBot.cs' }, { name: 'bot.py', dot: 'b' }], body, ry: -5 })}`;
        ui.querySelector('.win pre').style.fontSize = '25px';
      },
    };
  }, 5);
  cue(at, 'whoosh');
  add(({ dur }) => ({
    dur,
    render(t) {
      show3d(false); show2d(true);
      map2d(RING, 300 + t * 6, { zoom: 1.05, alpha: 0.22 });
      const a = eOut(prog(t, 0.05, 0.45)), b = eOut(prog(t, 1.5, 1.9)), c = eOut(prog(t, 3.3, 3.7));
      ui.innerHTML = `
        <div style="position:absolute;left:150px;top:110px;opacity:${a};transform:translateX(${(1 - a) * -50}px)"><div class="kicker">A server is already up and running</div><div class="codeword" style="font-size:88px;margin-top:10px;color:#fff">netrts.snowse.io</div></div>
        <div style="position:absolute;left:150px;top:380px;opacity:${b};transform:translateX(${(1 - b) * -50}px)"><div class="kicker">Want your own? Run it locally</div>
          <div class="codeword" style="font-size:40px;margin-top:16px;color:#d6e2f0;line-height:1.6"><span class="prompt">PS&gt;</span> docker run -p 8080:8080 snowcollege/netrts<br><span class="mutedc">or</span>  <span class="prompt">PS&gt;</span> dotnet run --project src/NetRts.Server</div></div>
        <div style="position:absolute;left:150px;top:690px;opacity:${c};transform:translateX(${(1 - c) * -50}px)"><div class="kicker">The server is open source</div>
          <div style="font:700 72px var(--head);margin-top:12px">Read it. Hunt for <span class="orange">"features."</span></div>
          <div style="font:34px var(--head);color:#b8c7d8;margin-top:12px">Every rule lives in <span class="codeword skyc">GameRules.cs</span>. Know them better than your opponent does.</div></div>`;
    },
  }), 6);
  cue(at, 'hit');
  add(({ dur }) => rulesCard({ dur }), 7);
  cue(at, 'whoosh');
  add(({ dur }) => scheduleCard({ dur }), 6.5);
  cue(at, 'boom');
  add(({ dur }) => endCard({ dur }), 5);
  return { scenes: S, duration: at };
}

const { scenes, duration } = CUT === 'long' ? buildLong() : buildShort();

// Global transitions: a white flash on hits, letterbox reset.
function fx(T) {
  let flash = 0;
  for (const c of cues) {
    if (c.type === 'hit' || c.type === 'boom') flash = Math.max(flash, (c.type === 'boom' ? 0.7 : 0.35) * (1 - prog(T, c.t, c.t + 0.35)) * (T >= c.t ? 1 : 0));
    if (c.type === 'glitch' && T >= c.t && T < c.t + 0.2) flash = Math.max(flash, 0.25);
  }
  flashEl.style.opacity = flash;
}

let current = null;
window.seek = (T) => {
  const s = scenes.find((x) => T >= x.start && T < x.start + x.dur) || scenes.at(-1);
  if (s !== current) { barsT.style.height = barsB.style.height = '0px'; current = s; }
  s.render(clamp(T - s.start, 0, s.dur - 1e-4));
  fx(T);
};
window.duration = duration;
window.cues = cues.sort((a, b) => a.t - b.t);
window.cut = CUT;
window.ready = true;

// Live playback: ?cut=long (no render param) plays in real time with the soundtrack if present.
if (!params.has('render')) {
  const stage = $('#stage');
  const fit = () => { const s = Math.min(innerWidth / 1920, innerHeight / 1080); stage.style.transform = `scale(${s})`; stage.style.position = 'absolute'; stage.style.left = `${(innerWidth - 1920 * s) / 2}px`; stage.style.top = `${(innerHeight - 1080 * s) / 2}px`; };
  fit(); addEventListener('resize', fit);
  const t0 = performance.now() - (+params.get('t') || 0) * 1000;
  const loop = () => { const T = (performance.now() - t0) / 1000; window.seek(T % duration); requestAnimationFrame(loop); };
  requestAnimationFrame(loop);
}

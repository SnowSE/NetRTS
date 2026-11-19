// #/pickup/screen: the big screen at the booth (a laptop on a projector). The hill's match in the 3D view,
// moving on to each new match by itself, and beside it: "Got next?" with a QR code for the phone page,
// the champion and their wins, who's playing, and who's next in line. Polls GET /pickup/hill every second.
import { api, esc, mountScope, setTitle, playerNames } from '../util.js';
import { openMatchFeed } from '../matchfeed.js';
import { mount3d } from '../3d/mount.js';
import qrcode from '../vendor/qrcode/qrcode.js';
import { takePage } from './page.js';
import { playersTable, TREATY_S } from './playing.js';
import { matchClock, clockText } from '../matchui.js';

const POLL_MS = 1000;
const LINE_SHOWN = 6;

/** A QR code for text as an SVG (dark modules on white, with the quiet zone). */
export function qrSvg(text) {
  const q = qrcode(0, 'M');
  q.addData(text);
  q.make();
  const n = q.getModuleCount(), pad = 2;
  let d = '';
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) if (q.isDark(y, x)) d += `M${x + pad},${y + pad}h1v1h-1z`;
  return `<svg viewBox="0 0 ${n + pad * 2} ${n + pad * 2}" shape-rendering="crispEdges" role="img" aria-label="QR code for ${esc(text)}"><rect width="100%" height="100%" fill="#fff"/><path d="${d}" fill="#0f1d2e"/></svg>`;
}

export function mountScreen(main) {
  const scope = mountScope();
  const { later } = scope;
  scope.cleanup(takePage(main, { full: true }));
  setTitle('Big screen', 'Pick-up Game');
  const url = `${location.origin}/#/pickup`;
  main.innerHTML = `<div class="pks">
      <div class="pks-stage">
        <div class="map3d pks-3d" id="pks-3d"></div>
        <div class="pks-msg" id="pks-msg"><p>Waiting for the first match…</p></div>
        <div class="pks-banner" id="pks-banner" hidden></div>
        <div class="pks-clock" id="pks-clock" hidden></div>
      </div>
      <aside class="pks-side">
        <section class="pks-got">
          <h2>Got next?</h2>
          <p>Scan, build a bot on your phone, and take on the champion.</p>
          <div class="pks-qr">${qrSvg(url)}</div>
          <p class="pks-url">${esc(url.replace(/^https?:\/\//, ''))}</p>
        </section>
        <section class="pks-champ" id="pks-champ" aria-live="polite"></section>
        <section class="pks-playing" id="pks-playing"></section>
        <section class="pks-line" id="pks-line"></section>
      </aside>
    </div>`;
  const $ = (id) => main.querySelector(`#${id}`);

  let hill = null;
  let shown = null;    // the match in the 3D view: { matchId, view, feed, state }
  let starting = null; // the match whose view is being built

  function setMsg(text) {
    $('pks-msg').hidden = !text;
    $('pks-msg').innerHTML = text ? `<p>${esc(text)}</p>` : '';
  }

  function drop() {
    shown?.feed?.close();
    shown?.view?.dispose();
    shown = null;
    $('pks-clock').hidden = true;
  }

  /** How long the match on screen has left (it can end sooner, when a base falls), and the treaty while it holds. */
  function paintClock(state) {
    const el = $('pks-clock');
    if (!state) { el.hidden = true; return; }
    const { pct, msLeft, tick } = matchClock({ state });
    const treatyLeft = TREATY_S * 1000 - tick * (state.tickIntervalMs || 250);
    el.hidden = false;
    el.innerHTML = state.outcome
      ? '<b>Final</b><small>the next match starts soon</small>'
      : hill?.warmup
        ? '<b>Warm-up</b><small>the house plays until someone is ready · scan to play</small>'
      : `<b>${clockText(msLeft)}</b><small>${treatyLeft > 0 ? `left · fighting starts in ${clockText(treatyLeft)}` : 'left in this match'}</small>
         <span class="pks-bar"><span style="width:${pct.toFixed(1)}%"></span></span>`;
  }
  scope.cleanup(drop);

  /** The 3D view and the feed for matchId, in place of the match before. */
  async function show(matchId) {
    starting = matchId;
    drop();
    setMsg('Building the 3D valley…');
    let v;
    try {
      const cur = { matchId, view: null, feed: null, state: null };
      v = await mount3d($('pks-3d'), {
        scope,
        names: () => playerNames(null, cur.state),
        onReady: () => {
          setMsg('');
          // Farther out and slower than the match page's director, so the whole fight (and both bases on a
          // small pick-up map) stays in view from across the booth.
          const view = $('pks-3d').view3d;   // mount3d puts it on its box; v may not be set yet
          Object.assign(view.rig.director, { near: 13, far: 20, spread: 1.6, pitch: 0.7, pitchSpan: 0.2, yawSpeed: 0.035, cutEvery: 12 });
          view.setMode('director');
        },
      });
      if (starting !== matchId) { v.dispose(); return; }
      cur.view = v;
      shown = cur;
      cur.feed = openMatchFeed(matchId, {
        onMap: (map) => v.view3d.setMap(map),
        onState: (s) => { cur.state = s; v.view3d.setState(s); if (shown === cur) paintClock(s); },
        onGone: () => { if (shown === cur) setMsg('That match is over. The next one starts soon.'); },
      });
    } catch (e) {
      setMsg(`The 3D view couldn't start here (${e?.message || 'no WebGL'}).`);
    }
  }

  function render() {
    const c = hill.champion;
    $('pks-champ').innerHTML = c
      ? `<h3>Champion</h3><p class="pks-champ-name"><span class="crown">♛</span> ${esc(c.name)}${c.house ? ' <span class="house">house</span>' : ''}</p>
         <p class="pks-wins">${hill.championWins} win${hill.championWins === 1 ? '' : 's'}${c.house ? '' : ' · retires at 5'}</p>`
      : '<h3>Champion</h3><p class="pks-champ-name">Nobody yet</p>';
    $('pks-playing').innerHTML = hill.playing?.length ? `<h3>Playing now</h3><div class="hudp hud-dark">${playersTable(hill.playing, ['fighters', 'buildings', 'score'])}</div>` : '';
    const line = hill.line || [];
    $('pks-line').innerHTML = `<h3>Next in line</h3>${line.length
      ? `<ol>${line.slice(0, LINE_SHOWN).map((n) => `<li>${esc(n)}</li>`).join('')}</ol>${line.length > LINE_SHOWN ? `<p class="pks-more">+${line.length - LINE_SHOWN} more</p>` : ''}`
      : '<p class="pks-more">Nobody yet. Scan the code and you\'re next!</p>'}`;
    const banner = $('pks-banner');
    const counting = hill.phase === 'countdown' && hill.secondsLeft != null;
    banner.hidden = !counting;
    if (counting) banner.textContent = `Next match in ${hill.secondsLeft}…`;
  }

  async function poll() {
    try {
      hill = await scope.wait(api('/api/v1/pickup/hill'));
      render();
      if (hill.matchId && hill.matchId !== starting) show(hill.matchId);
      if (!hill.matchId && !shown) setMsg('The next match starts soon.');
    } catch (e) {
      if (!shown) setMsg(`Can't reach the hill: ${e.message}`);
    }
    later(poll, POLL_MS);
  }
  poll();

  return () => scope.dispose();
}

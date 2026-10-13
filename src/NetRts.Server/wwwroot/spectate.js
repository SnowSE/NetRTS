// Spectate view: #/match/{id} or #/match/{name}
import { api, html, setHtml, esc, slotVar, displayName, playerLabel, fmt, outcomeText } from './util.js';
import { GAME, SNOW, say, ORE, unitName, buildingName, buildingRole, upgradeName, badgerize, hqName, housingName } from './lore.js';
import { MapView, unitPath } from './mapview.js';

const HOT = /Lost|Destroyed|Killed|Eliminated|Failed/;

export function mountSpectate(root, matchKey) {
  const byName = !/^[0-9a-fA-F-]{36}$/.test(matchKey);
  let matchId = byName ? null : matchKey;
  document.title = `${say('Match', 'Brawl')} – ${GAME}`;
  setHtml(root, html`
    <div id="banner-slot" aria-live="polite"></div>
    <div class="match-head">
      <div>
        <p class="small" style="margin:0 0 6px"><a href="#/" id="back-link">${say('All matches', 'All brawls')}</a></p>
        <div class="title-row">
          <h1 id="title">${say('Loading match…', 'Waking the badgers…')}</h1>
          <span class="matchup" id="matchup" hidden></span>
        </div>
        <p class="sub" id="subtitle"></p>
      </div>
      <div class="clock-col">
        <div class="clock" id="clock" aria-live="off"></div>
        <div class="speed" id="speed" role="group" aria-label="Match speed" hidden></div>
      </div>
    </div>
    <div class="arena">
      <div class="map-col">
        <div class="map-toolbar">
          <fieldset class="field" id="vision-field" hidden>
            <legend class="visually-hidden">Vision</legend>
            <span class="small muted" aria-hidden="true">Vision</span>
            <div class="segmented" id="vision"></div>
          </fieldset>
          <ul class="legend" aria-label="Map key">${legendItems()}</ul>
        </div>
        <div class="replay-bar" id="replay" role="group" aria-label="Replay" hidden></div>
        <div class="map-frame" id="frame">
          <canvas id="map" tabindex="0" role="img" aria-label="${say('Battle', 'Brawl')} map. Use the arrow keys to inspect tiles, plus and minus to zoom."
            aria-describedby="tooltip"></canvas>
          <div class="map3d" id="map3d" hidden></div>
          <div class="map3d-card" id="card3d" hidden aria-live="polite"></div>
          <div class="map-controls" role="toolbar" aria-label="Map view">
            <button type="button" id="view3d" aria-pressed="false" title="Fly around the battlefield in 3D (3)">3D</button>
            <button type="button" id="zoom-in" aria-label="Zoom in" title="Zoom in (+)">+</button>
            <button type="button" id="zoom-out" aria-label="Zoom out" title="Zoom out (−)">−</button>
            <button type="button" id="zoom-fit" title="Show the whole map (0)">Fit</button>
            <button type="button" id="follow" aria-pressed="false" title="Keep the camera on the biggest fight (F)">${say('Follow the fight', 'Follow the brawl')}</button>
            <button type="button" id="fly" aria-pressed="false" title="Free flight: mouse to look, WASD to move, E/Q up and down, Shift for speed, Esc to land" hidden>Fly</button>
            <button type="button" id="grid3d" aria-pressed="false" title="Show the tile grid" hidden>Grid</button>
            <button type="button" id="quality3d" title="Graphics quality" hidden>Quality</button>
            <button type="button" id="sound3d" aria-expanded="false" aria-controls="sound-panel" title="Music, sound effects and voices" hidden>Sound</button>
            <span class="zoom-level" id="zoom-level" aria-live="polite">1×</span>
            <button type="button" id="fullscreen" aria-pressed="false" title="Full screen (G)">Full screen</button>
          </div>
          <section class="fs-panel fs-left" aria-label="Scoreboard">
            <button type="button" class="fs-toggle" id="board-toggle" aria-expanded="true" aria-controls="board-body">
              <span>Scoreboard</span><span class="fs-sub" id="board-tick"></span><span aria-hidden="true" class="chev">▾</span>
            </button>
            <div class="fs-body" id="board-body">
              <table class="fs-board">
                <thead><tr>
                  <th scope="col"><span class="visually-hidden">Rank</span></th><th scope="col">Player</th><th scope="col" class="num">Score</th>
                  <th scope="col" class="num x">${ORE}</th><th scope="col" class="num x">${say('Units', 'Badgers')}</th>
                  <th scope="col" class="num x">Bldgs</th><th scope="col" class="num xx">/min</th>
                </tr></thead>
                <tbody id="board-list"></tbody>
              </table>
            </div>
          </section>
          <section class="fs-panel fs-right" aria-label="${say('Match', 'Brawl')}">
            <button type="button" class="fs-toggle" id="info-toggle" aria-expanded="true" aria-controls="info-body">
              <span id="fs-title">${say('Match', 'Brawl')}</span><span aria-hidden="true" class="chev">▾</span>
            </button>
            <div class="fs-body" id="info-body">
              <p class="fs-matchup" id="fs-matchup"></p>
              <div class="speed" id="fs-speed" role="group" aria-label="Match speed" hidden></div>
              <div class="replay-bar compact" id="fs-replay" role="group" aria-label="Replay" hidden></div>
              <h3 class="fs-h">${say('Battle log', 'Play-by-play')}</h3>
              <ol class="fs-feed" id="fs-feed"></ol>
            </div>
          </section>
          <div class="sound-panel" id="sound-panel" role="group" aria-label="Sound" hidden>
            <label class="sound-row sound-mute"><input type="checkbox" id="snd-on"> Sound on</label>
            <label class="sound-row" for="snd-music">Music <output id="snd-music-v"></output></label>
            <input type="range" id="snd-music" min="0" max="100" step="5">
            <label class="sound-row" for="snd-sfx">Sound effects <output id="snd-sfx-v"></output></label>
            <input type="range" id="snd-sfx" min="0" max="100" step="5">
            <label class="sound-row" for="snd-voice">Voices <output id="snd-voice-v"></output></label>
            <input type="range" id="snd-voice" min="0" max="100" step="5">
            <label class="sound-row sound-mute"><input type="checkbox" id="snd-captions"> Show what players say</label>
            <p class="small muted sound-note">Effects are only heard for what's on screen, quieter further away. Each player has their own voice.
              Voices: <a href="https://huggingface.co/hexgrad/Kokoro-82M" target="_blank" rel="noopener">Kokoro</a> through LocalAI,
              and <a href="https://github.com/OHF-Voice/piper1-gpl" target="_blank" rel="noopener">Piper</a> with the
              <a href="https://datashare.ed.ac.uk/handle/10283/3443" target="_blank" rel="noopener">VCTK corpus</a> (CC BY 4.0).</p>
          </div>
          <div class="tooltip" id="tooltip" role="status" hidden></div>
          <div class="overlay-msg" id="overlay"><div><strong>${say('Loading map…', 'Unrolling the Sanpete Valley…')}</strong></div></div>
          <div class="gameover" id="gameover" role="dialog" aria-labelledby="go-title" hidden></div>
        </div>
        <p class="small muted" style="margin:0" id="map-help">${say('Scroll or pinch to zoom in on a battle,', 'Scroll or pinch to zoom in on a brawl,')} drag to look around, double-click to zoom. Hover a tile to inspect it, or focus the map and use the arrow keys.</p>
        <p class="small muted" style="margin:0" id="map-help-3d" hidden>Drag to pan, right-drag or Shift-drag to turn, scroll to zoom. WASD moves, Q and E turn. Click a ${say('unit', 'badger')} to ride along with it, or press Fly for free flight (Esc to land). On a touch screen, drag to pan, pinch to zoom and twist to turn.</p>
      </div>
      <aside class="side" aria-label="Players and events">
        <section aria-labelledby="players-h">
          <h2 id="players-h" class="visually-hidden">Players</h2>
          <div id="players" style="display:grid;gap:10px"></div>
        </section>
        <section class="feed" aria-labelledby="feed-h">
          <h2 id="feed-h">${say('Battle log', 'Play-by-play')}</h2>
          <ol id="feed" aria-live="off"><li class="muted"><span></span><span>${say('Waiting for the first tick…', 'Waiting for the opening whistle…')}</span></li></ol>
        </section>
      </aside>
    </div>`);

  const $ = (id) => root.querySelector('#' + id);
  const canvas = $('map'), frame = $('frame'), overlay = $('overlay'), tooltip = $('tooltip');
  const view = new MapView(canvas);
  const box3d = $('map3d');
  let view3d = null;       // the 3D view, loaded the first time someone opens it
  let is3d = false;
  let anchor3d = null;     // where the pointer last hovered in 3D, for refreshing the tooltip
  const showMap = (map) => { view.setMap(map); view3d?.setMap(map); };
  const showState = (s) => { view.setState(s); view3d?.setState(s); syncPause(); };
  /** The 3D picture stands still while the match or the replay is paused. */
  const syncPause = () => view3d?.setPaused(replay ? !replay.playing : !!state?.paused);

  let alive = true;
  let es = null;
  let timers = [];
  let summary = null;
  let state = null;
  let result = null;
  let finished = false;
  let visionSlots = '';
  let replay = null;       // playing back recorded frames (see startReplay)
  let replayNote = '';     // why there is no replay, when there isn't one
  const events = [];
  const eventKeys = new Set();

  const later = (fn, ms) => { const t = setTimeout(() => alive && fn(), ms); timers.push(t); };
  const names = () => {
    const m = new Map();
    (summary?.players || []).forEach((p) => m.set(p.slot, displayName(p.name)));
    (state?.players || []).forEach((p) => m.set(p.slot, displayName(p.name)));
    return m;
  };

  // ---------- layout
  const fit = () => {
    const cs = getComputedStyle(frame);
    const padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
    const w = frame.clientWidth - padX;
    let h;
    if (frame.classList.contains('is-full')) {
      h = frame.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
    } else {
      const top = frame.getBoundingClientRect().top + window.scrollY;
      // Show the whole map without scrolling where possible; phones fall back to width.
      h = window.innerWidth < 760 ? w : Math.max(300, window.innerHeight - top - 40);
    }
    view.fit(w, h);
    // The 3D view fills the same space, but never taller than wide on a phone.
    box3d.style.width = `${w}px`;
    box3d.style.height = `${Math.round(window.innerWidth < 760 && !frame.classList.contains('is-full') ? w * 0.8 : h)}px`;
  };
  const ro = new ResizeObserver(fit);
  ro.observe(frame);
  window.addEventListener('resize', fit);
  const scheme = window.matchMedia('(prefers-color-scheme: dark)');
  const onScheme = () => view.readTheme();
  scheme.addEventListener('change', onScheme);
  window.addEventListener('netrts-theme', onScheme);

  // ---------- tooltip / inspection
  function inspect(tile, anchor, reveal = false) {
    view.setCursor(tile, reveal);
    if (!tile) { tooltip.hidden = true; return; }
    const info = view.entitiesAt(tile.x, tile.y);
    const who = names();
    const owner = (slot) => html`<span class="pname" style="--pc:${slotVar(slot)}">${who.get(slot) ?? `Player ${slot + 1}`}</span>`;
    const items = [];
    for (const b of info.buildings) {
      items.push(html`<div class="tt-item"><b>${buildingName(b.type, b.owner)}</b> #${b.id} ${owner(b.owner)}
        <span class="tt-role">${buildingRole(b.type)}</span>
        HP ${fmt(b.hp)} / ${fmt(b.maxHp)}${b.completed ? '' : html`<br>Under construction, ${b.constructionPercent}%`}
        ${b.production?.length ? html`<br>${say('Producing', 'Training')} ${b.production.map((p) => `${unitName(p.unitType)} ${p.percent}%`).join(', ')}` : ''}
        ${b.research ? html`<br>${say('Researching', 'Studying')} ${upgradeName(b.research.upgrade)} ${b.research.percent}%` : ''}</div>`);
    }
    for (const u of info.units) {
      items.push(html`<div class="tt-item"><b>${unitName(u.type)}</b> #${u.id} ${owner(u.owner)}<br>
        HP ${fmt(u.hp)} / ${fmt(u.maxHp)}, ${u.activity.toLowerCase()}
        ${u.carrying ? html`<br>Carrying ${u.carrying} ${ORE}` : ''}
        ${u.targetId != null ? html`<br>Target #${u.targetId}` : ''}
        ${u.destination ? html`<br>Heading to ${u.destination.x}, ${u.destination.y}` : ''}</div>`);
    }
    for (const r of info.resources) {
      items.push(html`<div class="tt-item"><b>${say('Ore deposit', 'Grub patch')}</b> #${r.id}<br>${fmt(r.remaining)} ${ORE} left</div>`);
    }
    const fog = info.visible === false ? html`, hidden from ${who.get(view.vision)}` : '';
    setHtml(tooltip, html`<div class="tt-coord">${tile.x}, ${tile.y}: ${SNOW ? (info.terrain === 'Rock' ? 'Wasatch Plateau rock' : 'Sanpete Valley') : info.terrain}${fog}</div>${items}`);
    tooltip.hidden = false;
    const pt = anchor || view.tileCenter(tile);
    const fr = frame.getBoundingClientRect(), cr = (is3d ? box3d : canvas).getBoundingClientRect();
    let left = cr.left - fr.left + pt.x + 14;
    let top = cr.top - fr.top + pt.y + 14;
    const tw = tooltip.offsetWidth, th = tooltip.offsetHeight;
    if (left + tw > frame.clientWidth - 4) left = Math.max(4, cr.left - fr.left + pt.x - tw - 14);
    if (top + th > frame.clientHeight - 4) top = Math.max(4, cr.top - fr.top + pt.y - th - 14);
    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${top}px`;
  }

  // ---------- zoom & pan
  const zoomLabel = $('zoom-level'), followBtn = $('follow');
  view.onCameraChange = () => {
    if (is3d) return;
    zoomLabel.textContent = `${view.zoom >= 10 ? Math.round(view.zoom) : Math.round(view.zoom * 10) / 10}×`;
    followBtn.setAttribute('aria-pressed', String(view.follow));
    canvas.style.cursor = view.zoom > 1 ? 'grab' : 'crosshair';
    canvas.style.touchAction = view.zoom > 1 ? 'none' : 'pan-y';
    queueMicrotask(() => refreshTooltip()); // defined further down
  };
  view.onCameraChange(view);
  $('zoom-in').addEventListener('click', () => (is3d ? view3d.zoom(1 / 1.4) : view.zoomCentre(1.5)));
  $('zoom-out').addEventListener('click', () => (is3d ? view3d.zoom(1.4) : view.zoomCentre(1 / 1.5)));
  $('zoom-fit').addEventListener('click', () => (is3d ? view3d.overview() : view.resetView()));
  followBtn.addEventListener('click', () => {
    if (is3d) view3d.setMode(view3d.mode === 'director' ? 'orbit' : 'director');
    else view.setFollow(!view.follow);
  });

  // Full screen: the map frame fills the screen with a collapsible scoreboard. Uses the Fullscreen API
  // where there is one, and a fixed overlay where there isn't (iPhone Safari).
  const fullBtn = $('fullscreen'), boardToggle = $('board-toggle');
  const isFull = () => frame.classList.contains('is-full');
  const setFull = (on) => {
    frame.classList.toggle('is-full', on);
    document.documentElement.classList.toggle('map-full', on);
    fullBtn.setAttribute('aria-pressed', String(on));
    fullBtn.textContent = on ? 'Exit full screen' : 'Full screen';
    fit();
  };
  const toggleFull = async () => {
    if (!isFull()) {
      if (frame.requestFullscreen) {
        try { await frame.requestFullscreen(); return; } catch { /* refused: fall back to the overlay */ }
      }
      setFull(true);
    } else if (document.fullscreenElement) {
      await document.exitFullscreen();
    } else {
      setFull(false);
    }
  };
  const onFullscreenChange = () => setFull(document.fullscreenElement === frame);
  document.addEventListener('fullscreenchange', onFullscreenChange);
  fullBtn.addEventListener('click', toggleFull);
  // Each full-screen panel collapses to its title bar, and the choice is remembered on this device.
  const setPanel = (toggle, open) => {
    toggle.setAttribute('aria-expanded', String(open));
    $(toggle.getAttribute('aria-controls')).hidden = !open;
  };
  for (const toggle of [boardToggle, $('info-toggle')]) {
    const key = `netrts.fs.${toggle.id}`;
    try { if (localStorage.getItem(key) === 'closed') setPanel(toggle, false); } catch { /* storage blocked */ }
    toggle.addEventListener('click', () => {
      const open = toggle.getAttribute('aria-expanded') !== 'true';
      setPanel(toggle, open);
      try { localStorage.setItem(key, open ? 'open' : 'closed'); } catch { /* storage blocked */ }
    });
  }

  canvas.addEventListener('wheel', (e) => {
    // At full view, scrolling down keeps scrolling the page; otherwise the wheel zooms the map.
    if (view.zoom <= 1 && e.deltaY > 0) return;
    e.preventDefault();
    view.zoomAt(Math.exp(-e.deltaY * (e.deltaMode === 1 ? 0.05 : 0.0018)), e.offsetX, e.offsetY);
  }, { passive: false });
  canvas.addEventListener('dblclick', (e) => view.zoomTo(2, e.offsetX, e.offsetY));

  // One pointer drags the view; two pinch-zoom around their midpoint.
  const pointers = new Map();
  let dragged = false, pinch = null;
  const local = (e) => { const r = canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  canvas.addEventListener('pointerdown', (e) => {
    pointers.set(e.pointerId, local(e));
    dragged = false;
    if (pointers.size === 2) {
      const [a, b] = [...pointers.values()];
      pinch = { dist: Math.hypot(a.x - b.x, a.y - b.y) };
    }
    if (view.zoom > 1 || pointers.size === 2) canvas.setPointerCapture(e.pointerId);
  });
  canvas.addEventListener('pointermove', (e) => {
    const prev = pointers.get(e.pointerId);
    if (!prev) return;
    const p = local(e);
    pointers.set(e.pointerId, p);
    if (pointers.size === 2 && pinch) {
      const [a, b] = [...pointers.values()];
      const dist = Math.hypot(a.x - b.x, a.y - b.y);
      if (pinch.dist > 0) view.zoomAt(dist / pinch.dist, (a.x + b.x) / 2, (a.y + b.y) / 2);
      pinch.dist = dist;
      dragged = true;
    } else if (pointers.size === 1 && view.zoom > 1) {
      if (!dragged && Math.hypot(p.x - prev.x, p.y - prev.y) < 3) return;
      dragged = true;
      canvas.style.cursor = 'grabbing';
      inspect(null);
      view.panBy(p.x - prev.x, p.y - prev.y);
    }
  });
  const release = (e) => {
    pointers.delete(e.pointerId);
    if (pointers.size < 2) pinch = null;
    if (pointers.size === 0) canvas.style.cursor = view.zoom > 1 ? 'grab' : 'crosshair';
  };
  canvas.addEventListener('pointerup', release);
  canvas.addEventListener('pointercancel', release);

  canvas.addEventListener('mousemove', (e) => {
    if (pointers.size && dragged) return;
    inspect(view.tileAt(e.offsetX, e.offsetY), { x: e.offsetX, y: e.offsetY });
  });
  canvas.addEventListener('mouseleave', () => inspect(null));
  canvas.addEventListener('blur', () => inspect(null));
  canvas.addEventListener('focus', () => {
    if (view.map) inspect(view.cursor || { x: view.map.width >> 1, y: view.map.height >> 1 });
  });
  canvas.addEventListener('keydown', (e) => {
    if (!view.map) return;
    const d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
    if (e.key === 'Escape' && !$('gameover').hidden) { hideGameOver(); return; }
    if (e.key === 'Escape') { inspect(null); if (isFull() && !document.fullscreenElement) setFull(false); return; }
    if (e.key === '+' || e.key === '=') { view.zoomCentre(1.5); return; }
    if (e.key === '-' || e.key === '_') { view.zoomCentre(1 / 1.5); return; }
    if (e.key === '0') { view.resetView(); return; }
    if (e.key === 'f' || e.key === 'F') { view.setFollow(!view.follow); return; }
    if (e.key === 'g' || e.key === 'G') { toggleFull(); return; }
    if (!d) return;
    e.preventDefault();
    const step = e.shiftKey ? 8 : 1;
    const c = view.cursor || { x: view.map.width >> 1, y: view.map.height >> 1 };
    inspect({
      x: Math.max(0, Math.min(view.map.width - 1, c.x + d[0] * step)),
      y: Math.max(0, Math.min(view.map.height - 1, c.y + d[1] * step)),
    }, null, true);
  });
  const refreshTooltip = () => {
    if (tooltip.hidden || !view.cursor) return;
    if (is3d) { if (anchor3d) inspect(view.cursor, anchor3d); return; }
    inspect(view.cursor, null);
  };

  // ---------- 3D view
  const btn3d = $('view3d'), flyBtn = $('fly'), gridBtn = $('grid3d'), qualityBtn = $('quality3d');
  const QUALITY_NAMES = { low: 'Low', medium: 'Medium', high: 'High' };
  const MODE_NAMES = { orbit: 'Orbit', fly: 'Flying', follow: 'Riding along', director: 'Director' };
  let cardKey = '';
  function render3dControls() {
    btn3d.setAttribute('aria-pressed', String(is3d));
    for (const b of [flyBtn, gridBtn, qualityBtn, soundBtn]) b.hidden = !is3d;
    if (!is3d) showSoundPanel(false);
    for (const id of ['zoom-in', 'zoom-out', 'zoom-fit', 'follow']) $(id).hidden = false;
    $('map-help').hidden = is3d;
    $('map-help-3d').hidden = !is3d;
    if (!is3d) { view.onCameraChange(view); return; }
    const mode = view3d?.mode ?? 'orbit';
    followBtn.setAttribute('aria-pressed', String(mode === 'director'));
    flyBtn.setAttribute('aria-pressed', String(mode === 'fly'));
    gridBtn.setAttribute('aria-pressed', String(!!view3d?.grid));
    qualityBtn.textContent = `Quality: ${QUALITY_NAMES[view3d?.quality] ?? 'Medium'}`;
    zoomLabel.textContent = MODE_NAMES[mode] ?? '';
  }
  function renderCard(u) {
    const card = $('card3d');
    if (!u || !is3d) { card.hidden = true; cardKey = ''; return; }
    const key = `${u.id}|${u.hp}|${u.activity}|${u.carrying}|${u.owner}`;
    if (key === cardKey) return;
    cardKey = key;
    const who = names().get(u.owner) ?? `Player ${u.owner + 1}`;
    const pct = Math.max(0, Math.min(100, (u.hp / u.maxHp) * 100));
    setHtml(card, html`
      <div class="card3d-top"><b>${unitName(u.type)}</b> <span class="muted">#${u.id}</span></div>
      <div class="pname" style="--pc:${slotVar(u.owner)}">${who}</div>
      <div class="card3d-hp" role="img" aria-label="Health ${u.hp} of ${u.maxHp}"><span style="width:${pct.toFixed(1)}%"></span></div>
      <div class="small">HP ${fmt(u.hp)} / ${fmt(u.maxHp)} · ${u.activity.toLowerCase()}${u.carrying ? ` · carrying ${u.carrying} ${ORE}` : ''}</div>
      <div class="small muted">Esc or drag to let go</div>`);
    card.hidden = false;
  }
  async function set3d(on) {
    if (on && !view3d) {
      try {
        const { View3D } = await import('./3d/view.js');
        if (!alive) return;
        const { GameAudio } = await import('./3d/audio.js');
        audio ??= new GameAudio();
        view3d = new View3D(box3d, { badger: SNOW, names, audio });
        box3d.view3d = view3d;   // handy from the dev tools console
      } catch (e) {
        setOverlay('The 3D view could not start', e?.message || 'This browser may not support WebGL.');
        later(() => setOverlay(null), 3500);
        return;
      }
      view3d.onModeChange = () => render3dControls();
      view3d.onQualityChange = () => render3dControls();
      view3d.onFollowChange = (u) => renderCard(u);
      view3d.onHoverTile = (tile, at) => {
        anchor3d = at || null;
        inspect(tile, at);
      };
      if (view.map) view3d.setMap(view.map);
      if (view.state) view3d.setState(view.state);
      syncPause();
      view3d.setVision(view.vision);
    }
    is3d = on;
    audio?.setActive(on);
    renderSound();
    frame.classList.toggle('is-3d', on);
    canvas.hidden = on;
    box3d.hidden = !on;
    inspect(null);
    if (!on) renderCard(null);
    try { localStorage.setItem('netrts.view', on ? '3d' : '2d'); } catch { /* storage blocked */ }
    render3dControls();
    fit();
    if (on) view3d.renderer.domElement.focus({ preventScroll: true });
  }
  btn3d.addEventListener('click', () => set3d(!is3d));

  // ---------- sound (3D view only): music, effects and voices, each with its own volume
  let audio = null;
  const soundBtn = $('sound3d'), soundPanel = $('sound-panel');
  const SOUND_KEYS = ['music', 'sfx', 'voice'];
  function renderSound() {
    if (!audio) return;
    const st = audio.settings;
    $('snd-on').checked = !st.muted;
    $('snd-captions').checked = st.captions;
    for (const k of SOUND_KEYS) {
      $(`snd-${k}`).value = String(Math.round(st[k] * 100));
      $(`snd-${k}-v`).textContent = `${Math.round(st[k] * 100)}%`;
    }
    soundBtn.textContent = st.muted ? 'Sound: off' : 'Sound';
    soundBtn.setAttribute('aria-pressed', String(!st.muted));
  }
  function showSoundPanel(on) {
    soundPanel.hidden = !on;
    soundBtn.setAttribute('aria-expanded', String(on));
    if (on) renderSound();
  }
  soundBtn.addEventListener('click', (e) => { e.stopPropagation(); audio?.unlock(); showSoundPanel(soundPanel.hidden); });
  soundPanel.addEventListener('input', (e) => {
    if (!audio) return;
    const k = SOUND_KEYS.find((x) => e.target.id === `snd-${x}`);
    if (k) { audio.set(k, Number(e.target.value) / 100); if (audio.settings.muted && audio.settings[k] > 0) audio.set('muted', false); }
    if (e.target.id === 'snd-on') audio.set('muted', !e.target.checked);
    if (e.target.id === 'snd-captions') audio.set('captions', e.target.checked);
    audio.unlock();
    renderSound();
  });
  soundPanel.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); showSoundPanel(false); soundBtn.focus(); } });
  const onDocDown = (e) => {
    audio?.unlock();   // browsers only start sound once the viewer clicks or presses a key
    if (!soundPanel.hidden && !soundPanel.contains(e.target) && e.target !== soundBtn) showSoundPanel(false);
  };
  document.addEventListener('pointerdown', onDocDown);
  document.addEventListener('keydown', onDocDown);
  flyBtn.addEventListener('click', () => view3d?.setMode(view3d.mode === 'fly' ? 'orbit' : 'fly'));
  gridBtn.addEventListener('click', () => { view3d?.setGrid(!view3d.grid); render3dControls(); });
  qualityBtn.addEventListener('click', () => {
    if (!view3d) return;
    const order = ['low', 'medium', 'high'];
    view3d.setQuality(order[(order.indexOf(view3d.quality) + 1) % order.length]);
    render3dControls();
  });
  box3d.addEventListener('keydown', (e) => {
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key === 'Escape') {
      inspect(null);
      if (view3d && view3d.mode !== 'orbit' && view3d.mode !== 'fly') view3d.setMode('orbit');
      else if (isFull() && !document.fullscreenElement) setFull(false);
      return;
    }
    if (e.key === 'g' || e.key === 'G') { toggleFull(); return; }
    if (e.key === 'f' || e.key === 'F') { view3d?.setMode(view3d.mode === 'director' ? 'orbit' : 'director'); return; }
    if (e.key === '3') { set3d(false); return; }
    if (e.key === '0') { view3d?.overview(); return; }
    if (e.key === '+' || e.key === '=') { view3d?.zoom(1 / 1.4); return; }
    if (e.key === '-' || e.key === '_') { view3d?.zoom(1.4); return; }
    const act = { ' ': 'play', ',': 'back', '.': 'fwd', Home: 'start', End: 'end' }[e.key];
    if (act && replay) { e.preventDefault(); replayAct(act); }
  });
  canvas.addEventListener('keydown', (e) => { if (e.key === '3' && !e.ctrlKey && !e.metaKey) set3d(true); });

  // ---------- rendering pieces
  function renderHeader() {
    const players = [...(state?.players || summary?.players || [])].sort((a, b) => a.slot - b.slot);
    const seats = summary?.maxPlayers ?? players.length;
    const matchup = !players.length ? say('Waiting for players', 'Waiting for badgers')
      : seats > 4 ? `${seats}-player ${say('match', 'brawl')}`   // the scoreboard lists everyone
        : players.map((p) => displayName(p.name)).join(' vs ');
    // A heat links back to its league match, where every heat's scoreboard is.
    if (summary?.parentMatchId) {
      $('back-link').href = `#/match/${summary.parentMatchId}`;
      $('back-link').textContent = `${(summary.name || '').replace(/-h\d+$/, '')}: all heats`;
    }
    // A named match shows its name, with the matchup beside it.
    const title = summary?.name || matchup;
    $('title').textContent = title;
    $('matchup').textContent = summary?.name ? matchup : '';
    $('matchup').hidden = !summary?.name;
    $('fs-title').textContent = title;
    $('fs-matchup').textContent = summary?.name ? matchup : '';
    document.title = `${title} – ${GAME}`;
    if (summary) {
      $('subtitle').textContent = `${summary.mapWidth} × ${summary.mapHeight} map, seed ${summary.seed}, ${state?.tickIntervalMs || summary.tickIntervalMs} ms per tick, ${say('match', 'brawl')} ${summary.matchId.slice(0, 8)}`;
    }
  }

  function renderClock() {
    const tick = state?.tick ?? summary?.tick ?? 0;
    const max = state?.maxTicks ?? summary?.maxTicks ?? 0;
    const status = replay ? 'Replay' : finished ? 'Completed' : (state?.status ?? summary?.status ?? 'Waiting');
    const paused = status === 'Active' && (state?.paused ?? summary?.paused);
    const pct = max ? Math.min(100, (tick / max) * 100) : 0;
    setHtml($('clock'), html`
      <div class="clock-top">
        <span class="status ${paused ? 'Paused' : status}">${paused ? 'Paused' : status === 'Active' ? 'Live' : status}</span>
        <span class="tick">${fmt(tick)} <small>/ ${fmt(max)} ticks</small></span>
      </div>
      <div class="progress" role="progressbar" aria-label="Match progress" aria-valuemin="0" aria-valuemax="${max}" aria-valuenow="${tick}">
        <span style="width:${pct.toFixed(2)}%"></span></div>`);
  }

  function renderVision() {
    const players = [...(state?.players || [])].sort((a, b) => a.slot - b.slot);
    const key = players.map((p) => p.slot + p.name).join('|');
    if (!players.length || key === visionSlots) return;
    visionSlots = key;
    const field = $('vision-field');
    const hasFog = players.some((p) => p.visibility?.length);
    field.hidden = !hasFog;
    const cur = view.vision == null ? 'all' : String(view.vision);
    // Big matches get a drop-down; a row of sixteen buttons doesn't fit.
    $('vision').classList.toggle('segmented', players.length <= 4);
    setHtml($('vision'), players.length > 4 ? html`
      <select class="vision-select" aria-label="Show the map as seen by">
        <option value="all" ${cur === 'all' ? html`selected` : ''}>All</option>
        ${players.map((p) => html`<option value="${p.slot}" ${cur === String(p.slot) ? html`selected` : ''}>${displayName(p.name)}</option>`)}
      </select>` : html`
      <input type="radio" name="vision" id="v-all" value="all" ${cur === 'all' ? html`checked` : ''}><label for="v-all">All</label>
      ${players.map((p) => html`
        <input type="radio" name="vision" id="v-${p.slot}" value="${p.slot}" ${cur === String(p.slot) ? html`checked` : ''}>
        <label for="v-${p.slot}"><span class="pname" style="--pc:${slotVar(p.slot)}">${displayName(p.name)}</span></label>`)}`);
  }
  $('vision').addEventListener('change', (e) => {
    const slot = e.target.value === 'all' ? null : Number(e.target.value);
    view.setVision(slot);
    view3d?.setVision(slot);
    refreshTooltip();
  });

  /** The full-screen scoreboard: everyone ranked by total score, eliminated players last. */
  function renderBoard(list, byId, winnerId) {
    const rows = list.map((p) => {
      const score = p.score || byId.get(p.playerId)?.score;
      return { p, total: score ? totalOf(score) : 0 };
    }).sort((a, b) => (a.p.eliminated - b.p.eliminated) || (b.total - a.total) || (a.p.slot - b.p.slot));
    const num = (v) => (typeof v === 'number' ? fmt(v) : '–');
    setHtml($('board-list'), html`${rows.map(({ p, total }, i) => {
      const score = p.score || byId.get(p.playerId)?.score;
      return html`
      <tr class="${p.eliminated ? 'out' : ''}" style="--pc:${slotVar(p.slot)}">
        <td class="rank">${i + 1}</td>
        <th scope="row">
          <span class="pname">${displayName(p.name)}</span>
          ${winnerId && p.playerId === winnerId ? html`<span class="badge won">${say('Winner', 'Top Badger')}</span>` : ''}
          ${score ? html`<span class="breakdown">D ${fmt(score.destruction)} · E ${fmt(score.economy)} · S ${fmt(score.survival)}</span>` : ''}
        </th>
        <td class="num board-score">${fmt(total)}</td>
        <td class="num x">${num(p.resources)}</td>
        <td class="num x">${num(p.unitCount)}</td>
        <td class="num x">${num(p.buildingCount)}</td>
        <td class="num xx">${num(p.incomePerMinute)}</td>
      </tr>`;
    })}`);
    const tick = state?.tick ?? summary?.tick;
    $('board-tick').textContent = tick != null ? `tick ${fmt(tick)}` : '';
  }

  function renderPlayers() {
    const winnerId = (state?.outcome || result?.outcome || summary?.outcome)?.winnerId;
    const atEnd = !replay || replay.tick >= replay.last;
    const done = (finished || !!result) && atEnd;
    const byId = new Map((atEnd ? result?.players || [] : []).map((p) => [p.playerId, p]));
    // The summary knows who verified their bot with Discord; the live state doesn't.
    const seat = new Map((summary?.players || []).map((p) => [p.playerId, p]));
    const label = (p) => playerLabel({ ...p, ...seat.get(p.playerId) });
    let list;
    if (state) list = state.players;
    else if (result) list = result.players;
    else list = (summary?.players || []).map((p) => ({ ...p, pending: true }));
    list = [...list].sort((a, b) => a.slot - b.slot);
    if (!list.length) { setHtml($('players'), html`<p class="empty">No one has joined yet.</p>`); return; }
    renderBoard(list, byId, done && winnerId);
    // Five or more players: compact cards that wrap, so sixteen fit beside the map.
    const many = list.length > 4;
    $('players').classList.toggle('players-many', many);
    setHtml($('players'), html`${list.map((p) => {
      const r = byId.get(p.playerId);
      const score = p.score || r?.score;
      const won = done && winnerId && p.playerId === winnerId;
      if (many) {
        return html`
      <article class="player compact ${p.eliminated ? 'out' : ''}" style="--pc:${slotVar(p.slot)}" aria-label="${displayName(p.name)}">
        <h3>${label(p)}${p.eliminated ? html`<span class="badge dead">${say('Out', 'Hibernating')}</span>` : ''}${won ? html`<span class="badge won">${say('Winner', 'Top Badger')}</span>` : ''}</h3>
        ${score ? html`<p class="counts">Score ${fmt(totalOf(score))}</p>` : ''}
        ${typeof p.resources === 'number' ? html`<p class="counts">${fmt(p.resources)} ${ORE} · ${p.unitCount} units</p>` : ''}
      </article>`;
      }
      return html`
      <article class="player ${p.eliminated ? 'out' : ''}" style="--pc:${slotVar(p.slot)}" aria-label="${displayName(p.name)}">
        <div class="player-top">
          <h3>${label(p)}${p.eliminated ? html`<span class="badge dead">${say('Eliminated', 'Hibernating')}</span>` : ''}${won ? html`<span class="badge won">${say('Winner', 'Top Badger')}</span>` : ''}</h3>
          ${typeof p.resources === 'number' ? html`<span class="ore-count" title="${say('Ore in the bank', 'Grubs in the larder')}">${fmt(p.resources)} ${ORE}</span>` : ''}
        </div>
        ${SNOW ? html`<p class="counts">Home: ${hqName(p.slot)} · Lives in ${housingName(p.slot)}</p>` : ''}
        ${typeof p.unitCount === 'number' ? html`<p class="counts">${p.unitCount} ${say('unit', 'badger')}${p.unitCount === 1 ? "" : "s"}, ${p.buildingCount} building${p.buildingCount === 1 ? "" : "s"}${typeof p.incomePerMinute === 'number' ? html` · <span title="${say('Ore', 'Grubs')} banked in the last 60 ticks (one minute at normal speed)">${fmt(p.incomePerMinute)} ${ORE}/min</span>` : ''}</p>` : ''}
        ${r ? html`<p class="counts">${r.unitsProduced} ${say('units built', 'badgers trained')}, ${r.unitsLost} lost, ${r.unitsKilled} ${say('kills', 'bowled over')}, ${r.buildingsDestroyed} buildings ${say('razed', 'flattened')}, ${r.buildingsLost} lost</p>` : ''}
        ${p.pending ? html`<p class="counts">${say('Waiting for the match to start', 'Waiting for the opening whistle')}</p>` : ''}
        ${score ? html`<dl class="stats">
          <div><dt>Destruction</dt><dd>${fmt(score.destruction)}</dd></div>
          <div><dt>Economy</dt><dd>${fmt(score.economy)}</dd></div>
          <div><dt>Survival</dt><dd>${fmt(score.survival)}</dd></div>
          <div class="total"><dt>Total</dt><dd>${fmt(totalOf(score))}</dd></div>
        </dl>` : ''}
        ${p.upgrades?.length ? html`<ul class="upgrades" aria-label="Upgrades">${p.upgrades.map((u) => html`<li>${upgradeName(u)}</li>`)}</ul>` : ''}
      </article>`;
    })}`);
  }

  function addEvents(list) {
    let added = false;
    for (const ev of list || []) {
      const k = `${ev.tick}|${ev.kind}|${ev.message}`;
      if (eventKeys.has(k)) continue;
      eventKeys.add(k);
      events.push(ev);
      added = true;
    }
    if (!added) {
      if (!events.length) {
        setHtml($('feed'), html`<li class="muted"><span></span><span>${say('No kills, buildings or research yet.', 'No fights, buildings or classes yet.')}</span></li>`);
      }
      return;
    }
    events.sort((a, b) => b.tick - a.tick);
    if (events.length > 200) events.length = 200;
    setHtml($('fs-feed'), html`${events.slice(0, 14).map((ev) => html`
      <li class="${HOT.test(ev.kind) ? 'hot' : ''}"><span class="t">${fmt(ev.tick)}</span><span>${badgerize(ev.message)}</span></li>`)}`);
    setHtml($('feed'), html`${events.map((ev) => html`
      <li class="${HOT.test(ev.kind) ? 'hot' : ''}"><span class="t">${fmt(ev.tick)}</span>
        <span>${badgerize(ev.message)}<span class="k">${spaced(ev.kind)}</span></span></li>`)}`);
  }

  function renderBanner() {
    if (replay && replay.tick < replay.last) {
      if ($('banner-slot').childElementCount) setHtml($('banner-slot'), html``);
      return;
    }
    if ($('banner-slot').childElementCount) return;
    const outcome = state?.outcome || result?.outcome || summary?.outcome;
    if (!finished || !outcome) return;
    const players = state?.players || result?.players || summary?.players || [];
    const winner = players.find((p) => p.playerId === outcome.winnerId);
    const reason = reasonText(outcome);
    setHtml($('banner-slot'), html`
      <div class="banner ${winner ? 'win' : ''}" style="${winner ? `--pc:${slotVar(winner.slot)}` : ''}">
        <h2>${winner ? `${displayName(winner.name)} ${say('wins', 'wins the brawl')}` : say('Draw', 'A tie in Ephraim')}</h2>
        <p>${outcomeText(outcome, players)}. ${reason}</p>
        <div class="banner-actions">
          ${replay ? html`<button type="button" class="btn" data-replay="restart">Watch it again</button>`
            : replayNote ? '' : html`<button type="button" class="btn" data-replay="start">Watch the replay</button>`}
          <a class="btn ghost" href="#/">${say('Back to matches', 'Back to the brawls')}</a>
        </div>
      </div>`);
    fit();
  }

  function reasonText(outcome) {
    return (SNOW
      ? { Elimination: 'Last badger standing. Go Badgers!', TimeLimit: 'The final buzzer sounded; the higher score wins.', Surrender: 'The other side forfeited.' }
      : { Elimination: 'Last army standing.', TimeLimit: 'Time ran out; the higher score wins.', Surrender: 'The other side surrendered.' })[outcome?.reason] || '';
  }

  function renderAll() {
    renderHeader(); renderClock(); renderSpeed(); renderVision(); renderPlayers(); renderBanner();
  }

  // ---------- speed: exhibitions and games against house bots can be sped up, slowed down or paused
  const SPEED_CHOICES = [100, 250, 500, 1000, 2000];
  let speedKey = '';
  function renderSpeed() {
    const boxes = [$('speed'), $('fs-speed')];
    const status = finished ? 'Completed' : (state?.status ?? summary?.status);
    const adjustable = status === 'Active' && (state?.speedAdjustable ?? summary?.speedAdjustable);
    const ms = state?.tickIntervalMs ?? summary?.tickIntervalMs ?? 1000;
    const paused = Boolean(state?.paused ?? summary?.paused);
    const key = `${adjustable}|${ms}|${paused}`;
    if (key === speedKey) return;   // re-rendering every tick would steal focus from the drop-down
    speedKey = key;
    boxes.forEach((box) => { box.hidden = !adjustable; });
    if (!adjustable) return;
    const choices = SPEED_CHOICES.includes(ms) ? SPEED_CHOICES : [...SPEED_CHOICES, ms].sort((a, b) => a - b);
    const controls = html`
      <button type="button" class="btn ghost" data-act="pause" aria-pressed="${paused}">${paused ? 'Resume' : 'Pause'}</button>
      <button type="button" class="btn ghost" data-act="step" ${paused ? '' : html`disabled`} title="Run one tick">Step</button>
      <label class="small muted">Speed
        <select class="speed-ms">
          ${choices.map((c) => html`<option value="${c}" ${c === ms ? html`selected` : ''}>${c / 1000} s per tick</option>`)}
        </select>
      </label>`;
    boxes.forEach((box) => setHtml(box, controls));
  }
  async function changeSpeed(path, body) {
    try {
      summary = { ...summary, ...(await api(`/api/v1/matches/${matchId}/${path}`, { method: 'POST', body })) };
      // The stream brings the new state; until it does, show what we asked for.
      if (state) state = { ...state, paused: summary.paused, tickIntervalMs: summary.tickIntervalMs, tick: Math.max(state.tick, summary.tick) };
      renderAll();
    } catch (e) {
      setOverlay(say('Could not change the speed', 'The badgers would not change pace'), e.message);
      later(() => setOverlay(null), 2500);
    }
  }
  for (const box of [$('speed'), $('fs-speed')]) {
    box.addEventListener('click', (e) => {
      const act = e.target.closest('button')?.dataset.act;
      if (act === 'pause') changeSpeed('speed', { paused: !(state?.paused ?? summary?.paused) });
      if (act === 'step') changeSpeed('step');
    });
    box.addEventListener('change', (e) => {
      if (e.target.matches('.speed-ms')) changeSpeed('speed', { tickIntervalMs: Number(e.target.value) });
    });
  }

  function setOverlay(title, body) {
    if (!title) { overlay.hidden = true; return; }
    overlay.hidden = false;
    setHtml(overlay, html`<div><strong>${title}</strong>${body ? html`<span>${body}</span>` : ''}</div>`);
  }

  // ---------- data flow
  async function loadSummary() {
    try {
      summary = await api(byName && !matchId ? `/api/v1/matches/named/${encodeURIComponent(matchKey)}` : `/api/v1/matches/${matchId}`);
      matchId = summary.matchId;
      // Show the friendlier name in the address bar while the match is waiting or running.
      if (summary.name && summary.status !== 'Completed' && location.hash !== `#/match/${summary.name}`) {
        history.replaceState(null, '', `#/match/${summary.name}`);
      }
      return true;
    } catch (e) {
      if (e.status === 404) {
        $('title').textContent = say('Match not found', 'Brawl not found');
        setOverlay(byName ? say('No match with this name', 'No brawl with this name') : say('No match with this id', 'No brawl with this id'), say('It may have been removed. Go back to the match list to pick another.', 'It may have been cleared out. Go back to the list to pick another.'));
        setHtml($('players'), html``);
        setHtml($('clock'), html``);
        $('vision-field').hidden = true;
        return false;
      }
      setOverlay(say('Could not load the match', 'Could not load the brawl'), e.message);
      later(start, 3000);
      return false;
    }
  }

  async function loadMap() {
    try {
      const map = await api(`/api/v1/matches/${matchId}/map`);
      showMap(map);
      fit();
      return true;
    } catch (e) {
      if (e.code === 'MATCH_NOT_STARTED' || e.status === 409) {
        const open = summary ? summary.maxPlayers - summary.players.length : 0;
        setOverlay(say('Waiting for the match to start', 'Waiting for the opening whistle'), open > 0
          ? `${open} seat${open === 1 ? '' : 's'} still open. The map appears when every ${say('player', 'badger')} has joined.`
          : 'The map appears as soon as the first tick runs.');
      } else {
        setOverlay('Could not load the map', e.message);
      }
      return false;
    }
  }

  async function start() {
    if (!alive) return;
    if (!(await loadSummary())) return;
    renderAll();
    if (summary.status === 'Waiting') {
      // Polling the summary avoids a stream of 409s from /map while seats fill.
      const open = summary.maxPlayers - summary.players.length;
      setOverlay(say('Waiting for the match to start', 'Waiting for the opening whistle'), open > 0
        ? `${open} seat${open === 1 ? '' : 's'} still open. The map appears when every ${say('player', 'badger')} has joined.`
        : 'The map appears as soon as the first tick runs.');
      later(start, 2000);
      return;
    }
    if (summary.status === 'Completed' && await startReplay()) return;
    if (!(await loadMap())) {
      later(start, 2000);
      return;
    }
    setOverlay(null);
    if (summary.status === 'Completed') {
      await showFinal();
      return;
    }
    openStream();
  }

  function openStream() {
    if (!alive || finished) return;
    es = new EventSource(`/api/v1/matches/${matchId}/spectate/stream?fog=true`);
    es.onmessage = (msg) => {
      let s;
      try { s = JSON.parse(msg.data); } catch { return; }
      onState(s);
    };
    es.onerror = async () => {
      es?.close();
      es = null;
      if (!alive || finished) return;
      // Find out why the stream dropped: match over, archived, or a network blip.
      try {
        summary = await api(`/api/v1/matches/${matchId}`);
        if (summary.status === 'Completed') { await showFinal(); return; }
      } catch (e) {
        if (e.status === 404) { setOverlay(say('This match is gone', 'This brawl is over and gone'), 'It is no longer on the server.'); return; }
      }
      later(openStream, 1500);
    };
  }

  function onState(s) {
    if (!alive) return;
    state = s;
    showState(s);
    addEvents(s.events);
    renderAll();
    refreshTooltip();
    if (s.status === 'Completed') {
      es?.close();
      es = null;
      showFinal();
    }
  }

  async function showFinal() {
    if (finished) return;
    finished = true;
    es?.close();
    es = null;
    // Final positions: available while the match is still in memory.
    if (!state) {
      try {
        const s = await api(`/api/v1/matches/${matchId}/spectate?fog=true`);
        state = s;
        showState(s);
        addEvents(s.events);
      } catch { /* archived: only the result survives */ }
    }
    for (let attempt = 0; attempt < 5 && alive && !result; attempt++) {
      try {
        result = await api(`/api/v1/matches/${matchId}/result`);
      } catch (e) {
        if (e.status !== 409) break;
        await new Promise((r) => setTimeout(r, 600));
      }
    }
    if (!alive) return;
    if (!state && view.map) {
      setOverlay(null);
    }
    renderAll();
    if (!state && events.length === 0) {
      setHtml($('feed'), html`<li class="muted"><span></span><span>${replayNote || 'The log is not kept once a match is archived.'}</span></li>`);
    }
  }

  // ---------- game over: when a replay reaches the end, the final standings appear over the map
  let goAnim = 0;
  function showGameOver() {
    const box = $('gameover');
    const outcome = result?.outcome || state?.outcome || summary?.outcome;
    const live = new Map((state?.players || []).map((p) => [p.playerId, p]));
    const seat = new Map((summary?.players || []).map((p) => [p.playerId, p]));
    const players = result?.players?.length ? result.players : state?.players || [];
    if (!alive || !outcome || !players.length) return;
    const rows = players.map((p) => {
      const score = p.score || live.get(p.playerId)?.score;
      const won = p.playerId === outcome.winnerId;
      return { p: { ...seat.get(p.playerId), ...p, eliminated: live.get(p.playerId)?.eliminated }, score, total: score ? totalOf(score) : 0, won };
    }).sort((a, b) => (b.won - a.won) || (b.total - a.total) || (a.p.slot - b.p.slot));
    const top = Math.max(1, ...rows.map((r) => r.total));
    const winner = rows.find((r) => r.won)?.p;
    const ticks = outcome.ticks ?? replay?.last ?? state?.tick ?? 0;
    const pace = summary?.tickIntervalMs || state?.tickIntervalMs || 1000;
    const hasStats = rows.some((r) => typeof r.p.unitsKilled === 'number');
    const sum = (k) => rows.reduce((n, r) => n + (r.p[k] ?? 0), 0);
    const pct = (v) => `${((v / top) * 100).toFixed(2)}%`;
    // Who stood out: the best at each thing, if anyone did it at all.
    const best = (label, get, unit) => {
      const r = rows.reduce((a, b) => (get(b) > get(a) ? b : a), rows[0]);
      if (!(get(r) > 0)) return '';
      return html`<li style="--pc:${slotVar(r.p.slot)}"><span class="go-k">${label}</span>
        <b class="go-who">${displayName(r.p.name)}</b><span class="go-v"><span data-count="${get(r)}">${fmt(get(r))}</span> ${unit}</span></li>`;
    };
    const chip = (v, label) => html`<span class="go-chip"><b data-count="${v}">${fmt(v)}</b> ${label}</span>`;
    setHtml(box, html`
      <div class="go-card" style="${winner ? `--pc:${slotVar(winner.slot)}` : ''}" tabindex="-1">
        <button type="button" class="go-x" data-go="close" aria-label="Close and look at the map" title="Close (Esc)">×</button>
        <header class="go-head">
          <p class="go-kicker">${say('Game over', 'Final whistle')}</p>
          <h2 id="go-title">${winner ? html`<span class="go-crown" aria-hidden="true">♛</span> ${displayName(winner.name)} ${say('wins', 'wins the brawl')}` : say('Draw', 'A tie in Ephraim')}</h2>
          <p class="go-sub">${outcomeText(outcome, players)}. ${reasonText(outcome)}</p>
        </header>
        <ol class="go-board" aria-label="Final standings">
          ${rows.map((r, i) => html`
          <li class="go-row ${r.won ? 'won' : ''} ${r.p.eliminated ? 'out' : ''}" style="--pc:${slotVar(r.p.slot)};--i:${i}">
            <span class="go-rank">${i + 1}</span>
            <div class="go-main">
              <div class="go-line">
                <span class="go-name">${playerLabel(r.p)}</span>
                <span class="go-total"><span class="visually-hidden">Score </span><span data-count="${r.total}">${fmt(r.total)}</span></span>
              </div>
              ${r.score ? html`<div class="go-bar" title="Destruction ${fmt(r.score.destruction)} · Economy ${fmt(r.score.economy)} · Survival ${fmt(r.score.survival)}">
                <span class="seg d" style="width:${pct(r.score.destruction)}"></span><span class="seg e" style="width:${pct(r.score.economy)}"></span><span class="seg s" style="width:${pct(r.score.survival)}"></span>
              </div>` : ''}
              ${typeof r.p.unitsKilled === 'number' ? html`<div class="go-chips">
                ${chip(r.p.unitsKilled, say('kills', 'bowled over'))}${chip(r.p.unitsProduced, say('built', 'trained'))}${chip(r.p.unitsLost, 'lost')}${chip(r.p.buildingsDestroyed, say('razed', 'flattened'))}
              </div>` : ''}
            </div>
          </li>`)}
        </ol>
        <p class="go-legend" aria-hidden="true"><span class="seg d"></span>Destruction <span class="seg e"></span>Economy <span class="seg s"></span>Survival</p>
        <div class="go-facts">
          <ul class="go-best">
            ${best(say('Most kills', 'Most bowled over'), (r) => r.p.unitsKilled ?? 0, 'kills')}
            ${best('Biggest economy', (r) => r.score?.economy ?? 0, ORE)}
            ${best('Most destruction', (r) => r.score?.destruction ?? 0, 'pts')}
          </ul>
          <dl class="go-totals">
            <div><dt>Ticks</dt><dd data-count="${ticks}">${fmt(ticks)}</dd></div>
            <div><dt>At match pace</dt><dd>${clockText(ticks * pace)}</dd></div>
            ${hasStats ? html`<div><dt>${say('Units', 'Badgers')} fallen</dt><dd data-count="${sum('unitsLost')}">${fmt(sum('unitsLost'))}</dd></div>
            <div><dt>Buildings razed</dt><dd data-count="${sum('buildingsDestroyed')}">${fmt(sum('buildingsDestroyed'))}</dd></div>` : ''}
          </dl>
        </div>
        <div class="go-actions">
          <button type="button" class="btn" data-go="again">Watch it again</button>
          <button type="button" class="btn ghost" data-go="close">Look at the map</button>
          <a class="btn ghost" href="#/">${say('Back to matches', 'Back to the brawls')}</a>
        </div>
      </div>`);
    box.hidden = false;
    // A short map frame grows to fit the standings (full screen has room already).
    frame.style.minHeight = isFull() ? '' : `${box.querySelector('.go-card').offsetHeight + 40}px`;
    box.classList.remove('go-in');
    void box.offsetWidth;   // restart the entrance animation
    box.classList.add('go-in');
    box.querySelector('.go-card').focus({ preventScroll: true });
    countUp(box);
  }

  function hideGameOver() {
    const box = $('gameover');
    if (box.hidden) return;
    cancelAnimationFrame(goAnim);
    box.hidden = true;
    setHtml(box, html``);
    frame.style.minHeight = '';
  }

  /** Numbers tick up from zero as the rows arrive (not with reduced motion). */
  function countUp(box) {
    cancelAnimationFrame(goAnim);
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const els = [...box.querySelectorAll('[data-count]')].map((el) => {
      const row = el.closest('.go-row');
      const delay = 250 + (row ? Number(row.style.getPropertyValue('--i')) * 90 : 500);
      return { el, to: Number(el.dataset.count), delay };
    });
    const t0 = performance.now();
    const step = (now) => {
      let busy = false;
      for (const c of els) {
        const t = Math.min(1, Math.max(0, (now - t0 - c.delay) / 900));
        c.el.textContent = fmt(Math.round(c.to * (1 - Math.pow(1 - t, 3))));
        if (t < 1) busy = true;
      }
      if (busy && alive) goAnim = requestAnimationFrame(step);
    };
    goAnim = requestAnimationFrame(step);
  }

  /** 1:23, or 1:02:03 */
  function clockText(ms) {
    const s = Math.round(ms / 1000), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), ss = String(s % 60).padStart(2, '0');
    return h ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
  }

  // ---------- replay: finished matches play back from their recorded frames, a chunk at a time
  // Ticks per second, whatever pace the match was played at.
  const REPLAY_SPEEDS = [0.25, 0.5, 1, 2, 4, 8, 16, 32];
  const KEEP_CHUNKS = 4;
  let scrubbing = false;

  async function startReplay() {
    let manifest;
    // Older matches are re-simulated the first time anyone watches them, which can take a while.
    const slow = setTimeout(() => alive && setOverlay('Preparing the replay…',
      'Matches played before replays were recorded are rebuilt the first time someone watches them. This can take a few seconds.'), 700);
    try {
      manifest = await api(`/api/v1/matches/${matchId}/frames`);
    } catch (e) {
      replayNote = e.code === 'FRAMES_UNAVAILABLE' ? e.message : '';
      return false;
    } finally {
      clearTimeout(slow);
    }
    if (!alive) return true;
    replay = {
      m: manifest, last: manifest.lastTick, chunkTicks: manifest.chunkTicks,
      chunks: new Map(), pending: new Map(), tick: 0, playing: false, speed: 8,
      timer: null, seq: 0, feedTick: -1,
    };
    finished = true;
    es?.close();
    es = null;
    setHtml($('banner-slot'), html``);
    showMap(manifest.map);
    fit();
    setOverlay(null);
    renderReplayControls();
    if (!result) api(`/api/v1/matches/${matchId}/result`).then((r) => { result = r; if (alive) renderAll(); }).catch(() => {});
    await seekReplay(0);
    playReplay(true);
    return true;
  }

  function chunk(i) {
    if (replay.chunks.has(i)) {
      const frames = replay.chunks.get(i);
      replay.chunks.delete(i);          // most recently used goes last
      replay.chunks.set(i, frames);
      return Promise.resolve(frames);
    }
    if (!replay.pending.has(i)) {
      replay.pending.set(i, fetch(`/api/v1/matches/${matchId}/frames/${i}`)
        .then((r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.text(); })
        .then((text) => {
          const frames = text.split('\n').filter(Boolean).map((line) => JSON.parse(line));
          replay.chunks.set(i, frames);
          while (replay.chunks.size > KEEP_CHUNKS) replay.chunks.delete(replay.chunks.keys().next().value);
          return frames;
        })
        .finally(() => replay.pending.delete(i)));
    }
    return replay.pending.get(i);
  }

  async function seekReplay(t) {
    t = Math.max(0, Math.min(replay.last, Math.round(t)));
    if (t < replay.last) hideGameOver();
    replay.tick = t;
    const seq = ++replay.seq;
    const i = Math.floor(t / replay.chunkTicks);
    let frames = replay.chunks.get(i);
    if (!frames) {
      updateReplayControls(true);
      try {
        frames = await chunk(i);
      } catch (e) {
        if (seq !== replay.seq || !alive) return;
        playReplay(false);
        setOverlay('Could not load the replay', e.message);
        later(() => { setOverlay(null); seekReplay(replay.tick); }, 3000);
        return;
      }
    }
    if (seq !== replay.seq || !alive) return;   // a later seek has taken over
    const frame = frames[t - i * replay.chunkTicks];
    state = frame;
    // Animate at the playback pace, not the pace the match was recorded at.
    showState({ ...frame, tickIntervalMs: replayMs() });
    replayFeed(t);
    renderAll();
    refreshTooltip();
    updateReplayControls(false);
    // Fetch the next chunk ahead of time.
    if (i + 1 < replay.m.chunkCount && t - i * replay.chunkTicks >= replay.chunkTicks / 2) chunk(i + 1).catch(() => {});
  }

  function playReplay(on) {
    clearTimeout(replay.timer);
    if (on && replay.tick >= replay.last) seekReplay(0);
    replay.playing = on;
    syncPause();
    updateReplayControls(false);
    if (on) nextReplayTick();
  }

  function nextReplayTick() {
    const ms = replayMs();
    replay.timer = setTimeout(async () => {
      if (!alive || !replay.playing) return;
      if (replay.tick >= replay.last) { playReplay(false); showGameOver(); return; }
      await seekReplay(replay.tick + 1);
      if (alive && replay.playing) nextReplayTick();
    }, ms);
    timers.push(replay.timer);
  }

  const replayMs = () => Math.max(16, 1000 / replay.speed);

  /** The play-by-play up to the current tick (the newest 200). */
  function replayFeed(t) {
    if (replay.feedTick === t) return;
    replay.feedTick = t;
    events.length = 0;
    eventKeys.clear();
    const all = replay.m.events;
    let n = 0;
    while (n < all.length && all[n].tick <= t) n++;
    addEvents(all.slice(Math.max(0, n - 200), n));
  }

  function renderReplayControls() {
    const speedOptions = REPLAY_SPEEDS.map((x) => html`<option value="${x}" ${x === replay.speed ? html`selected` : ''}>${x} tick${x === 1 ? '' : 's'}/s</option>`);
    for (const box of [$('replay'), $('fs-replay')]) {
      box.hidden = false;
      setHtml(box, html`
        <span class="replay-tag">Replay</span>
        <button type="button" class="btn ghost" data-act="start" aria-label="Back to the start" title="Back to the start (Home)">⏮</button>
        <button type="button" class="btn ghost" data-act="back" aria-label="One tick back" title="One tick back (,)">−1</button>
        <button type="button" class="btn replay-play" data-act="play" aria-pressed="false">Play</button>
        <button type="button" class="btn ghost" data-act="fwd" aria-label="One tick forward" title="One tick forward (.)">+1</button>
        <button type="button" class="btn ghost" data-act="end" aria-label="Jump to the end" title="Jump to the end (End)">⏭</button>
        <input type="range" class="replay-scrub" min="0" max="${replay.last}" step="1" value="0" aria-label="Replay position (tick)">
        <span class="replay-time small muted" aria-hidden="true"></span>
        <label class="small muted replay-speed-label">Speed
          <select class="replay-speed">${speedOptions}</select>
        </label>`);
    }
  }

  function updateReplayControls(buffering) {
    if (!replay) return;
    for (const box of [$('replay'), $('fs-replay')]) {
      const play = box.querySelector('[data-act="play"]');
      if (!play) continue;
      play.textContent = replay.playing ? 'Pause' : replay.tick >= replay.last ? 'Replay' : 'Play';
      play.setAttribute('aria-pressed', String(replay.playing));
      const scrub = box.querySelector('.replay-scrub');
      if (!(scrubbing && document.activeElement === scrub)) scrub.value = String(replay.tick);
      scrub.setAttribute('aria-valuetext', `Tick ${replay.tick} of ${replay.last}`);
      box.querySelector('.replay-time').textContent = buffering ? 'Loading…' : `${fmt(replay.tick)} / ${fmt(replay.last)}`;
    }
  }

  const replayAct = (act) => {
    if (!replay || !act) return;
    if (act === 'play') playReplay(!replay.playing);
    else if (act === 'start') seekReplay(0);
    else if (act === 'end') { playReplay(false); seekReplay(replay.last).then(() => { if (replay.tick >= replay.last) showGameOver(); }); }
    else if (act === 'back') { playReplay(false); seekReplay(replay.tick - 1); }
    else if (act === 'fwd') { playReplay(false); seekReplay(replay.tick + 1); }
  };
  for (const box of [$('replay'), $('fs-replay')]) {
    box.addEventListener('click', (e) => replayAct(e.target.closest('button')?.dataset.act));
    box.addEventListener('input', (e) => {
      if (e.target.matches('.replay-scrub')) { scrubbing = true; seekReplay(Number(e.target.value)); }
    });
    box.addEventListener('change', (e) => {
      if (e.target.matches('.replay-scrub')) scrubbing = false;
      if (e.target.matches('.replay-speed')) {
        replay.speed = Number(e.target.value);
        root.querySelectorAll('.replay-speed').forEach((sel) => { sel.value = e.target.value; });
        if (replay.playing) playReplay(true);
      }
    });
  }
  $('banner-slot').addEventListener('click', async (e) => {
    const which = e.target.closest('[data-replay]')?.dataset.replay;
    if (!which) return;
    if (which === 'restart') { seekReplay(0); playReplay(true); return; }
    if (!(await startReplay())) {
      setOverlay('No replay for this match', replayNote || 'Try again in a moment.');
      later(() => setOverlay(null), 3000);
    }
  });
  $('gameover').addEventListener('click', (e) => {
    const act = e.target.closest('[data-go]')?.dataset.go;
    if (act === 'close') hideGameOver();
    if (act === 'again') { hideGameOver(); seekReplay(0); playReplay(true); }
  });
  $('gameover').addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); hideGameOver(); } });

  canvas.addEventListener('keydown', (e) => {
    if (!replay) return;
    const act = { ' ': 'play', ',': 'back', '.': 'fwd', Home: 'start', End: 'end' }[e.key];
    if (!act) return;
    e.preventDefault();
    replayAct(act);
  });

  start();
  try { if (localStorage.getItem('netrts.view') === '3d') set3d(true); } catch { /* storage blocked */ }

  return () => {
    alive = false;
    view3d?.destroy();
    es?.close();
    timers.forEach(clearTimeout);
    ro.disconnect();
    window.removeEventListener('resize', fit);
    scheme.removeEventListener('change', onScheme);
    document.removeEventListener('fullscreenchange', onFullscreenChange);
    document.removeEventListener('pointerdown', onDocDown);
    document.removeEventListener('keydown', onDocDown);
    audio?.dispose();
    document.documentElement.classList.remove('map-full');
    if (document.fullscreenElement === frame) document.exitFullscreen().catch(() => {});
    view.destroy();
  };
}

const totalOf = (score) => score.total ?? score.destruction + score.economy + score.survival;

function spaced(s) {
  return String(s ?? '').replace(/([a-z])([A-Z0-9])/g, '$1 $2');
}

function legendItems() {
  const shape = (type) => {
    const svg = `<svg viewBox="0 0 14 14" aria-hidden="true"><path d="${svgPath(type)}"/></svg>`;
    return svg;
  };
  const items = [
    [unitName('Worker'), shape('Worker')],
    [unitName('Soldier'), shape('Soldier')],
    [unitName('Archer'), shape('Archer')],
    [unitName('Scout'), shape('Scout')],
    ['Building', '<svg viewBox="0 0 14 14" aria-hidden="true"><rect x="1" y="1" width="12" height="12"/></svg>'],
    ['Under construction', '<svg viewBox="0 0 14 14" aria-hidden="true"><rect class="hollow" x="1.5" y="1.5" width="11" height="11"/><path class="hollow" d="M1.5 9.5l8-8M4.5 12.5l8-8"/></svg>'],
    SNOW
      ? ['Grubs', '<svg viewBox="0 0 14 14" aria-hidden="true"><path class="grub" d="M10.5 3.4A5 5 0 1 0 12 7"/></svg>']
      : ['Ore', '<svg viewBox="0 0 14 14" aria-hidden="true"><path class="ore" d="M7 1l5 4.5-2 7.5H4L2 5.5z"/></svg>'],
  ];
  return { __html: items.map(([label, svg]) => `<li>${svg}${esc(label)}</li>`).join('') };
}

/** SVG path matching the canvas unit shapes (via a recording context). */
function svgPath(type) {
  let d = '';
  const rec = {
    moveTo: (x, y) => { d += `M${x.toFixed(2)} ${y.toFixed(2)}`; },
    lineTo: (x, y) => { d += `L${x.toFixed(2)} ${y.toFixed(2)}`; },
    closePath: () => { d += 'Z'; },
    rect: (x, y, w, h) => { d += `M${x} ${y}h${w}v${h}h${-w}Z`; },
    arc: (cx, cy, r) => { d += `M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0Z`; },
  };
  unitPath(rec, type, 7, 7, 6);
  return d;
}

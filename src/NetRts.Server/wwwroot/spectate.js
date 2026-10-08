// Spectate view: #/match/{id}
import { api, html, setHtml, esc, slotVar, displayName, fmt, outcomeText } from './util.js';
import { GAME, ORE, unitName, buildingName, upgradeName, badgerize } from './lore.js';
import { MapView, unitPath } from './mapview.js';

const HOT = /Lost|Destroyed|Killed|Eliminated|Failed/;

export function mountSpectate(root, matchId) {
  document.title = `Brawl – ${GAME}`;
  setHtml(root, html`
    <div id="banner-slot" aria-live="polite"></div>
    <div class="match-head">
      <div>
        <p class="small" style="margin:0 0 6px"><a href="#/">All brawls</a></p>
        <h1 id="title">Waking the badgers…</h1>
        <p class="sub" id="subtitle"></p>
      </div>
      <div class="clock" id="clock" aria-live="off"></div>
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
        <div class="map-frame" id="frame">
          <canvas id="map" tabindex="0" role="img" aria-label="Brawl map. Use the arrow keys to inspect tiles, plus and minus to zoom."
            aria-describedby="tooltip"></canvas>
          <div class="map-controls" role="toolbar" aria-label="Map view">
            <button type="button" id="zoom-in" aria-label="Zoom in" title="Zoom in (+)">+</button>
            <button type="button" id="zoom-out" aria-label="Zoom out" title="Zoom out (−)">−</button>
            <button type="button" id="zoom-fit" title="Show the whole map (0)">Fit</button>
            <button type="button" id="follow" aria-pressed="false" title="Keep the camera on the biggest pile-up (F)">Follow the brawl</button>
            <span class="zoom-level" id="zoom-level" aria-live="polite">1×</span>
          </div>
          <div class="tooltip" id="tooltip" role="status" hidden></div>
          <div class="overlay-msg" id="overlay"><div><strong>Unrolling the Sanpete Valley…</strong></div></div>
        </div>
        <p class="small muted" style="margin:0">Scroll or pinch to zoom in on a brawl, drag to look around, double-click to zoom. Hover a tile to inspect it, or focus the map and use the arrow keys.</p>
      </div>
      <aside class="side" aria-label="Players and events">
        <section aria-labelledby="players-h">
          <h2 id="players-h" class="visually-hidden">Players</h2>
          <div id="players" style="display:grid;gap:10px"></div>
        </section>
        <section class="feed" aria-labelledby="feed-h">
          <h2 id="feed-h">Play-by-play</h2>
          <ol id="feed" aria-live="off"><li class="muted"><span></span><span>Waiting for the opening whistle…</span></li></ol>
        </section>
      </aside>
    </div>`);

  const $ = (id) => root.querySelector('#' + id);
  const canvas = $('map'), frame = $('frame'), overlay = $('overlay'), tooltip = $('tooltip');
  const view = new MapView(canvas);

  let alive = true;
  let es = null;
  let timers = [];
  let summary = null;
  let state = null;
  let result = null;
  let finished = false;
  let visionSlots = '';
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
    const top = frame.getBoundingClientRect().top + window.scrollY;
    // Show the whole map without scrolling where possible; phones fall back to width.
    const h = window.innerWidth < 760 ? w : Math.max(300, window.innerHeight - top - 40);
    view.fit(w, h);
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
      items.push(html`<div class="tt-item"><b>${buildingName(b.type, b.owner)}</b> #${b.id} ${owner(b.owner)}<br>
        HP ${fmt(b.hp)} / ${fmt(b.maxHp)}${b.completed ? '' : html`<br>Under construction, ${b.constructionPercent}%`}
        ${b.production?.length ? html`<br>Training ${b.production.map((p) => `${unitName(p.unitType)} ${p.percent}%`).join(', ')}` : ''}
        ${b.research ? html`<br>Studying ${upgradeName(b.research.upgrade)} ${b.research.percent}%` : ''}</div>`);
    }
    for (const u of info.units) {
      items.push(html`<div class="tt-item"><b>${unitName(u.type)}</b> #${u.id} ${owner(u.owner)}<br>
        HP ${fmt(u.hp)} / ${fmt(u.maxHp)}, ${u.activity.toLowerCase()}
        ${u.carrying ? html`<br>Carrying ${u.carrying} ${ORE}` : ''}
        ${u.targetId != null ? html`<br>Target #${u.targetId}` : ''}
        ${u.destination ? html`<br>Heading to ${u.destination.x}, ${u.destination.y}` : ''}</div>`);
    }
    for (const r of info.resources) {
      items.push(html`<div class="tt-item"><b>Henhouse</b> #${r.id}<br>${fmt(r.remaining)} Sunday ${ORE} left</div>`);
    }
    const fog = info.visible === false ? html`, hidden from ${who.get(view.vision)}` : '';
    setHtml(tooltip, html`<div class="tt-coord">${tile.x}, ${tile.y}: ${info.terrain === 'Rock' ? 'Wasatch Plateau rock' : 'Sanpete Valley'}${fog}</div>${items}`);
    tooltip.hidden = false;
    const pt = anchor || view.tileCenter(tile);
    const fr = frame.getBoundingClientRect(), cr = canvas.getBoundingClientRect();
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
    zoomLabel.textContent = `${view.zoom >= 10 ? Math.round(view.zoom) : Math.round(view.zoom * 10) / 10}×`;
    followBtn.setAttribute('aria-pressed', String(view.follow));
    canvas.style.cursor = view.zoom > 1 ? 'grab' : 'crosshair';
    canvas.style.touchAction = view.zoom > 1 ? 'none' : 'pan-y';
    queueMicrotask(() => refreshTooltip()); // defined further down
  };
  view.onCameraChange(view);
  $('zoom-in').addEventListener('click', () => view.zoomCentre(1.5));
  $('zoom-out').addEventListener('click', () => view.zoomCentre(1 / 1.5));
  $('zoom-fit').addEventListener('click', () => view.resetView());
  followBtn.addEventListener('click', () => view.setFollow(!view.follow));

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
    if (e.key === 'Escape') { inspect(null); return; }
    if (e.key === '+' || e.key === '=') { view.zoomCentre(1.5); return; }
    if (e.key === '-' || e.key === '_') { view.zoomCentre(1 / 1.5); return; }
    if (e.key === '0') { view.resetView(); return; }
    if (e.key === 'f' || e.key === 'F') { view.setFollow(!view.follow); return; }
    if (!d) return;
    e.preventDefault();
    const step = e.shiftKey ? 8 : 1;
    const c = view.cursor || { x: view.map.width >> 1, y: view.map.height >> 1 };
    inspect({
      x: Math.max(0, Math.min(view.map.width - 1, c.x + d[0] * step)),
      y: Math.max(0, Math.min(view.map.height - 1, c.y + d[1] * step)),
    }, null, true);
  });
  const refreshTooltip = () => { if (!tooltip.hidden && view.cursor) inspect(view.cursor, null); };

  // ---------- rendering pieces
  function renderHeader() {
    const players = [...(state?.players || summary?.players || [])].sort((a, b) => a.slot - b.slot);
    const title = players.length ? players.map((p) => displayName(p.name)).join(' vs ') : 'Waiting for badgers';
    $('title').textContent = title;
    document.title = `${title} – ${GAME}`;
    if (summary) {
      $('subtitle').textContent = `${summary.mapWidth} × ${summary.mapHeight} map, seed ${summary.seed}, ${summary.tickIntervalMs} ms per tick, brawl ${summary.matchId.slice(0, 8)}`;
    }
  }

  function renderClock() {
    const tick = state?.tick ?? summary?.tick ?? 0;
    const max = state?.maxTicks ?? summary?.maxTicks ?? 0;
    const status = finished ? 'Completed' : (state?.status ?? summary?.status ?? 'Waiting');
    const pct = max ? Math.min(100, (tick / max) * 100) : 0;
    setHtml($('clock'), html`
      <div class="clock-top">
        <span class="status ${status}">${status === 'Active' ? 'Live' : status}</span>
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
    setHtml($('vision'), html`
      <input type="radio" name="vision" id="v-all" value="all" ${cur === 'all' ? html`checked` : ''}><label for="v-all">All</label>
      ${players.map((p) => html`
        <input type="radio" name="vision" id="v-${p.slot}" value="${p.slot}" ${cur === String(p.slot) ? html`checked` : ''}>
        <label for="v-${p.slot}"><span class="pname" style="--pc:${slotVar(p.slot)}">${displayName(p.name)}</span></label>`)}`);
  }
  $('vision').addEventListener('change', (e) => {
    view.setVision(e.target.value === 'all' ? null : Number(e.target.value));
    refreshTooltip();
  });

  function renderPlayers() {
    const winnerId = (state?.outcome || result?.outcome || summary?.outcome)?.winnerId;
    const done = finished || !!result;
    const byId = new Map((result?.players || []).map((p) => [p.playerId, p]));
    let list;
    if (state) list = state.players;
    else if (result) list = result.players;
    else list = (summary?.players || []).map((p) => ({ ...p, pending: true }));
    list = [...list].sort((a, b) => a.slot - b.slot);
    if (!list.length) { setHtml($('players'), html`<p class="empty">No one has joined yet.</p>`); return; }
    setHtml($('players'), html`${list.map((p) => {
      const r = byId.get(p.playerId);
      const score = p.score || r?.score;
      const won = done && winnerId && p.playerId === winnerId;
      return html`
      <article class="player ${p.eliminated ? 'out' : ''}" style="--pc:${slotVar(p.slot)}" aria-label="${displayName(p.name)}">
        <div class="player-top">
          <h3>${displayName(p.name)}${p.eliminated ? html`<span class="badge dead">Hibernating</span>` : ''}${won ? html`<span class="badge won">Top Badger</span>` : ''}</h3>
          ${typeof p.resources === 'number' ? html`<span class="ore-count" title="Sunday eggs in the basket">${fmt(p.resources)} ${ORE}</span>` : ''}
        </div>
        <p class="counts">Home base: ${buildingName('CommandCenter', p.slot)}</p>
        ${typeof p.unitCount === 'number' ? html`<p class="counts">${p.unitCount} badger${p.unitCount === 1 ? "" : "s"}, ${p.buildingCount} building${p.buildingCount === 1 ? "" : "s"}${typeof p.incomePerMinute === 'number' ? html` · <span title="Eggs banked in the last 60 ticks (one minute at normal speed)">${fmt(p.incomePerMinute)} ${ORE}/min</span>` : ''}</p>` : ''}
        ${r ? html`<p class="counts">${r.unitsProduced} badgers trained, ${r.unitsLost} lost, ${r.unitsKilled} sacked, ${r.buildingsDestroyed} buildings flattened, ${r.buildingsLost} lost</p>` : ''}
        ${p.pending ? html`<p class="counts">Waiting for the opening whistle</p>` : ''}
        ${score ? html`<dl class="stats">
          <div><dt>Destruction</dt><dd>${fmt(score.destruction)}</dd></div>
          <div><dt>Economy</dt><dd>${fmt(score.economy)}</dd></div>
          <div><dt>Survival</dt><dd>${fmt(score.survival)}</dd></div>
          <div class="total"><dt>Total</dt><dd>${fmt(score.total ?? score.destruction + score.economy + score.survival)}</dd></div>
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
        setHtml($('feed'), html`<li class="muted"><span></span><span>No tackles, buildings or classes yet.</span></li>`);
      }
      return;
    }
    events.sort((a, b) => b.tick - a.tick);
    if (events.length > 200) events.length = 200;
    setHtml($('feed'), html`${events.map((ev) => html`
      <li class="${HOT.test(ev.kind) ? 'hot' : ''}"><span class="t">${fmt(ev.tick)}</span>
        <span>${badgerize(ev.message)}<span class="k">${spaced(ev.kind)}</span></span></li>`)}`);
  }

  function renderBanner() {
    if ($('banner-slot').childElementCount) return;
    const outcome = state?.outcome || result?.outcome || summary?.outcome;
    if (!finished || !outcome) return;
    const players = state?.players || result?.players || summary?.players || [];
    const winner = players.find((p) => p.playerId === outcome.winnerId);
    const reason = { Elimination: 'Last badger standing. Go Badgers!', TimeLimit: 'The final buzzer sounded; the higher score wins.', Surrender: 'The other side forfeited.' }[outcome.reason] || '';
    setHtml($('banner-slot'), html`
      <div class="banner ${winner ? 'win' : ''}" style="${winner ? `--pc:${slotVar(winner.slot)}` : ''}">
        <h2>${winner ? `${displayName(winner.name)} wins the brawl` : 'A tie in Ephraim'}</h2>
        <p>${outcomeText(outcome, players)}. ${reason}</p>
        <a class="btn ghost" href="#/">Back to the brawls</a>
      </div>`);
    fit();
  }

  function renderAll() {
    renderHeader(); renderClock(); renderVision(); renderPlayers(); renderBanner();
  }

  function setOverlay(title, body) {
    if (!title) { overlay.hidden = true; return; }
    overlay.hidden = false;
    setHtml(overlay, html`<div><strong>${title}</strong>${body ? html`<span>${body}</span>` : ''}</div>`);
  }

  // ---------- data flow
  async function loadSummary() {
    try {
      summary = await api(`/api/v1/matches/${matchId}`);
      return true;
    } catch (e) {
      if (e.status === 404) {
        $('title').textContent = 'Brawl not found';
        setOverlay('No brawl with this id', 'It may have been cleared out. Go back to the list to pick another.');
        setHtml($('players'), html``);
        setHtml($('clock'), html``);
        $('vision-field').hidden = true;
        return false;
      }
      setOverlay('Could not load the brawl', e.message);
      later(start, 3000);
      return false;
    }
  }

  async function loadMap() {
    try {
      const map = await api(`/api/v1/matches/${matchId}/map`);
      view.setMap(map);
      fit();
      return true;
    } catch (e) {
      if (e.code === 'MATCH_NOT_STARTED' || e.status === 409) {
        const open = summary ? summary.maxPlayers - summary.players.length : 0;
        setOverlay('Waiting for the opening whistle', open > 0
          ? `${open} seat${open === 1 ? '' : 's'} still open. The map appears when every badger has joined.`
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
      setOverlay('Waiting for the opening whistle', open > 0
        ? `${open} seat${open === 1 ? '' : 's'} still open. The map appears when every badger has joined.`
        : 'The map appears as soon as the first tick runs.');
      later(start, 2000);
      return;
    }
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
        if (e.status === 404) { setOverlay('This brawl is over and gone', 'It is no longer on the server.'); return; }
      }
      later(openStream, 1500);
    };
  }

  function onState(s) {
    if (!alive) return;
    state = s;
    view.setState(s);
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
        view.setState(s);
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
      setHtml($('feed'), html`<li class="muted"><span></span><span>The log is not kept once a match is archived.</span></li>`);
    }
  }

  start();

  return () => {
    alive = false;
    es?.close();
    timers.forEach(clearTimeout);
    ro.disconnect();
    window.removeEventListener('resize', fit);
    scheme.removeEventListener('change', onScheme);
    view.destroy();
  };
}

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
    ['Sunday eggs', '<svg viewBox="0 0 14 14" aria-hidden="true"><path class="egg" d="M7 1.5C9.6 1.5 11.5 5.6 11.5 8.3S9.5 12.8 7 12.8 2.5 11 2.5 8.3 4.4 1.5 7 1.5z"/></svg>'],
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

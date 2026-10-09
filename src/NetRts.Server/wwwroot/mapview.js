// Canvas renderer for a NetRts match: terrain, ore, buildings, units, fog — with a zoomable camera.
// The Snow College theme swaps in its own art: grubs, claw marks, badgers, campus halls and dorms.
//
// Level of detail follows the on-screen tile size (Te, in device pixels):
//   Te <  9   plain shapes, no building letters
//   Te >= 9   shapes + building letters
//   Te >= 26  illustrated glyphs (pickaxe, sword & shield, bow, eye; keep, crossed swords, crate,
//             flask, tower — or the Snow College set), production / research bars, health bars on everything damaged
//   Te >= 56  labels: unit ids and building names
import { playerColours, cssVar } from './util.js';
import { SNOW, buildingName } from './lore.js';

const CLASSIC_GLYPH = { CommandCenter: 'C', Barracks: 'B', ResourceDepot: 'D', TechLab: 'T', GuardTower: 'G' };
// Snow College letters: HQs and housing by the first letter of their campus name, then Co-op Store,
// Makerspace and guard Tower.
const SNOW_GLYPH = { ResourceDepot: 'C', TechLab: 'M', GuardTower: 'T' };
const glyph = (b) => (!SNOW ? CLASSIC_GLYPH[b.type]
  : b.type === 'CommandCenter' || b.type === 'Barracks' ? buildingName(b.type, b.owner)[0] : SNOW_GLYPH[b.type]);
const unitIcon = (...args) => (SNOW ? snowUnitIcon : classicUnitIcon)(...args);
const buildingIcon = (...args) => (SNOW ? snowBuildingIcon : classicBuildingIcon)(...args);
const DETAIL = 26;
const LABELS = 56;
const MAX_TILE = 112; // largest on-screen tile, in device pixels
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export class MapView {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.map = null;
    this.state = null;
    this.vision = null;          // slot whose fog is shown, or null for all
    this.cursor = null;          // {x, y} keyboard / hover tile
    this.T = 8;                  // tile size at zoom 1, in device pixels
    this.zoom = 1;               // 1 = whole map fits
    this.cam = null;             // camera centre in tile coordinates
    this.camGoal = null;         // where the camera is gliding to (follow mode)
    this.follow = false;
    this.onCameraChange = null;  // callback(view) when zoom / follow changes
    this.anim = new Map();       // unit id -> {fx, fy, tx, ty, t0, dur}
    this.oreMax = new Map();     // deposit id -> largest remaining seen
    this.dirty = true;
    this.raf = 0;
    this.readTheme();
    const loop = (now) => {
      this.raf = requestAnimationFrame(loop);
      const gliding = this.glide();
      if (this.dirty || gliding || this.animating(now)) this.draw(now);
    };
    this.raf = requestAnimationFrame(loop);
  }

  destroy() { cancelAnimationFrame(this.raf); }

  readTheme() {
    this.colours = playerColours();
    this.theme = {
      open: cssVar('--terrain-open'),
      grid: cssVar('--terrain-grid'),
      rock: cssVar('--terrain-rock'),
      rockHi: cssVar('--terrain-rock-hi'),
      ore: cssVar('--ore'),
      fog: cssVar('--fog'),
      ink: cssVar('--ink'),
      outline: cssVar('--outline'),
      glyph: cssVar('--glyph'),
      bg: cssVar('--panel'),
      ok: cssVar('--ok'),
      danger: cssVar('--danger'),
      focus: cssVar('--focus'),
      signal: cssVar('--signal-bg'),
    };
    this.terrainCache = null;
    this.dirty = true;
  }

  setMap(map) {
    this.map = map;
    this.terrainCache = null;
    this.cam = { x: map.width / 2, y: map.height / 2 };
    this.dirty = true;
  }

  setVision(slot) {
    this.vision = slot;
    this.fogCache = null;
    this.dirty = true;
  }

  /** reveal scrolls the view to keep the tile on screen: for arrow keys only, never mouse hover. */
  setCursor(tile, reveal = false) {
    this.cursor = tile;
    if (tile && reveal) this.ensureVisible(tile);
    this.dirty = true;
  }

  /**
   * Size the canvas to availW × availH CSS pixels. The tile size at zoom 1 is the largest integer
   * (in device pixels) that shows the whole map; zooming in then uses the full canvas.
   */
  fit(availW, availH) {
    if (!this.map) return;
    const dpr = window.devicePixelRatio || 1;
    const { width: W, height: H } = this.map;
    const T = Math.max(2, Math.floor(Math.min(availW / W, availH / H) * dpr));
    const cw = Math.round(availW * dpr), ch = Math.round(Math.min(availH, (H * T) / dpr) * dpr);
    if (T === this.T && this.canvas.width === cw && this.canvas.height === ch) return;
    this.T = T;
    this.canvas.width = cw;
    this.canvas.height = ch;
    this.canvas.style.width = `${cw / dpr}px`;
    this.canvas.style.height = `${ch / dpr}px`;
    this.terrainCache = null;
    this.clampCamera();
    this.dirty = true;
  }

  // ---------- camera

  get maxZoom() { return Math.max(1, MAX_TILE / this.T); }

  /** On-screen tile size in device pixels (an integer keeps tiles crisp). */
  tileSize() { return Math.max(1, Math.round(this.T * this.zoom)); }

  /** Top-left of the view, in device pixels of the zoomed world (negative = the map is centred with margins). */
  offset(Te = this.tileSize()) {
    const { width: W, height: H } = this.map;
    const axis = (size, view, centre) => size <= view
      ? Math.round((size - view) / 2)
      : Math.round(Math.min(size - view, Math.max(0, centre * Te - view / 2)));
    return { ox: axis(W * Te, this.canvas.width, this.cam.x), oy: axis(H * Te, this.canvas.height, this.cam.y) };
  }

  clampCamera() {
    if (!this.map || !this.cam) return;
    const Te = this.tileSize();
    const halfW = this.canvas.width / 2 / Te, halfH = this.canvas.height / 2 / Te;
    const { width: W, height: H } = this.map;
    this.cam.x = W <= halfW * 2 ? W / 2 : Math.min(W - halfW, Math.max(halfW, this.cam.x));
    this.cam.y = H <= halfH * 2 ? H / 2 : Math.min(H - halfH, Math.max(halfH, this.cam.y));
  }

  /** Zoom by factor, keeping the world point under (cssX, cssY) still. */
  zoomAt(factor, cssX, cssY) {
    if (!this.map) return;
    const scale = this.canvas.width / this.canvas.getBoundingClientRect().width;
    const px = cssX * scale, py = cssY * scale;
    const before = this.screenToWorld(px, py);
    this.zoom = Math.min(this.maxZoom, Math.max(1, this.zoom * factor));
    const Te = this.tileSize();
    this.cam = {
      x: (before.x * Te - px + this.canvas.width / 2) / Te,
      y: (before.y * Te - py + this.canvas.height / 2) / Te,
    };
    if (this.zoom === 1) this.cam = { x: this.map.width / 2, y: this.map.height / 2 };
    this.clampCamera();
    this.dirty = true;
    this.onCameraChange?.(this);
  }

  /** Zoom by factor and centre the view on the world point under (cssX, cssY). */
  zoomTo(factor, cssX, cssY) {
    if (!this.map) return;
    const scale = this.canvas.width / this.canvas.getBoundingClientRect().width;
    const target = this.screenToWorld(cssX * scale, cssY * scale);
    this.zoom = Math.min(this.maxZoom, Math.max(1, this.zoom * factor));
    this.cam = this.zoom === 1 ? { x: this.map.width / 2, y: this.map.height / 2 } : target;
    if (this.follow) this.setFollow(false);
    this.clampCamera();
    this.dirty = true;
    this.onCameraChange?.(this);
  }

  zoomCentre(factor) {
    const r = this.canvas.getBoundingClientRect();
    this.zoomAt(factor, r.width / 2, r.height / 2);
  }

  /** Pan by a drag of (dx, dy) CSS pixels. */
  panBy(dx, dy) {
    if (!this.map) return;
    const scale = this.canvas.width / this.canvas.getBoundingClientRect().width;
    const Te = this.tileSize();
    this.cam = { x: this.cam.x - (dx * scale) / Te, y: this.cam.y - (dy * scale) / Te };
    this.clampCamera();
    if (this.follow) this.setFollow(false);
    this.dirty = true;
  }

  resetView() {
    this.zoom = 1;
    this.follow = false;
    this.camGoal = null;
    if (this.map) this.cam = { x: this.map.width / 2, y: this.map.height / 2 };
    this.dirty = true;
    this.onCameraChange?.(this);
  }

  /** Follow mode: the camera glides to wherever the fighting is. */
  setFollow(on) {
    this.follow = on;
    if (on && this.zoom < 2.5) this.zoom = Math.min(this.maxZoom, 3);
    if (on) this.camGoal = this.hotspot() || this.camGoal;
    else this.camGoal = null;
    this.dirty = true;
    this.onCameraChange?.(this);
  }

  ensureVisible(tile) {
    if (!this.map || this.zoom === 1) return;
    const Te = this.tileSize();
    const { ox, oy } = this.offset(Te);
    const x0 = ox / Te, y0 = oy / Te, x1 = (ox + this.canvas.width) / Te, y1 = (oy + this.canvas.height) / Te;
    if (tile.x < x0 + 1 || tile.x > x1 - 2 || tile.y < y0 + 1 || tile.y > y1 - 2) {
      this.cam = { x: tile.x + 0.5, y: tile.y + 0.5 };
      this.clampCamera();
    }
  }

  glide() {
    if (!this.camGoal || !this.cam) return false;
    const k = reducedMotion() ? 1 : 0.05;
    const dx = this.camGoal.x - this.cam.x, dy = this.camGoal.y - this.cam.y;
    if (Math.abs(dx) < 0.02 && Math.abs(dy) < 0.02) return false;
    this.cam = { x: this.cam.x + dx * k, y: this.cam.y + dy * k };
    this.clampCamera();
    return true;
  }

  /** Centre of the biggest fight: the attacker with the most other attackers within 6 tiles. */
  hotspot() {
    const s = this.state;
    if (!s) return null;
    let fighters = s.units.filter((u) => u.activity === 'Attacking');
    if (fighters.length === 0) fighters = s.units.filter((u) => u.type !== 'Worker');
    if (fighters.length === 0) return null;
    let best = null, bestGroup = [];
    for (const u of fighters) {
      const group = fighters.filter((o) => Math.max(Math.abs(o.x - u.x), Math.abs(o.y - u.y)) <= 6);
      if (group.length > bestGroup.length) { best = u; bestGroup = group; }
    }
    if (!best) return null;
    return {
      x: bestGroup.reduce((a, u) => a + u.x, 0) / bestGroup.length + 0.5,
      y: bestGroup.reduce((a, u) => a + u.y, 0) / bestGroup.length + 0.5,
    };
  }

  screenToWorld(px, py) {
    const Te = this.tileSize();
    const { ox, oy } = this.offset(Te);
    return { x: (px + ox) / Te, y: (py + oy) / Te };
  }

  /** CSS-pixel position inside the canvas -> tile coordinates. */
  tileAt(offsetX, offsetY) {
    if (!this.map) return null;
    const scale = this.canvas.width / this.canvas.getBoundingClientRect().width;
    const w = this.screenToWorld(offsetX * scale, offsetY * scale);
    const x = Math.floor(w.x), y = Math.floor(w.y);
    if (x < 0 || y < 0 || x >= this.map.width || y >= this.map.height) return null;
    return { x, y };
  }

  /** Tile -> CSS-pixel centre, for placing tooltips. */
  tileCenter(tile) {
    const Te = this.tileSize();
    const { ox, oy } = this.offset(Te);
    const scale = this.canvas.width / this.canvas.getBoundingClientRect().width;
    return { x: ((tile.x + 0.5) * Te - ox) / scale, y: ((tile.y + 0.5) * Te - oy) / scale };
  }

  // ---------- state

  setState(state) {
    const prev = this.state;
    this.state = state;
    this.fogCache = null;
    for (const r of state.resources) {
      this.oreMax.set(r.id, Math.max(this.oreMax.get(r.id) || 0, r.remaining, 500));
    }

    // Lay stacked units out in sub-cells so several on one tile stay visible.
    const groups = new Map();
    for (const u of state.units) {
      const k = u.y * 4096 + u.x;
      if (!groups.has(k)) groups.set(k, []);
      groups.get(k).push(u);
    }
    const now = performance.now();
    const dur = reducedMotion() ? 0 : Math.min(Math.max(state.tickIntervalMs || 0, 0), 1200) * 0.9;
    const prevIds = new Set(prev ? prev.units.map((u) => u.id) : []);
    const next = new Map();
    for (const list of groups.values()) {
      const n = list.length;
      list.forEach((u, i) => {
        let ox = 0.5, oy = 0.5;
        if (n > 1) {
          const cols = n <= 4 ? 2 : 3;
          const cell = 1 / cols;
          const slot = i % (cols * cols);
          ox = cell * ((slot % cols) + 0.5);
          oy = cell * (Math.floor(slot / cols) + 0.5);
        }
        const tx = u.x + ox, ty = u.y + oy;
        const cur = this.anim.get(u.id);
        let fx = tx, fy = ty;
        if (cur && prevIds.has(u.id)) {
          const p = this.interp(cur, now);
          fx = p.x; fy = p.y;
        }
        u._scale = n > 1 ? (n <= 4 ? 0.62 : 0.45) : 1;
        next.set(u.id, { fx, fy, tx, ty, t0: now, dur });
      });
    }
    this.anim = next;
    if (this.follow) {
      // Only re-aim when the fight has really moved, so the camera doesn't jitter between skirmishes.
      const spot = this.hotspot();
      const goal = this.camGoal || this.cam;
      if (spot && (!goal || Math.hypot(spot.x - goal.x, spot.y - goal.y) > 5)) this.camGoal = spot;
    }
    this.dirty = true;
  }

  interp(a, now) {
    if (!a.dur) return { x: a.tx, y: a.ty };
    const t = Math.min(1, (now - a.t0) / a.dur);
    return { x: a.fx + (a.tx - a.fx) * t, y: a.fy + (a.ty - a.fy) * t };
  }

  animating(now) {
    for (const a of this.anim.values()) {
      if (a.dur && now - a.t0 < a.dur && (a.fx !== a.tx || a.fy !== a.ty)) return true;
    }
    return false;
  }

  /** Everything on a tile, for tooltips. */
  entitiesAt(x, y) {
    const s = this.state;
    const out = { terrain: this.map?.terrain[y]?.[x] === '#' ? 'Rock' : 'Open ground', units: [], buildings: [], resources: [] };
    if (!s) return out;
    out.buildings = s.buildings.filter((b) => b.x === x && b.y === y);
    out.units = s.units.filter((u) => u.x === x && u.y === y);
    out.resources = s.resources.filter((r) => r.x === x && r.y === y);
    if (this.vision != null) {
      const p = s.players.find((pl) => pl.slot === this.vision);
      out.visible = p?.visibility?.[y]?.[x] !== '0';
    }
    return out;
  }

  // ---------- drawing

  /** Rock and open ground at zoom-1 resolution; scaled up crisply when zoomed. */
  buildTerrain() {
    const { width: W, height: H, terrain } = this.map;
    const T = this.T;
    const c = document.createElement('canvas');
    c.width = W * T; c.height = H * T;
    const g = c.getContext('2d');
    g.fillStyle = this.theme.open;
    g.fillRect(0, 0, c.width, c.height);
    for (let y = 0; y < H; y++) {
      const row = terrain[y] || '';
      for (let x = 0; x < W; x++) {
        if (row[x] !== '#') continue;
        g.fillStyle = this.theme.rock;
        g.fillRect(x * T, y * T, T, T);
        // Lit edge where rock meets open ground above / left: gives the outcrops some relief.
        const edge = Math.max(1, Math.round(T / 7));
        g.fillStyle = this.theme.rockHi;
        if (y === 0 || terrain[y - 1][x] !== '#') g.fillRect(x * T, y * T, T, edge);
        if (x === 0 || row[x - 1] !== '#') g.fillRect(x * T, y * T, edge, T);
      }
    }
    this.terrainCache = c;
  }

  buildFog() {
    const s = this.state;
    const p = s?.players.find((pl) => pl.slot === this.vision);
    if (!p || !p.visibility?.length) { this.fogCache = 'none'; return; }
    const { width: W, height: H } = this.map;
    const c = document.createElement('canvas');
    c.width = W; c.height = H;
    const g = c.getContext('2d');
    g.fillStyle = this.theme.fog;
    for (let y = 0; y < H; y++) {
      const row = p.visibility[y] || '';
      let start = -1;
      for (let x = 0; x <= W; x++) {
        const fogged = x < W && row[x] === '0';
        if (fogged && start < 0) start = x;
        if (!fogged && start >= 0) { g.fillRect(start, y, x - start, 1); start = -1; }
      }
    }
    this.fogCache = c;
  }

  draw(now = performance.now()) {
    this.dirty = false;
    const g = this.ctx;
    if (!this.map) return;
    if (!this.terrainCache) this.buildTerrain();
    const { width: W, height: H } = this.map;
    const Te = this.tileSize();
    const { ox, oy } = this.offset(Te);
    const view = {
      x0: Math.floor(ox / Te) - 1, y0: Math.floor(oy / Te) - 1,
      x1: Math.ceil((ox + this.canvas.width) / Te) + 1, y1: Math.ceil((oy + this.canvas.height) / Te) + 1,
    };
    const inView = (x, y) => x >= view.x0 && x <= view.x1 && y >= view.y0 && y <= view.y1;

    g.setTransform(1, 0, 0, 1, 0, 0);
    g.imageSmoothingEnabled = false;
    g.fillStyle = this.theme.bg;
    g.fillRect(0, 0, this.canvas.width, this.canvas.height);
    g.drawImage(this.terrainCache, -ox, -oy, W * Te, H * Te);
    g.translate(-ox, -oy);

    this.drawGrid(g, Te, view);
    this.drawStarts(g, Te);
    const s = this.state;
    if (s) {
      this.drawResources(g, s, Te, inView);
      this.drawBuildings(g, s, Te, inView);
      this.drawUnits(g, s, Te, now, inView);
      if (this.vision != null) {
        if (!this.fogCache) this.buildFog();
        if (this.fogCache !== 'none') g.drawImage(this.fogCache, 0, 0, W * Te, H * Te);
      }
    }
    if (this.cursor) {
      g.strokeStyle = this.theme.focus;
      g.lineWidth = Math.max(2, Math.round(Te / 8));
      const o = g.lineWidth / 2;
      g.strokeRect(this.cursor.x * Te + o, this.cursor.y * Te + o, Te - g.lineWidth, Te - g.lineWidth);
    }
    g.setTransform(1, 0, 0, 1, 0, 0);
  }

  drawGrid(g, Te, view) {
    if (Te < 6) return;
    const { width: W, height: H } = this.map;
    g.fillStyle = this.theme.grid;
    const x0 = Math.max(0, view.x0), x1 = Math.min(W, view.x1), y0 = Math.max(0, view.y0), y1 = Math.min(H, view.y1);
    for (let x = x0; x <= x1; x++) g.fillRect(x * Te, y0 * Te, x % 8 === 0 ? 2 : 1, (y1 - y0) * Te);
    for (let y = y0; y <= y1; y++) g.fillRect(x0 * Te, y * Te, (x1 - x0) * Te, y % 8 === 0 ? 2 : 1);
  }

  drawStarts(g, Te) {
    if (!this.map.startPositions || Te < 6) return;
    g.strokeStyle = this.theme.ink;
    g.globalAlpha = 0.25;
    g.lineWidth = Math.max(1, Math.min(3, Te / 10));
    g.setLineDash([Math.max(3, Te / 3), Math.max(2, Te / 4)]);
    this.map.startPositions.forEach((p) => {
      g.beginPath();
      g.arc((p.x + 0.5) * Te, (p.y + 0.5) * Te, Te * 2.2, 0, Math.PI * 2);
      g.stroke();
    });
    g.setLineDash([]);
    g.globalAlpha = 1;
  }

  drawResources(g, s, T, inView) {
    if (SNOW) { this.drawGrubs(g, s, T, inView); return; }
    g.fillStyle = this.theme.ore;
    for (const r of s.resources) {
      if (r.remaining <= 0 || !inView(r.x, r.y)) continue;
      const frac = Math.min(1, r.remaining / (this.oreMax.get(r.id) || r.remaining));
      const size = T * (0.3 + 0.42 * frac);
      g.globalAlpha = 0.45 + 0.55 * frac;
      const cx = (r.x + 0.5) * T, cy = (r.y + 0.5) * T;
      g.beginPath();
      g.moveTo(cx, cy - size / 2);
      g.lineTo(cx + size * 0.38, cy - size * 0.1);
      g.lineTo(cx + size * 0.22, cy + size / 2);
      g.lineTo(cx - size * 0.22, cy + size / 2);
      g.lineTo(cx - size * 0.38, cy - size * 0.1);
      g.closePath();
      g.fill();
      if (T >= DETAIL) {
        // Facets catch the light.
        g.globalAlpha = 0.35;
        g.fillStyle = '#fff';
        g.beginPath();
        g.moveTo(cx, cy - size / 2);
        g.lineTo(cx + size * 0.38, cy - size * 0.1);
        g.lineTo(cx, cy);
        g.closePath();
        g.fill();
        g.fillStyle = this.theme.ore;
      }
      if (T >= LABELS * 1.5) {
        g.globalAlpha = 0.9;
        this.label(g, `${r.remaining}`, cx, (r.y + 1) * T + 2, T);
        g.fillStyle = this.theme.ore;
      }
    }
    g.globalAlpha = 1;
  }

  /** Snow College theme: curled grubs instead of ore crystals. */
  drawGrubs(g, s, T, inView) {
    for (const r of s.resources) {
      if (r.remaining <= 0 || !inView(r.x, r.y)) continue;
      const frac = Math.min(1, r.remaining / (this.oreMax.get(r.id) || r.remaining));
      const size = T * (0.3 + 0.42 * frac);
      g.globalAlpha = 0.45 + 0.55 * frac;
      const cx = (r.x + 0.5) * T, cy = (r.y + 0.5) * T;
      grub(g, cx, cy, size, this.theme.ore, T >= DETAIL);
      if (T >= LABELS * 1.5) {
        g.globalAlpha = 0.9;
        this.label(g, `${r.remaining}`, cx, (r.y + 1) * T + 2, T);
      }
    }
    g.globalAlpha = 1;
  }

  drawBuildings(g, s, T, inView) {
    const inset = Math.max(1, Math.round(T * 0.06));
    for (const b of s.buildings) {
      if (!inView(b.x, b.y)) continue;
      const col = this.colours[b.owner % this.colours.length];
      const x = b.x * T + inset, y = b.y * T + inset, w = T - inset * 2;
      if (b.completed) {
        g.fillStyle = col;
        g.fillRect(x, y, w, w);
        g.strokeStyle = this.theme.outline;
        g.lineWidth = Math.max(1, Math.round(T / 14));
        g.strokeRect(x + g.lineWidth / 2, y + g.lineWidth / 2, w - g.lineWidth, w - g.lineWidth);
        if (T >= DETAIL) {
          buildingIcon(g, b.type, x + w / 2, y + w / 2, w * 0.36, this.theme.glyph, col);
        } else if (T >= 9) {
          g.fillStyle = this.theme.glyph;
          g.font = `700 ${Math.round(T * 0.7)}px Bahnschrift, "Segoe UI", sans-serif`;
          g.textAlign = 'center';
          g.textBaseline = 'middle';
          g.fillText(glyph(b) || '?', x + w / 2, y + w / 2 + T * 0.04);
        }
      } else {
        // Construction site: outline + hatch, fill rising with progress.
        g.save();
        g.beginPath();
        g.rect(x, y, w, w);
        g.clip();
        g.strokeStyle = col;
        g.lineWidth = Math.max(1, T / 12);
        g.globalAlpha = 0.8;
        const step = Math.max(3, T / 4);
        for (let d = -w; d < w * 2; d += step) {
          g.beginPath();
          g.moveTo(x + d, y + w);
          g.lineTo(x + d + w, y);
          g.stroke();
        }
        const pct = Math.max(0, Math.min(100, b.constructionPercent)) / 100;
        g.globalAlpha = 0.55;
        g.fillStyle = col;
        g.fillRect(x, y + w * (1 - pct), w, w * pct);
        g.restore();
        g.strokeStyle = col;
        g.lineWidth = Math.max(1, Math.round(T / 10));
        g.strokeRect(x + g.lineWidth / 2, y + g.lineWidth / 2, w - g.lineWidth, w - g.lineWidth);
        if (T >= DETAIL) {
          g.globalAlpha = 0.8;
          buildingIcon(g, b.type, x + w / 2, y + w / 2, w * 0.3, col, this.theme.bg);
          g.globalAlpha = 1;
        }
      }
      if (T >= DETAIL) {
        // Production and research progress under the building.
        const job = b.production?.[0] ? b.production[0].percent : b.research ? b.research.percent : null;
        if (job != null) this.progressBar(g, b.x * T, (b.y + 1) * T - Math.max(3, T * 0.1), T, job / 100);
      }
      if (b.hp < b.maxHp && b.completed) this.hpBar(g, b.x * T, b.y * T, T, b.hp / b.maxHp);
      if (T >= LABELS) this.label(g, `${buildingName(b.type, b.owner)} #${b.id}`, (b.x + 0.5) * T, (b.y + 1) * T + 2, T);
    }
  }

  drawUnits(g, s, T, now, inView) {
    const pos = new Map();
    for (const u of s.units) {
      const a = this.anim.get(u.id);
      pos.set(u.id, a ? this.interp(a, now) : { x: u.x + 0.5, y: u.y + 0.5 });
    }
    // Attack lines first so the unit glyphs sit on top of them.
    const targets = new Map();
    s.buildings.forEach((b) => targets.set(b.id, { x: b.x + 0.5, y: b.y + 0.5 }));
    g.lineWidth = Math.max(1, Math.min(4, T / 12));
    for (const u of s.units) {
      if (u.activity !== 'Attacking' || u.targetId == null || !inView(u.x, u.y)) continue;
      const t = pos.get(u.targetId) || targets.get(u.targetId);
      if (!t) continue;
      const p = pos.get(u.id);
      g.strokeStyle = this.colours[u.owner % this.colours.length];
      g.globalAlpha = 0.7;
      if (T >= DETAIL) g.setLineDash([T / 6, T / 8]);
      g.beginPath();
      g.moveTo(p.x * T, p.y * T);
      g.lineTo(t.x * T, t.y * T);
      g.stroke();
      g.setLineDash([]);
    }
    g.globalAlpha = 1;

    for (const u of s.units) {
      if (!inView(u.x, u.y)) continue;
      const p = pos.get(u.id);
      const cx = p.x * T, cy = p.y * T;
      const stacked = u._scale || 1;
      const r = T * 0.34 * (T >= DETAIL && stacked < 1 ? Math.min(1, stacked * 1.15) : stacked);
      const col = this.colours[u.owner % this.colours.length];
      const detailed = r * 2 >= DETAIL * 0.62;
      g.fillStyle = col;
      g.strokeStyle = this.theme.outline;
      g.lineWidth = r < 3 ? 0.6 : Math.max(1, Math.min(3, r / (detailed ? 7 : 4)));
      g.beginPath();
      unitPath(g, u.type, cx, cy, detailed ? r * 1.12 : r);
      g.fill();
      g.stroke();
      if (detailed) {
        // Triangles and diamonds have less room inside, so their icons sit lower / smaller.
        const iconScale = { Worker: 0.62, Soldier: 0.62, Archer: 0.55, Scout: 0.5 }[u.type] ?? 0.55;
        unitIcon(g, u.type, cx, cy + (u.type === 'Archer' ? r * 0.22 : 0), r * iconScale, this.theme.glyph, col);
      }
      if (u.carrying > 0 && r >= 2) {
        g.fillStyle = this.theme.ore;
        g.strokeStyle = this.theme.outline;
        g.lineWidth = detailed ? Math.max(1, r / 10) : 0;
        g.beginPath();
        g.arc(cx + r * 0.8, cy - r * 0.8, Math.max(1, r * (detailed ? 0.28 : 0.35)), 0, Math.PI * 2);
        g.fill();
        if (detailed) g.stroke();
      }
      if (u.hp < u.maxHp) this.hpBar(g, cx - T / 2, cy - r - T * 0.32 + T * 0.1, T, u.hp / u.maxHp, u._scale);
      if (T * (u._scale || 1) >= LABELS) this.label(g, `#${u.id}`, cx, cy + r * 1.2 + 2, T * (u._scale || 1));
    }
  }

  hpBar(g, x, y, T, frac, scale = 1) {
    const w = T * 0.8 * scale, h = Math.max(2, Math.min(6, Math.round(T * 0.12)));
    const bx = x + (T - w) / 2, by = y + Math.max(0, T * 0.02);
    g.fillStyle = this.theme.outline;
    g.fillRect(bx - 1, by - 1, w + 2, h + 2);
    g.fillStyle = frac > 0.5 ? this.theme.ok : this.theme.danger;
    g.fillRect(bx, by, Math.max(1, w * frac), h);
  }

  progressBar(g, x, y, T, frac) {
    const w = T * 0.8, h = Math.max(2, Math.min(5, Math.round(T * 0.08)));
    const bx = x + (T - w) / 2;
    g.fillStyle = this.theme.outline;
    g.fillRect(bx - 1, y - 1, w + 2, h + 2);
    g.fillStyle = this.theme.signal;
    g.fillRect(bx, y, Math.max(1, w * Math.min(1, frac)), h);
  }

  label(g, text, cx, top, T) {
    const px = Math.max(10, Math.min(15, Math.round(T * 0.18)));
    g.font = `600 ${px}px Bahnschrift, "Segoe UI", sans-serif`;
    g.textAlign = 'center';
    g.textBaseline = 'top';
    g.lineWidth = 3;
    g.strokeStyle = this.theme.bg;
    g.strokeText(text, cx, top);
    g.fillStyle = this.theme.ink;
    g.fillText(text, cx, top);
  }
}

/** Shapes, so unit types are distinguishable without colour. */
export function unitPath(g, type, cx, cy, r) {
  switch (type) {
    case 'Soldier': // square
      g.rect(cx - r * 0.85, cy - r * 0.85, r * 1.7, r * 1.7);
      break;
    case 'Archer': // triangle
      g.moveTo(cx, cy - r * 1.05);
      g.lineTo(cx + r, cy + r * 0.8);
      g.lineTo(cx - r, cy + r * 0.8);
      g.closePath();
      break;
    case 'Scout': // diamond
      g.moveTo(cx, cy - r * 1.1);
      g.lineTo(cx + r * 0.8, cy);
      g.lineTo(cx, cy + r * 1.1);
      g.lineTo(cx - r * 0.8, cy);
      g.closePath();
      break;
    default: // Worker: circle
      g.arc(cx, cy, r * 0.85, 0, Math.PI * 2);
  }
}

/** A curled grub: what workers dig for. Detailed grubs get body segments. */
function grub(g, cx, cy, size, colour, detailed) {
  g.save();
  g.strokeStyle = colour;
  g.lineCap = 'round';
  g.lineWidth = Math.max(1.5, size * 0.3);
  const rad = size * 0.3;
  g.beginPath();
  g.arc(cx, cy, rad, Math.PI * 0.15, Math.PI * 1.75);
  g.stroke();
  if (detailed) {
    g.strokeStyle = 'rgba(255, 255, 255, 0.5)';
    g.lineWidth = Math.max(1, size * 0.05);
    for (let a = 0.45; a < 1.75; a += 0.32) {
      const t = Math.PI * a;
      g.beginPath();
      g.moveTo(cx + Math.cos(t) * rad * 0.55, cy + Math.sin(t) * rad * 0.55);
      g.lineTo(cx + Math.cos(t) * rad * 1.45, cy + Math.sin(t) * rad * 1.45);
      g.stroke();
    }
  }
  g.restore();
}

/** The badger: white wedge face, two dark stripes through the eyes, a dark nose. */
function badgerFace(g, s, ink, accent) {
  g.fillStyle = ink;
  g.beginPath();
  g.moveTo(-s * 0.9, -s * 0.5);
  g.quadraticCurveTo(0, -s * 1.05, s * 0.9, -s * 0.5);
  g.lineTo(s * 0.14, s * 0.85);
  g.quadraticCurveTo(0, s * 1.0, -s * 0.14, s * 0.85);
  g.closePath();
  g.fill();
  g.strokeStyle = accent;
  g.lineWidth = Math.max(1, s * 0.2);
  g.beginPath();
  g.moveTo(-s * 0.5, -s * 0.62);
  g.lineTo(-s * 0.08, s * 0.6);
  g.moveTo(s * 0.5, -s * 0.62);
  g.lineTo(s * 0.08, s * 0.6);
  g.stroke();
  g.fillStyle = accent;
  g.beginPath();
  g.arc(0, s * 0.74, s * 0.15, 0, Math.PI * 2);
  g.fill();
}

/** Snow College close-up unit art, drawn inside the unit's shape: ink is the glyph colour, accent the player colour. */
function snowUnitIcon(g, type, cx, cy, s, ink, accent) {
  g.save();
  g.translate(cx, cy);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.strokeStyle = ink;
  g.fillStyle = ink;
  g.lineWidth = Math.max(1.2, s * 0.2);
  switch (type) {
    case 'Worker': { // three claw marks: badgers dig
      for (const dx of [-0.5, 0, 0.5]) {
        g.beginPath();
        g.moveTo(s * (dx - 0.2), -s * 0.75);
        g.quadraticCurveTo(s * (dx + 0.25), -s * 0.1, s * (dx - 0.05), s * 0.75);
        g.stroke();
      }
      break;
    }
    case 'Soldier': { // a badger, face on
      badgerFace(g, s, ink, accent);
      break;
    }
    case 'Archer': { // an arrow in flight
      g.lineWidth = Math.max(1.4, s * 0.22);
      g.beginPath();
      g.moveTo(-s * 0.75, s * 0.75);
      g.lineTo(s * 0.45, -s * 0.45);
      g.stroke();
      g.beginPath(); // head
      g.moveTo(s * 0.9, -s * 0.9);
      g.lineTo(s * 0.12, -s * 0.62);
      g.lineTo(s * 0.62, -s * 0.12);
      g.closePath();
      g.fill();
      g.lineWidth = Math.max(1, s * 0.14); // fletching
      g.beginPath();
      g.moveTo(-s * 0.55, s * 0.55);
      g.lineTo(-s * 0.85, s * 0.35);
      g.moveTo(-s * 0.55, s * 0.55);
      g.lineTo(-s * 0.35, s * 0.85);
      g.stroke();
      break;
    }
    case 'Scout': { // a paw print: scouts sniff out the enemy
      g.beginPath();
      g.ellipse(0, s * 0.3, s * 0.42, s * 0.34, 0, 0, Math.PI * 2);
      g.fill();
      for (const [x, y] of [[-0.55, -0.15], [-0.2, -0.5], [0.2, -0.5], [0.55, -0.15]]) {
        g.beginPath();
        g.arc(s * x, s * y, s * 0.17, 0, Math.PI * 2);
        g.fill();
      }
      break;
    }
  }
  g.restore();
}

/** Snow College close-up building art. */
function snowBuildingIcon(g, type, cx, cy, s, ink, accent) {
  g.save();
  g.translate(cx, cy);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.fillStyle = ink;
  g.strokeStyle = ink;
  g.lineWidth = Math.max(1.2, s * 0.14);
  switch (type) {
    case 'CommandCenter': { // HQ: a historic campus hall (the Noyes Building, for one) with a cupola and pennant
      g.fillRect(-s * 0.85, -s * 0.15, s * 1.7, s * 1.0);
      g.beginPath(); // pediment
      g.moveTo(-s * 0.95, -s * 0.1);
      g.lineTo(0, -s * 0.6);
      g.lineTo(s * 0.95, -s * 0.1);
      g.closePath();
      g.fill();
      g.fillRect(-s * 0.16, -s * 0.85, s * 0.32, s * 0.3); // cupola
      g.fillStyle = accent; // door and windows
      g.fillRect(-s * 0.15, s * 0.35, s * 0.3, s * 0.5);
      for (const x of [-0.6, 0.35]) g.fillRect(s * x, s * 0.1, s * 0.25, s * 0.25);
      g.beginPath(); // pennant
      g.moveTo(0, -s * 0.85);
      g.lineTo(0, -s * 1.15);
      g.stroke();
      g.fillStyle = ink;
      g.beginPath();
      g.moveTo(0, -s * 1.15);
      g.lineTo(s * 0.45, -s * 1.05);
      g.lineTo(0, -s * 0.95);
      g.closePath();
      g.fill();
      break;
    }
    case 'Barracks': { // Housing: a dorm block with lit windows
      g.fillRect(-s * 0.8, -s * 0.85, s * 1.6, s * 1.7);
      g.fillStyle = accent;
      for (const y of [-0.6, -0.15, 0.3]) {
        for (const x of [-0.55, -0.12, 0.31]) g.fillRect(s * x, s * y, s * 0.24, s * 0.26);
      }
      g.fillRect(-s * 0.14, s * 0.62, s * 0.28, s * 0.23); // door
      break;
    }
    case 'ResourceDepot': { // Co-op Store (where the first classes met in 1888): a crate of grubs
      g.lineWidth = Math.max(1.2, s * 0.14);
      g.strokeRect(-s * 0.75, -s * 0.65, s * 1.5, s * 1.4);
      g.beginPath();
      g.moveTo(-s * 0.75, -s * 0.65);
      g.lineTo(s * 0.75, s * 0.75);
      g.moveTo(s * 0.75, -s * 0.65);
      g.lineTo(-s * 0.75, s * 0.75);
      g.stroke();
      break;
    }
    case 'TechLab': { // GRSC Makerspace: a flask
      g.beginPath();
      g.moveTo(-s * 0.22, -s * 0.9);
      g.lineTo(-s * 0.22, -s * 0.25);
      g.lineTo(-s * 0.8, s * 0.8);
      g.lineTo(s * 0.8, s * 0.8);
      g.lineTo(s * 0.22, -s * 0.25);
      g.lineTo(s * 0.22, -s * 0.9);
      g.stroke();
      g.beginPath();
      g.moveTo(-s * 0.52, s * 0.3);
      g.lineTo(s * 0.52, s * 0.3);
      g.lineTo(s * 0.8, s * 0.8);
      g.lineTo(-s * 0.8, s * 0.8);
      g.closePath();
      g.fill();
      break;
    }
    case 'GuardTower': { // tall tower with an arrow slit
      g.beginPath();
      g.moveTo(-s * 0.45, s * 0.9);
      g.lineTo(-s * 0.35, -s * 0.5);
      g.lineTo(-s * 0.6, -s * 0.5);
      g.lineTo(-s * 0.6, -s * 0.9);
      g.lineTo(-s * 0.3, -s * 0.9);
      g.lineTo(-s * 0.3, -s * 0.72);
      g.lineTo(-s * 0.1, -s * 0.72);
      g.lineTo(-s * 0.1, -s * 0.9);
      g.lineTo(s * 0.1, -s * 0.9);
      g.lineTo(s * 0.1, -s * 0.72);
      g.lineTo(s * 0.3, -s * 0.72);
      g.lineTo(s * 0.3, -s * 0.9);
      g.lineTo(s * 0.6, -s * 0.9);
      g.lineTo(s * 0.6, -s * 0.5);
      g.lineTo(s * 0.35, -s * 0.5);
      g.lineTo(s * 0.45, s * 0.9);
      g.closePath();
      g.fill();
      g.fillStyle = accent;
      g.fillRect(-s * 0.07, -s * 0.25, s * 0.14, s * 0.45);
      break;
    }
  }
  g.restore();
}

/** Original close-up unit art, drawn inside the unit's shape: ink is the glyph colour, accent the player colour. */
function classicUnitIcon(g, type, cx, cy, s, ink, accent) {
  g.save();
  g.translate(cx, cy);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.strokeStyle = ink;
  g.fillStyle = ink;
  g.lineWidth = Math.max(1.2, s * 0.2);
  switch (type) {
    case 'Worker': { // pickaxe
      g.beginPath();
      g.moveTo(-s * 0.7, s * 0.75);
      g.lineTo(s * 0.35, -s * 0.3);
      g.stroke();
      g.lineWidth = Math.max(1.2, s * 0.24);
      g.beginPath();
      g.moveTo(-s * 0.15, -s * 0.85);
      g.quadraticCurveTo(s * 0.6, -s * 0.75, s * 0.85, s * 0.1);
      g.stroke();
      break;
    }
    case 'Soldier': { // shield with a sword across it
      g.beginPath();
      g.moveTo(-s * 0.7, -s * 0.75);
      g.lineTo(s * 0.7, -s * 0.75);
      g.lineTo(s * 0.7, -s * 0.05);
      g.quadraticCurveTo(s * 0.65, s * 0.7, 0, s * 0.95);
      g.quadraticCurveTo(-s * 0.65, s * 0.7, -s * 0.7, -s * 0.05);
      g.closePath();
      g.fill();
      g.strokeStyle = accent;
      g.lineWidth = Math.max(1, s * 0.16);
      g.beginPath();
      g.moveTo(0, -s * 0.55);
      g.lineTo(0, s * 0.6);
      g.moveTo(-s * 0.32, -s * 0.12);
      g.lineTo(s * 0.32, -s * 0.12);
      g.stroke();
      break;
    }
    case 'Archer': { // an arrow in flight
      g.lineWidth = Math.max(1.4, s * 0.22);
      g.beginPath();
      g.moveTo(-s * 0.75, s * 0.75);
      g.lineTo(s * 0.45, -s * 0.45);
      g.stroke();
      g.beginPath(); // head
      g.moveTo(s * 0.9, -s * 0.9);
      g.lineTo(s * 0.12, -s * 0.62);
      g.lineTo(s * 0.62, -s * 0.12);
      g.closePath();
      g.fill();
      g.lineWidth = Math.max(1, s * 0.14); // fletching
      g.beginPath();
      g.moveTo(-s * 0.55, s * 0.55);
      g.lineTo(-s * 0.85, s * 0.35);
      g.moveTo(-s * 0.55, s * 0.55);
      g.lineTo(-s * 0.35, s * 0.85);
      g.stroke();
      break;
    }
    case 'Scout': { // an eye: scouts see furthest
      g.beginPath();
      g.moveTo(-s * 0.95, 0);
      g.quadraticCurveTo(0, -s * 0.85, s * 0.95, 0);
      g.quadraticCurveTo(0, s * 0.85, -s * 0.95, 0);
      g.closePath();
      g.fill();
      g.fillStyle = accent;
      g.beginPath();
      g.arc(0, 0, s * 0.3, 0, Math.PI * 2);
      g.fill();
      break;
    }
  }
  g.restore();
}

/** Original close-up building art. */
function classicBuildingIcon(g, type, cx, cy, s, ink, accent) {
  g.save();
  g.translate(cx, cy);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.fillStyle = ink;
  g.strokeStyle = ink;
  g.lineWidth = Math.max(1.2, s * 0.14);
  switch (type) {
    case 'CommandCenter': { // keep with battlements and a flag
      g.beginPath();
      g.moveTo(-s * 0.8, s * 0.85);
      g.lineTo(-s * 0.8, -s * 0.2);
      for (let i = 0; i < 4; i++) {
        const x = -s * 0.8 + i * s * 0.4;
        g.lineTo(x, -s * 0.45);
        g.lineTo(x + s * 0.2, -s * 0.45);
        g.lineTo(x + s * 0.2, -s * 0.2);
        g.lineTo(x + s * 0.4, -s * 0.2);
      }
      g.lineTo(s * 0.8, s * 0.85);
      g.closePath();
      g.fill();
      g.fillStyle = accent;
      g.fillRect(-s * 0.18, s * 0.3, s * 0.36, s * 0.55); // gate
      g.beginPath();
      g.moveTo(0, -s * 0.45);
      g.lineTo(0, -s * 1.0);
      g.stroke();
      g.fillStyle = ink;
      g.beginPath();
      g.moveTo(0, -s * 1.0);
      g.lineTo(s * 0.45, -s * 0.85);
      g.lineTo(0, -s * 0.7);
      g.closePath();
      g.fill();
      break;
    }
    case 'Barracks': { // crossed swords
      for (const dir of [1, -1]) {
        g.lineWidth = Math.max(1.2, s * 0.16);
        g.beginPath();
        g.moveTo(-s * 0.8 * dir, -s * 0.8);
        g.lineTo(s * 0.6 * dir, s * 0.6);
        g.stroke();
        g.beginPath();
        g.moveTo(s * 0.35 * dir, s * 0.75);
        g.lineTo(s * 0.75 * dir, s * 0.35);
        g.stroke();
      }
      break;
    }
    case 'ResourceDepot': { // crate
      g.lineWidth = Math.max(1.2, s * 0.14);
      g.strokeRect(-s * 0.75, -s * 0.65, s * 1.5, s * 1.4);
      g.beginPath();
      g.moveTo(-s * 0.75, -s * 0.65);
      g.lineTo(s * 0.75, s * 0.75);
      g.moveTo(s * 0.75, -s * 0.65);
      g.lineTo(-s * 0.75, s * 0.75);
      g.stroke();
      break;
    }
    case 'TechLab': { // flask
      g.beginPath();
      g.moveTo(-s * 0.22, -s * 0.9);
      g.lineTo(-s * 0.22, -s * 0.25);
      g.lineTo(-s * 0.8, s * 0.8);
      g.lineTo(s * 0.8, s * 0.8);
      g.lineTo(s * 0.22, -s * 0.25);
      g.lineTo(s * 0.22, -s * 0.9);
      g.stroke();
      g.beginPath();
      g.moveTo(-s * 0.52, s * 0.3);
      g.lineTo(s * 0.52, s * 0.3);
      g.lineTo(s * 0.8, s * 0.8);
      g.lineTo(-s * 0.8, s * 0.8);
      g.closePath();
      g.fill();
      break;
    }
    case 'GuardTower': { // tall tower with an arrow slit
      g.beginPath();
      g.moveTo(-s * 0.45, s * 0.9);
      g.lineTo(-s * 0.35, -s * 0.5);
      g.lineTo(-s * 0.6, -s * 0.5);
      g.lineTo(-s * 0.6, -s * 0.9);
      g.lineTo(-s * 0.3, -s * 0.9);
      g.lineTo(-s * 0.3, -s * 0.72);
      g.lineTo(-s * 0.1, -s * 0.72);
      g.lineTo(-s * 0.1, -s * 0.9);
      g.lineTo(s * 0.1, -s * 0.9);
      g.lineTo(s * 0.1, -s * 0.72);
      g.lineTo(s * 0.3, -s * 0.72);
      g.lineTo(s * 0.3, -s * 0.9);
      g.lineTo(s * 0.6, -s * 0.9);
      g.lineTo(s * 0.6, -s * 0.5);
      g.lineTo(s * 0.35, -s * 0.5);
      g.lineTo(s * 0.45, s * 0.9);
      g.closePath();
      g.fill();
      g.fillStyle = accent;
      g.fillRect(-s * 0.07, -s * 0.25, s * 0.14, s * 0.45);
      break;
    }
  }
  g.restore();
}

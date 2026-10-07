// Canvas renderer for a NetRts match: terrain, ore, buildings, units, fog.
import { playerColours, cssVar } from './util.js';

const BUILDING_GLYPH = { CommandCenter: 'C', Barracks: 'B', ResourceDepot: 'D', TechLab: 'T', GuardTower: 'G' };
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export class MapView {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.map = null;
    this.state = null;
    this.vision = null;          // slot whose fog is shown, or null for all
    this.cursor = null;          // {x, y} keyboard / hover tile
    this.T = 8;                  // tile size in device pixels
    this.anim = new Map();       // unit id -> {fx, fy, tx, ty, t0, dur}
    this.oreMax = new Map();     // deposit id -> largest remaining seen
    this.dirty = true;
    this.raf = 0;
    this.readTheme();
    const loop = (now) => {
      this.raf = requestAnimationFrame(loop);
      if (this.dirty || this.animating(now)) this.draw(now);
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
    };
    this.terrainCache = null;
    this.dirty = true;
  }

  setMap(map) {
    this.map = map;
    this.terrainCache = null;
    this.dirty = true;
  }

  setVision(slot) {
    this.vision = slot;
    this.fogCache = null;
    this.dirty = true;
  }

  setCursor(tile) {
    this.cursor = tile;
    this.dirty = true;
  }

  /** Fit the map into availW × availH CSS pixels with an integer device-pixel tile size. */
  fit(availW, availH) {
    if (!this.map) return;
    const dpr = window.devicePixelRatio || 1;
    const { width: W, height: H } = this.map;
    const T = Math.max(2, Math.floor(Math.min(availW / W, availH / H) * dpr));
    if (T === this.T && this.canvas.width === W * T) return;
    this.T = T;
    this.canvas.width = W * T;
    this.canvas.height = H * T;
    this.canvas.style.width = `${(W * T) / dpr}px`;
    this.canvas.style.height = `${(H * T) / dpr}px`;
    this.terrainCache = null;
    this.dirty = true;
  }

  /** CSS-pixel position inside the canvas -> tile coordinates. */
  tileAt(offsetX, offsetY) {
    if (!this.map) return null;
    const rect = this.canvas.getBoundingClientRect();
    const x = Math.floor((offsetX / rect.width) * this.map.width);
    const y = Math.floor((offsetY / rect.height) * this.map.height);
    if (x < 0 || y < 0 || x >= this.map.width || y >= this.map.height) return null;
    return { x, y };
  }

  /** Tile -> CSS-pixel centre, for placing tooltips. */
  tileCenter(tile) {
    const rect = this.canvas.getBoundingClientRect();
    return {
      x: ((tile.x + 0.5) / this.map.width) * rect.width,
      y: ((tile.y + 0.5) / this.map.height) * rect.height,
    };
  }

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

  buildTerrain() {
    const { width: W, height: H, terrain } = this.map;
    const T = this.T;
    const c = document.createElement('canvas');
    c.width = W * T; c.height = H * T;
    const g = c.getContext('2d');
    g.fillStyle = this.theme.open;
    g.fillRect(0, 0, c.width, c.height);
    if (T >= 6) {
      g.fillStyle = this.theme.grid;
      for (let x = 0; x <= W; x++) g.fillRect(x * T, 0, x % 8 === 0 ? 2 : 1, c.height);
      for (let y = 0; y <= H; y++) g.fillRect(0, y * T, c.width, y % 8 === 0 ? 2 : 1);
    }
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
    if (this.map.startPositions && T >= 6) {
      g.strokeStyle = this.theme.ink;
      g.globalAlpha = 0.25;
      g.lineWidth = Math.max(1, T / 10);
      g.setLineDash([T / 3, T / 4]);
      this.map.startPositions.forEach((p) => {
        g.beginPath();
        g.arc((p.x + 0.5) * T, (p.y + 0.5) * T, T * 2.2, 0, Math.PI * 2);
        g.stroke();
      });
      g.setLineDash([]);
      g.globalAlpha = 1;
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
    const T = this.T;
    g.imageSmoothingEnabled = false;
    g.drawImage(this.terrainCache, 0, 0);
    const s = this.state;
    if (s) {
      this.drawResources(g, s, T);
      this.drawBuildings(g, s, T);
      this.drawUnits(g, s, T, now);
      if (this.vision != null) {
        if (!this.fogCache) this.buildFog();
        if (this.fogCache !== 'none') g.drawImage(this.fogCache, 0, 0, this.map.width * T, this.map.height * T);
      }
    }
    if (this.cursor) {
      g.strokeStyle = this.theme.focus;
      g.lineWidth = Math.max(2, Math.round(T / 8));
      const o = g.lineWidth / 2;
      g.strokeRect(this.cursor.x * T + o, this.cursor.y * T + o, T - g.lineWidth, T - g.lineWidth);
    }
  }

  drawResources(g, s, T) {
    g.fillStyle = this.theme.ore;
    for (const r of s.resources) {
      if (r.remaining <= 0) continue;
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
    }
    g.globalAlpha = 1;
  }

  drawBuildings(g, s, T) {
    const inset = Math.max(1, Math.round(T * 0.06));
    const fontPx = Math.round(T * 0.7);
    for (const b of s.buildings) {
      const col = this.colours[b.owner % 4];
      const x = b.x * T + inset, y = b.y * T + inset, w = T - inset * 2;
      if (b.completed) {
        g.fillStyle = col;
        g.fillRect(x, y, w, w);
        g.strokeStyle = this.theme.outline;
        g.lineWidth = Math.max(1, Math.round(T / 14));
        g.strokeRect(x + g.lineWidth / 2, y + g.lineWidth / 2, w - g.lineWidth, w - g.lineWidth);
        if (T >= 9) {
          g.fillStyle = this.theme.glyph;
          g.font = `700 ${fontPx}px Bahnschrift, "Segoe UI", sans-serif`;
          g.textAlign = 'center';
          g.textBaseline = 'middle';
          g.fillText(BUILDING_GLYPH[b.type] || '?', x + w / 2, y + w / 2 + T * 0.04);
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
      }
      if (b.hp < b.maxHp && b.completed) this.hpBar(g, b.x * T, b.y * T, T, b.hp / b.maxHp);
    }
  }

  drawUnits(g, s, T, now) {
    const pos = new Map();
    for (const u of s.units) {
      const a = this.anim.get(u.id);
      pos.set(u.id, a ? this.interp(a, now) : { x: u.x + 0.5, y: u.y + 0.5 });
    }
    // Attack lines first so the unit glyphs sit on top of them.
    const targets = new Map();
    s.buildings.forEach((b) => targets.set(b.id, { x: b.x + 0.5, y: b.y + 0.5 }));
    g.lineWidth = Math.max(1, T / 12);
    for (const u of s.units) {
      if (u.activity !== 'Attacking' || u.targetId == null) continue;
      const t = pos.get(u.targetId) || targets.get(u.targetId);
      if (!t) continue;
      const p = pos.get(u.id);
      g.strokeStyle = this.colours[u.owner % 4];
      g.globalAlpha = 0.7;
      g.beginPath();
      g.moveTo(p.x * T, p.y * T);
      g.lineTo(t.x * T, t.y * T);
      g.stroke();
    }
    g.globalAlpha = 1;

    for (const u of s.units) {
      const p = pos.get(u.id);
      const cx = p.x * T, cy = p.y * T;
      const r = T * 0.34 * (u._scale || 1);
      g.fillStyle = this.colours[u.owner % 4];
      g.strokeStyle = this.theme.outline;
      g.lineWidth = r < 3 ? 0.6 : Math.max(1, r / 4);
      g.beginPath();
      unitPath(g, u.type, cx, cy, r);
      g.fill();
      g.stroke();
      if (u.carrying > 0 && r >= 2) {
        g.fillStyle = this.theme.ore;
        g.beginPath();
        g.arc(cx + r * 0.75, cy - r * 0.75, Math.max(1, r * 0.35), 0, Math.PI * 2);
        g.fill();
      }
      if (u.hp < u.maxHp) this.hpBar(g, cx - T / 2, cy - r - T * 0.32 + T * 0.1, T, u.hp / u.maxHp, u._scale);
    }
  }

  hpBar(g, x, y, T, frac, scale = 1) {
    const w = T * 0.8 * scale, h = Math.max(2, Math.round(T * 0.12));
    const bx = x + (T - w) / 2, by = y + Math.max(0, T * 0.02);
    g.fillStyle = this.theme.outline;
    g.fillRect(bx - 1, by - 1, w + 2, h + 2);
    g.fillStyle = frac > 0.5 ? this.theme.ok : this.theme.danger;
    g.fillRect(bx, by, Math.max(1, w * frac), h);
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

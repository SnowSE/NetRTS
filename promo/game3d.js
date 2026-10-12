// The promo's 3D shots drawn with the game's own 3D view (wwwroot/3d): the Sanpete Valley, campus
// buildings, badgers and effects, exactly as spectators see them. Same interface as battle3d.js's
// Battle3D, so promo.js only changes its import.
//
// The view runs in manual mode on a virtual clock: tick k "arrives" at k * MS milliseconds, and the
// picture at fractional tick t is drawn at clock t * MS + lag. Slow-motion and freeze-frame shots slow
// and freeze the effects with them. Frames depend only on the tick, apart from short-lived particles.
import * as THREE from 'three';
import { View3D } from '/src/NetRts.Server/wwwroot/3d/view.js';
import { hqName, housingName, BUILDING_NAME } from '/src/NetRts.Server/wwwroot/lore.js';
import { unitPos, frameAt, TEAM } from './battle3d.js';

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

export { unitPos, frameAt, TEAM };

const MS = 100;          // virtual milliseconds per tick
const LAG = MS * 2.05;   // the view's lag at a steady tick (see View3D.renderFrame)
const UNIT_SCALE = 1.15;      // close-ups read better with slightly bigger badgers...
const BUILDING_SCALE = 1.55;  // ...and buildings about twice their height, as in the game

export class Battle3D {
  constructor(canvas, tagLayer) {
    this.canvas = canvas;
    this.tagLayer = tagLayer;
    this.view = new View3D(canvas.parentElement, { badger: true, manual: true, canvas, labels: false, quality: 'high' });
    this.view.unitScale = UNIT_SCALE;
    this.view.buildingScale = BUILDING_SCALE;
    this.view.arrowScale = 1.8;
    this.view.relief = 0.4;   // the shots were framed for flat ground; low plateaus keep them clear
    this.match = null;
    this.fed = -1;
    this.lastV = null;
    this.tags = new Map();
  }

  /** Feed the view every frame up to the one it needs to draw fractional tick `tick`. */
  sync(match, tick) {
    const v = this.view;
    const last = match.frames.length - 1;
    const want = Math.min(last, Math.floor(tick + LAG / MS));
    const feed = (k) => { v.setClock(k * MS); v.setState({ ...match.frames[k], tick: k }); };
    if (match === this.match && this.fed >= 0 && want >= this.fed && want - this.fed <= 40) {
      for (let k = this.fed + 1; k <= want; k++) feed(k);
      this.fed = want;
      return false;
    }
    // A new shot: start the view a little before, so units have a path behind them.
    if (match !== this.match) v.setMap(match.map);
    v.state = null;
    v.units.clear();
    v.buildings.clear();
    v.corpses.length = 0;
    v.effects?.clear();
    v.queue.length = 0;
    v.interval = MS;
    v.gapMax = MS;
    const from = Math.max(0, want - 14);
    v.lastSet = (from - 1) * MS;
    for (let k = from; k <= want; k++) feed(k);
    // Effects from the run-up would all go off at once; keep only what's just about to happen.
    const now = tick * MS + LAG;
    v.queue = v.queue.filter((e) => e.t > now - MS * 1.5);
    this.match = match;
    this.fed = want;
    return true;
  }

  render(opts) {
    const { match, tick } = opts;
    const v = this.view;
    const restarted = this.sync(match, tick);
    // Positions follow the match clock (V), but animation (strides, swings, the bow, particles) runs on
    // the video's own time, so a slowed-down shot doesn't slow every badger's arms down with it.
    const V = tick * MS + LAG;
    const T = window.videoTime ?? 0;
    const fresh = restarted || this.lastT == null || T < this.lastT;
    const dt = fresh ? 1 / 30 : Math.min(0.1, T - this.lastT);
    const dv = fresh ? MS / 1000 * 3 * dt : Math.max(0, V - this.lastV) / 1000;
    v.timeScale = dt > 0 ? clamp(dv / dt, 0.02, 30) : 1;   // match seconds per video second
    this.lastV = V;
    this.lastT = T;
    v.setClock(V);

    // The shot's camera, kept clear of the hills.
    const c = opts.cam;
    const cam = v.camera;
    cam.fov = c.fov ?? 38;
    const floor = v.terrain.heightAt(c.pos[0], c.pos[2]) + 0.35;
    cam.position.set(c.pos[0], Math.max(c.pos[1], floor), c.pos[2]);
    cam.up.set(0, 1, 0);
    const look = new THREE.Vector3(...c.look);
    cam.lookAt(look);
    if (c.roll) cam.rotateZ(c.roll);
    cam.updateProjectionMatrix();
    v.lookAt = look;

    v.highlight = opts.highlight ?? null;
    if (v.bloom) v.bloom.strength = 0.4 + (opts.bloom ?? 0.85) * 0.25;
    v.renderFrame(dt);
    this.renderTags(opts);
  }

  // ---------- info tags (the floating cards over units and buildings)

  project(x, y, z) {
    const cam = this.view.camera;
    const p = new THREE.Vector3(x, y, z).project(cam);
    const W = this.canvas.clientWidth || this.canvas.width, H = this.canvas.clientHeight || this.canvas.height;
    return { x: (p.x + 1) / 2 * W, y: (1 - p.y) / 2 * H, behind: p.z > 1, dist: cam.position.distanceTo(new THREE.Vector3(x, y, z)) };
  }

  renderTags(opts) {
    const v = this.view;
    const want = new Map();
    for (const tg of opts.tags || []) want.set(`${tg.kind || 'unit'}:${tg.id}`, tg);
    for (const [key, el] of this.tags) if (!want.has(key)) { el.remove(); this.tags.delete(key); }
    for (const [key, tg] of want) {
      let info = null;
      if ((tg.kind || 'unit') === 'unit') {
        const a = v.units.get(tg.id);
        const p = v.positions?.get(tg.id);
        if (!a || !p) { this.tags.get(key)?.remove(); this.tags.delete(key); continue; }
        const u = a.u;
        const act = u.activity === 'Attacking' ? (u.targetId == null ? 'ATTACK-MOVE' : `ATTACK → #${u.targetId}`)
          : u.activity === 'Gathering' ? (u.carrying ? `HAULING ${u.carrying} GRUBS` : 'DIGGING GRUBS')
            : u.activity === 'Building' ? 'BUILDING' : u.activity === 'Moving' ? 'MOVING' : 'IDLE';
        const top = v.terrain.heightAt(p.x, p.z) + v.models.units[u.type].height * UNIT_SCALE + 0.15;
        info = { x: p.x, y: top, z: p.z, owner: u.owner, title: (tg.label ?? u.type).toUpperCase(), id: u.id, hp: u.hp, maxHp: u.maxHp,
          line: `(${u.x}, ${u.y})`, act: tg.act ?? act, dying: a.deathT != null };
      } else {
        const rec = v.buildings.get(tg.id);
        const bd = rec?.b;
        if (!bd) { this.tags.get(key)?.remove(); this.tags.delete(key); continue; }
        const name = bd.type === 'CommandCenter' ? hqName(bd.owner) : bd.type === 'Barracks' ? housingName(bd.owner) : BUILDING_NAME[bd.type];
        const kind = { CommandCenter: 'HQ', Barracks: 'HOUSING', ResourceDepot: 'DROP-OFF', TechLab: 'RESEARCH', GuardTower: 'DEFENCE' }[bd.type];
        const x = bd.x + 0.5, z = bd.y + 0.5;
        info = { x, y: v.terrain.heightAt(x, z) + v.models.building(bd.type, bd.owner).height * BUILDING_SCALE + 0.2, z, owner: bd.owner,
          title: (tg.label ?? name).toUpperCase(), id: bd.id, hp: bd.hp, maxHp: bd.maxHp, line: `${bd.type} · (${bd.x}, ${bd.y})`, act: tg.act ?? kind };
      }
      const s = this.project(info.x, info.y, info.z);
      let el = this.tags.get(key);
      if (!el) {
        el = document.createElement('div');
        el.className = 'tag';
        el.innerHTML = '<div class="tag-card"><div class="tag-h"><b></b><i></i></div><div class="tag-l"></div><div class="tag-hp"><span></span></div><div class="tag-a"></div></div><div class="tag-stem"></div>';
        this.tagLayer.appendChild(el);
        this.tags.set(key, el);
      }
      el.style.setProperty('--tc', tg.accent || TEAM[info.owner % 16]);
      el.querySelector('b').textContent = info.title;
      el.querySelector('i').textContent = `#${info.id}`;
      el.querySelector('.tag-l').textContent = info.line;
      el.querySelector('.tag-a').textContent = info.act;
      const hp = el.querySelector('.tag-hp span');
      hp.style.width = `${Math.max(0, info.hp / info.maxHp) * 100}%`;
      hp.parentElement.dataset.v = `HP ${info.hp}/${info.maxHp}`;
      hp.style.background = info.hp / info.maxHp > 0.5 ? '#5fd3a8' : '#ff8a7a';
      const scale = Math.max(0.55, Math.min(1.25, (opts.tagScale ?? 1) * 15 / s.dist));
      const fade = (tg.alpha ?? 1) * (info.dying ? 0.4 : 1);
      el.style.opacity = s.behind ? 0 : fade;
      el.style.transform = `translate(${s.x}px, ${s.y}px) scale(${scale})`;
    }
  }

  hideTags() { for (const el of this.tags.values()) el.remove(); this.tags.clear(); }
}

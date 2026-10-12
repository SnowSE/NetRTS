// 3D match view: the camera. Four ways to watch:
//   orbit     drag to pan, right-drag (or Shift-drag) to turn, wheel to zoom toward the cursor, WASD/QE
//   fly       free flight with mouse look (pointer lock): WASD to move, E/Q up and down, Shift for speed
//   follow    the camera rides along with one unit; drag to orbit around it
//   director  the camera finds the biggest fight on its own and circles it
import * as THREE from 'three';
import { clamp } from './noise.js';

const MIN_PITCH = 0.1, MAX_PITCH = 1.48;
const damp = (rate, dt) => 1 - Math.exp(-rate * dt);
const wrapAngle = (a) => Math.atan2(Math.sin(a), Math.cos(a));

export class CameraRig {
  constructor(camera, dom, terrain) {
    this.camera = camera;
    this.dom = dom;
    this.terrain = terrain;
    this.mode = 'orbit';
    this.maxDist = Math.max(terrain.W, terrain.H) * 1.25;
    this.goal = { target: new THREE.Vector3(terrain.W / 2, 0, terrain.H / 2), yaw: 0.6, pitch: 0.95, dist: this.maxDist * 0.8 };
    this.cur = { target: this.goal.target.clone(), yaw: this.goal.yaw, pitch: this.goal.pitch, dist: this.goal.dist };
    this.fly = { pos: new THREE.Vector3(), yaw: 0, pitch: -0.3, vel: new THREE.Vector3() };
    this.keys = new Set();
    this.followFn = null;       // () => Vector3 | null
    this.directorFn = null;     // () => {x, z, size} | null
    this.director = { yawSpeed: 0.07, lastCut: 0, spot: null };
    this.onModeChange = null;   // (mode) => void
    this.onPick = null;         // (clientX, clientY) => void
    this.onHover = null;        // (clientX, clientY) | null => void
    this.rate = 9;
    this.shake = 0;
    this.time = 0;
    this.overview(true);
    this.bind();
  }

  // ---------------------------------------------------------------- modes

  setMode(mode) {
    if (mode === this.mode) return;
    if (this.mode === 'fly' && document.pointerLockElement === this.dom) document.exitPointerLock();
    if (mode === 'fly') this.enterFly();
    if (this.mode === 'fly' && mode !== 'fly') this.leaveFly();
    this.mode = mode;
    if (mode === 'director') { this.director.lastCut = -99; this.director.spot = null; }
    this.onModeChange?.(mode);
  }

  /** The whole map from a high angle. intro: swoop in from far away. */
  overview(intro = false) {
    const { W, H } = this.terrain;
    this.goal.target.set(W / 2, 0, H / 2 + Math.max(W, H) * 0.04);
    this.goal.yaw = this.cur.yaw && !intro ? this.cur.yaw : 0.5;
    this.goal.pitch = 0.92;
    this.goal.dist = Math.max(W, H) * 0.95;
    if (intro) {
      this.cur.target.copy(this.goal.target);
      this.cur.yaw = this.goal.yaw - 1.6;
      this.cur.pitch = 0.35;
      this.cur.dist = this.goal.dist * 1.8;
      this.rate = 1.1;
    }
    if (this.mode !== 'orbit') this.setMode('orbit');
  }

  focus(x, z, dist = 7) {
    this.goal.target.set(x, this.terrain.heightAt(x, z), z);
    this.goal.dist = dist;
    this.goal.pitch = clamp(this.goal.pitch, 0.45, 0.9);
    this.rate = 4;
  }

  zoom(factor) {
    if (this.mode === 'fly') { this.fly.pos.addScaledVector(this.forward(), (1 - factor) * 6); return; }
    this.goal.dist = clamp(this.goal.dist * factor, 1.2, this.maxDist);
    this.rate = 9;
  }

  follow(fn) { this.followFn = fn; this.setMode('follow'); this.goal.dist = Math.min(this.goal.dist, 4.5); this.goal.pitch = clamp(this.goal.pitch, 0.35, 0.7); this.rate = 4; }

  startDirector(fn) { this.directorFn = fn; this.setMode('director'); }

  enterFly() {
    // Start from wherever the camera is now, looking the same way.
    this.fly.pos.copy(this.camera.position);
    const d = new THREE.Vector3();
    this.camera.getWorldDirection(d);
    this.fly.yaw = Math.atan2(-d.x, -d.z);
    this.fly.pitch = Math.asin(clamp(d.y, -1, 1));
    this.fly.vel.set(0, 0, 0);
    // Keys must reach the view even when flight was started from a toolbar button.
    this.dom.focus({ preventScroll: true });
    this.dom.requestPointerLock?.()?.catch?.(() => {});
  }

  leaveFly() {
    // Orbit around the ground point the camera was looking at.
    const f = this.forward();
    const p = this.fly.pos;
    let t = 0;
    for (let s = 0; s < 120; s += 0.5) {
      const x = p.x + f.x * s, y = p.y + f.y * s, z = p.z + f.z * s;
      if (y <= this.terrain.heightAt(x, z)) { t = s; break; }
    }
    if (!t) t = 8;
    const hit = p.clone().addScaledVector(f, t);
    const off = p.clone().sub(hit);
    const dist = clamp(off.length(), 1.2, this.maxDist);
    this.goal.target.copy(hit); this.cur.target.copy(hit);
    this.goal.dist = this.cur.dist = dist;
    this.goal.yaw = this.cur.yaw = Math.atan2(off.x, off.z);
    this.goal.pitch = this.cur.pitch = clamp(Math.asin(clamp(off.y / dist, -1, 1)), MIN_PITCH, MAX_PITCH);
  }

  forward() {
    const cp = Math.cos(this.fly.pitch);
    return new THREE.Vector3(-Math.sin(this.fly.yaw) * cp, Math.sin(this.fly.pitch), -Math.cos(this.fly.yaw) * cp);
  }

  // ---------------------------------------------------------------- input

  bind() {
    const dom = this.dom;
    const pointers = new Map();
    let drag = null;
    this.handlers = [];
    const on = (el, ev, fn, opt) => { el.addEventListener(ev, fn, opt); this.handlers.push([el, ev, fn, opt]); };

    on(dom, 'contextmenu', (e) => e.preventDefault());
    on(dom, 'pointerdown', (e) => {
      dom.focus({ preventScroll: true });
      if (this.mode === 'fly' && document.pointerLockElement === dom) return;
      pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
      dom.setPointerCapture(e.pointerId);
      const rotate = e.button === 2 || e.button === 1 || e.shiftKey || e.altKey;
      drag = { rotate, moved: 0, x: e.clientX, y: e.clientY, pinch: null };
      if (pointers.size === 2) {
        const [a, b] = [...pointers.values()];
        drag.pinch = { d: Math.hypot(a.x - b.x, a.y - b.y), ang: Math.atan2(b.y - a.y, b.x - a.x), mx: (a.x + b.x) / 2, my: (a.y + b.y) / 2 };
      }
    });
    on(dom, 'pointermove', (e) => {
      if (this.mode === 'fly' && document.pointerLockElement === dom) {
        this.fly.yaw -= e.movementX * 0.0022;
        this.fly.pitch = clamp(this.fly.pitch - e.movementY * 0.0022, -1.5, 1.5);
        return;
      }
      const prev = pointers.get(e.pointerId);
      if (!prev || !drag) { this.onHover?.(e.clientX, e.clientY); return; }
      const dx = e.clientX - prev.x, dy = e.clientY - prev.y;
      prev.x = e.clientX; prev.y = e.clientY;
      drag.moved += Math.abs(dx) + Math.abs(dy);
      if (drag.moved < 4) return;
      this.onHover?.(null);
      if (pointers.size === 2 && drag.pinch) {
        const [a, b] = [...pointers.values()];
        const d = Math.hypot(a.x - b.x, a.y - b.y), ang = Math.atan2(b.y - a.y, b.x - a.x);
        const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
        if (drag.pinch.d > 0) this.zoom(drag.pinch.d / d);
        this.turn((ang - drag.pinch.ang) * 300, (my - drag.pinch.my) * 0.6);
        drag.pinch = { d, ang, mx, my };
        return;
      }
      if (this.mode === 'fly') {
        // Pointer lock refused: drag to look instead.
        this.fly.yaw -= dx * 0.004;
        this.fly.pitch = clamp(this.fly.pitch - dy * 0.004, -1.5, 1.5);
        return;
      }
      if (drag.rotate || this.mode === 'follow') this.turn(dx, dy);
      else this.pan(dx, dy);
    });
    const up = (e) => {
      const was = drag;
      pointers.delete(e.pointerId);
      if (pointers.size === 0) {
        drag = null;
        if (was && was.moved < 4 && e.type === 'pointerup' && e.button === 0) this.onPick?.(e.clientX, e.clientY);
      }
    };
    on(dom, 'pointerup', up);
    on(dom, 'pointercancel', up);
    on(dom, 'pointerleave', () => this.onHover?.(null));
    on(dom, 'wheel', (e) => {
      e.preventDefault();
      const f = Math.exp((e.deltaMode === 1 ? e.deltaY * 0.05 : e.deltaY * 0.0012));
      if (this.mode === 'orbit' && f < 1) {
        // Zoom toward the point under the cursor.
        const hit = this.groundAt(e.clientX, e.clientY);
        if (hit) this.goal.target.lerp(hit, (1 - f) * 0.9);
      }
      if (this.mode === 'director') this.setMode('orbit');
      this.zoom(f);
    }, { passive: false });
    on(dom, 'keydown', (e) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const k = e.key.toLowerCase();
      if (['w', 'a', 's', 'd', 'q', 'e', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright', 'shift', 'z', 'x'].includes(k)) {
        this.keys.add(k);
        if (k.startsWith('arrow')) e.preventDefault();
        if (this.mode === 'director' && k !== 'shift') this.setMode('orbit');
      }
    });
    on(dom, 'keyup', (e) => this.keys.delete(e.key.toLowerCase()));
    on(dom, 'blur', () => this.keys.clear());
    on(document, 'pointerlockchange', () => {
      if (document.pointerLockElement !== dom && this.mode === 'fly') this.setMode('orbit');
    });
  }

  pan(dx, dy) {
    if (this.mode !== 'orbit') this.setMode('orbit');
    const s = (this.cur.dist * 1.6) / Math.max(200, this.dom.clientHeight);
    const sy = Math.sin(this.goal.yaw), cy = Math.cos(this.goal.yaw);
    this.goal.target.x += (-dx * cy - dy * sy / Math.max(0.35, Math.sin(this.cur.pitch))) * s;
    this.goal.target.z += (dx * sy - dy * cy / Math.max(0.35, Math.sin(this.cur.pitch))) * s;
    this.clampTarget();
    this.rate = 14;
  }

  turn(dx, dy) {
    if (this.mode === 'director') this.setMode('orbit');
    this.goal.yaw -= dx * 0.006;
    this.goal.pitch = clamp(this.goal.pitch + dy * 0.005, MIN_PITCH, MAX_PITCH);
    this.rate = 14;
  }

  clampTarget() {
    const { W, H } = this.terrain, t = this.goal.target;
    t.x = clamp(t.x, -6, W + 6);
    t.z = clamp(t.z, -6, H + 6);
  }

  /** The ground point under a screen position (ray-marched against the height field). */
  groundAt(clientX, clientY) {
    const r = this.dom.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    const ray = new THREE.Raycaster();
    ray.setFromCamera(ndc, this.camera);
    const o = ray.ray.origin, d = ray.ray.direction;
    let prev = 0;
    for (let s = 0.2; s < 400; s *= 1.04) {
      const x = o.x + d.x * s, y = o.y + d.y * s, z = o.z + d.z * s;
      if (y <= this.terrain.heightAt(x, z)) {
        // Refine between the last two samples.
        let a = prev, b = s;
        for (let i = 0; i < 8; i++) {
          const m = (a + b) / 2;
          if (o.y + d.y * m <= this.terrain.heightAt(o.x + d.x * m, o.z + d.z * m)) b = m; else a = m;
        }
        return new THREE.Vector3(o.x + d.x * b, o.y + d.y * b, o.z + d.z * b);
      }
      prev = s;
    }
    return null;
  }

  // ---------------------------------------------------------------- per frame

  update(dt, shake = 0) {
    this.time += dt;
    const k = this.keys;
    const fast = k.has('shift') ? 3 : 1;
    if (this.mode === 'fly') {
      const f = this.forward();
      const right = new THREE.Vector3(Math.cos(this.fly.yaw), 0, -Math.sin(this.fly.yaw));
      const want = new THREE.Vector3();
      if (k.has('w') || k.has('arrowup')) want.add(f);
      if (k.has('s') || k.has('arrowdown')) want.sub(f);
      if (k.has('d') || k.has('arrowright')) want.add(right);
      if (k.has('a') || k.has('arrowleft')) want.sub(right);
      if (k.has('e')) want.y += 1;
      if (k.has('q')) want.y -= 1;
      if (want.lengthSq() > 0) want.normalize().multiplyScalar(7 * fast);
      this.fly.vel.lerp(want, damp(6, dt));
      this.fly.pos.addScaledVector(this.fly.vel, dt);
      const floor = this.terrain.heightAt(this.fly.pos.x, this.fly.pos.z) + 0.25;
      if (this.fly.pos.y < floor) this.fly.pos.y = floor;
      this.fly.pos.y = Math.min(this.fly.pos.y, 80);
      this.camera.position.copy(this.fly.pos);
      this.camera.lookAt(this.fly.pos.clone().add(this.forward()));
      this.applyShake(shake, dt);
      return;
    }

    // Keyboard panning and turning (orbit and follow).
    const panSpeed = this.cur.dist * 0.9 * fast * dt;
    let px = 0, pz = 0;
    if (k.has('w') || k.has('arrowup')) pz -= 1;
    if (k.has('s') || k.has('arrowdown')) pz += 1;
    if (k.has('d') || k.has('arrowright')) px += 1;
    if (k.has('a') || k.has('arrowleft')) px -= 1;
    if ((px || pz) && this.mode === 'follow') this.setMode('orbit');
    if (px || pz) {
      const sy = Math.sin(this.goal.yaw), cy = Math.cos(this.goal.yaw);
      this.goal.target.x += (px * cy + pz * sy) * panSpeed;
      this.goal.target.z += (-px * sy + pz * cy) * panSpeed;
      this.clampTarget();
      this.rate = 10;
    }
    if (k.has('q')) { this.goal.yaw += 1.6 * dt; this.rate = 10; }
    if (k.has('e')) { this.goal.yaw -= 1.6 * dt; this.rate = 10; }
    if (k.has('z')) this.zoom(Math.exp(-1.5 * dt));
    if (k.has('x')) this.zoom(Math.exp(1.5 * dt));

    if (this.mode === 'follow') {
      const p = this.followFn?.();
      if (p) { this.goal.target.copy(p); this.rate = Math.max(this.rate, 6); }
      else this.setMode('orbit');
    } else if (this.mode === 'director') {
      this.updateDirector(dt);
    }

    // Ease toward the goal; the rate relaxes back to normal after a fast move.
    const a = damp(this.rate, dt);
    this.cur.target.lerp(this.goal.target, a);
    this.cur.yaw += wrapAngle(this.goal.yaw - this.cur.yaw) * a;
    this.cur.pitch += (this.goal.pitch - this.cur.pitch) * a;
    this.cur.dist += (this.goal.dist - this.cur.dist) * a;
    this.rate += (7 - this.rate) * damp(0.8, dt);

    const groundY = this.terrain.heightAt(this.cur.target.x, this.cur.target.z);
    const ty = this.mode === 'follow' ? this.cur.target.y : groundY;
    const look = new THREE.Vector3(this.cur.target.x, ty + 0.15, this.cur.target.z);
    const cp = Math.cos(this.cur.pitch);
    const pos = new THREE.Vector3(
      look.x + Math.sin(this.cur.yaw) * cp * this.cur.dist,
      look.y + Math.sin(this.cur.pitch) * this.cur.dist,
      look.z + Math.cos(this.cur.yaw) * cp * this.cur.dist);
    const floor = this.terrain.heightAt(pos.x, pos.z) + 0.35;
    if (pos.y < floor) pos.y = floor;
    this.camera.position.copy(pos);
    this.camera.lookAt(look);
    this.applyShake(shake, dt);
  }

  updateDirector(dt) {
    const d = this.director;
    const spot = this.directorFn?.();
    const now = this.time;
    if (spot) {
      const far = !d.spot || Math.hypot(spot.x - d.spot.x, spot.z - d.spot.z) > 9;
      const drift = d.spot && Math.hypot(spot.x - d.spot.x, spot.z - d.spot.z) > 2.5;
      if ((far && now - d.lastCut > 5) || (!far && drift)) {
        if (far) {
          d.lastCut = now;
          // A new scene: vary the angle and framing.
          this.goal.yaw = this.cur.yaw + (Math.random() < 0.5 ? -1 : 1) * (0.6 + Math.random() * 0.9);
          this.goal.pitch = 0.32 + Math.random() * 0.3;
          this.rate = 1.6;
        }
        d.spot = { x: spot.x, z: spot.z };
      }
      this.goal.dist = clamp(3.5 + Math.sqrt(spot.size || 1) * 1.25, 4.5, 12);
    } else if (!d.spot) {
      d.spot = { x: this.terrain.W / 2, z: this.terrain.H / 2 };
      this.goal.dist = Math.max(this.terrain.W, this.terrain.H) * 0.6;
    }
    if (d.spot) this.goal.target.set(d.spot.x, 0, d.spot.z);
    this.goal.yaw += d.yawSpeed * dt;
    this.rate = Math.min(this.rate, 2.2);
  }

  applyShake(amount, dt) {
    if (amount <= 0.001) return;
    const s = amount * 0.08;
    this.camera.position.x += (Math.random() - 0.5) * s;
    this.camera.position.y += (Math.random() - 0.5) * s;
    this.camera.position.z += (Math.random() - 0.5) * s;
  }

  dispose() {
    for (const [el, ev, fn, opt] of this.handlers) el.removeEventListener(ev, fn, opt);
    if (document.pointerLockElement === this.dom) document.exitPointerLock();
  }
}

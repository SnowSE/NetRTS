// 3D match view: particles, projectiles and explosions. Purely cosmetic: the frames say who is
// attacking whom and who lost hit points, and this file turns that into arrows, sparks, dust, smoke,
// fire, debris and shockwaves.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

const MAX_PARTICLES = 6000;

/** A pool of camera-facing soft sprites, updated on the CPU. */
class ParticlePool {
  constructor(additive) {
    this.n = 0;
    this.pos = new Float32Array(MAX_PARTICLES * 3);
    this.col = new Float32Array(MAX_PARTICLES * 4);
    this.size = new Float32Array(MAX_PARTICLES);
    this.vel = new Float32Array(MAX_PARTICLES * 3);
    this.life = new Float32Array(MAX_PARTICLES);
    this.max = new Float32Array(MAX_PARTICLES);
    this.grow = new Float32Array(MAX_PARTICLES);
    this.size0 = new Float32Array(MAX_PARTICLES);
    this.grav = new Float32Array(MAX_PARTICLES);
    this.drag = new Float32Array(MAX_PARTICLES);
    this.alpha0 = new Float32Array(MAX_PARTICLES);
    this.rgb = new Float32Array(MAX_PARTICLES * 3);
    this.cool = new Float32Array(MAX_PARTICLES);   // fire cools from yellow to red to smoke
    const geo = new THREE.BufferGeometry();
    this.aPos = new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage);
    this.aCol = new THREE.BufferAttribute(this.col, 4).setUsage(THREE.DynamicDrawUsage);
    this.aSize = new THREE.BufferAttribute(this.size, 1).setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('position', this.aPos);
    geo.setAttribute('pcolor', this.aCol);
    geo.setAttribute('psize', this.aSize);
    geo.setDrawRange(0, 0);
    this.material = new THREE.ShaderMaterial({
      uniforms: { uScale: { value: 600 } },
      vertexShader: `
        attribute vec4 pcolor; attribute float psize;
        uniform float uScale;
        varying vec4 vCol;
        void main() {
          vCol = pcolor;
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          gl_PointSize = clamp(psize * uScale / max(0.1, -mv.z), 0.0, 400.0);
        }`,
      fragmentShader: `
        varying vec4 vCol;
        void main() {
          vec2 p = gl_PointCoord * 2.0 - 1.0;
          float r = dot(p, p);
          if (r > 1.0) discard;
          float a = ${additive ? 'pow(1.0 - r, 1.6)' : 'smoothstep(1.0, 0.15, r)'};
          gl_FragColor = vec4(vCol.rgb${additive ? ' * a * vCol.a' : ''}, ${additive ? '1.0' : 'vCol.a * a'});
        }`,
      transparent: true,
      depthWrite: false,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
    });
    this.points = new THREE.Points(geo, this.material);
    this.points.frustumCulled = false;
    this.points.renderOrder = additive ? 3 : 2;
    this.geo = geo;
  }

  emit(x, y, z, vx, vy, vz, { life = 1, size = 0.1, grow = 0, color = [1, 1, 1], alpha = 1, gravity = 0, drag = 0, cool = 0 } = {}) {
    if (this.n >= MAX_PARTICLES) return;
    const i = this.n++;
    this.pos.set([x, y, z], i * 3);
    this.vel.set([vx, vy, vz], i * 3);
    this.rgb.set(color, i * 3);
    this.life[i] = life; this.max[i] = life;
    this.size0[i] = size; this.grow[i] = grow;
    this.grav[i] = gravity; this.drag[i] = drag;
    this.alpha0[i] = alpha; this.cool[i] = cool;
  }

  update(dt) {
    let n = this.n;
    for (let i = 0; i < n; i++) {
      this.life[i] -= dt;
      if (this.life[i] <= 0) {
        // Swap the last live particle into this slot.
        n--;
        if (i !== n) this.move(n, i);
        i--;
        continue;
      }
      const t = 1 - this.life[i] / this.max[i];
      const d = Math.max(0, 1 - this.drag[i] * dt);
      const k = i * 3;
      this.vel[k] *= d; this.vel[k + 1] = this.vel[k + 1] * d - this.grav[i] * dt; this.vel[k + 2] *= d;
      this.pos[k] += this.vel[k] * dt; this.pos[k + 1] += this.vel[k + 1] * dt; this.pos[k + 2] += this.vel[k + 2] * dt;
      this.size[i] = this.size0[i] * (1 + this.grow[i] * t);
      let r = this.rgb[k], g = this.rgb[k + 1], b = this.rgb[k + 2];
      if (this.cool[i] > 0) {
        // Fire: white-yellow core cooling through orange to deep red.
        const c = Math.min(1, t * this.cool[i]);
        r *= 1 - c * 0.2; g *= 1 - c * 0.75; b *= 1 - c * 0.95;
      }
      const fadeIn = Math.min(1, t * 8);
      const a = this.alpha0[i] * fadeIn * (1 - t) * (1 - t * 0.3);
      const j = i * 4;
      this.col[j] = r; this.col[j + 1] = g; this.col[j + 2] = b; this.col[j + 3] = a;
    }
    this.n = n;
    this.geo.setDrawRange(0, n);
    this.aPos.needsUpdate = true;
    this.aCol.needsUpdate = true;
    this.aSize.needsUpdate = true;
  }

  move(from, to) {
    for (const [arr, w] of [[this.pos, 3], [this.vel, 3], [this.rgb, 3], [this.col, 4]]) {
      for (let c = 0; c < w; c++) arr[to * w + c] = arr[from * w + c];
    }
    for (const arr of [this.size, this.life, this.max, this.grow, this.size0, this.grav, this.drag, this.alpha0, this.cool]) arr[to] = arr[from];
  }

  clear() { this.n = 0; this.geo.setDrawRange(0, 0); }
}

/** A pool of instanced meshes driven by plain objects. */
class MeshPool {
  constructor(geo, mat, max) {
    this.mesh = new THREE.InstancedMesh(geo, mat, max);
    this.mesh.count = 0;
    this.mesh.frustumCulled = false;
    this.mesh.castShadow = true;
    this.max = max;
    this.items = [];
  }
  clear() { this.items.length = 0; this.mesh.count = 0; }
}

const rand = (a, b) => a + Math.random() * (b - a);

export class Effects {
  constructor(scene, terrain) {
    this.scene = scene;
    this.terrain = terrain;
    this.glow = new ParticlePool(true);
    this.soft = new ParticlePool(false);
    scene.add(this.glow.points, this.soft.points);
    this.shake = 0;
    this.emitters = [];          // smoke columns and the like that outlive a single burst

    // Arrows: a thin shaft with a steel head and white fletching.
    const shaft = new THREE.BoxGeometry(0.012, 0.012, 0.3);
    const tip = new THREE.ConeGeometry(0.018, 0.05, 4).rotateX(Math.PI / 2).translate(0, 0, 0.17);
    const fl = new THREE.BoxGeometry(0.004, 0.03, 0.06).translate(0, 0, -0.13);
    const arrowGeo = mergeGeometries([shaft, tip, fl].map((g) => { g.deleteAttribute('uv'); return g; }));
    this.arrows = new MeshPool(arrowGeo, new THREE.MeshStandardMaterial({ color: '#d8c39a', roughness: 0.6, emissive: '#3a2a10' }), 512);
    scene.add(this.arrows.mesh);

    // Debris: chunks of masonry and timber.
    const chunk = new THREE.DodecahedronGeometry(0.05, 0);
    this.debris = new MeshPool(chunk, new THREE.MeshStandardMaterial({ roughness: 0.9, flatShading: true }), 700);
    this.debris.mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(700 * 3), 3);
    scene.add(this.debris.mesh);

    // Shockwave rings and spawn rings.
    this.rings = [];
    this.ringGeo = new THREE.RingGeometry(0.93, 1, 64).rotateX(-Math.PI / 2);

    // Scorch marks left by destroyed buildings.
    this.scorchTex = radialTexture([[0, 'rgba(10,8,6,0.85)'], [0.55, 'rgba(25,18,12,0.55)'], [1, 'rgba(0,0,0,0)']]);
    this.scorches = [];

    // One light for explosions, always present so materials never recompile when it fires.
    this.flash = new THREE.PointLight('#ffb070', 0, 9, 1.6);
    scene.add(this.flash);
    this.tmp = new THREE.Object3D();
    this.onSound = null;         // (kind, position) => void: the 3D view plays a sound for some effects
  }

  // ---------------------------------------------------------------- bursts

  /** Sparks where a blade or arrow lands. */
  spark(p, n = 8, colour = [2.2, 1.7, 0.9], s = 1) {
    for (let i = 0; i < n; i++) {
      this.glow.emit(p.x, p.y, p.z, rand(-1.4, 1.4) * s, rand(0.4, 2.0) * s, rand(-1.4, 1.4) * s,
        { life: rand(0.18, 0.4), size: rand(0.03, 0.06) * s, color: colour, gravity: 6 * s, drag: 2 });
    }
    this.glow.emit(p.x, p.y, p.z, 0, 0, 0, { life: 0.12, size: 0.35 * s, color: [1, 0.85, 0.6], alpha: 0.8 });
  }

  /** A blow landing on a wall: a flash, sparks, and chips of brick and dust thrown back. */
  chip(p, s = 1) {
    this.onSound?.('chip', p);
    this.spark(p, 10, [2.2, 1.6, 0.8], s * 1.3);
    for (let i = 0; i < 5; i++) {
      this.soft.emit(p.x, p.y, p.z, rand(-1.2, 1.2) * s, rand(0.6, 1.8) * s, rand(-1.2, 1.2) * s,
        { life: rand(0.35, 0.6), size: rand(0.04, 0.07) * s, color: [0.45, 0.25, 0.18], alpha: 0.95, gravity: 7 * s, drag: 1 });
    }
    this.dust(p, 3, [0.6, 0.52, 0.44], 0.5 * s);
  }

  /** A puff of dust at a unit's feet. */
  dust(p, n = 4, colour = [0.62, 0.55, 0.44], spread = 0.4) {
    for (let i = 0; i < n; i++) {
      this.soft.emit(p.x + rand(-0.1, 0.1), p.y + 0.03, p.z + rand(-0.1, 0.1), rand(-spread, spread), rand(0.1, 0.35), rand(-spread, spread),
        { life: rand(0.5, 1.0), size: rand(0.12, 0.2), grow: 1.8, color: colour, alpha: 0.4, drag: 2.5 });
    }
  }

  /** A unit falls: a burst in its team colour and a cloud of dust. */
  death(p, teamRGB) {
    this.onSound?.('death', p);
    for (let i = 0; i < 14; i++) {
      this.glow.emit(p.x, p.y + 0.2, p.z, rand(-1.2, 1.2), rand(0.5, 2.2), rand(-1.2, 1.2),
        { life: rand(0.3, 0.6), size: rand(0.04, 0.08), color: teamRGB, gravity: 5, drag: 1.5 });
    }
    this.dust(p, 8, [0.55, 0.5, 0.42], 0.7);
    this.ring(p, teamRGB, 0.9, 0.45, 0.6);
  }

  /** A newly trained unit appears in a column of light. */
  spawn(p, teamRGB) {
    this.onSound?.('spawn', p);
    this.ring(p, teamRGB, 0.6, 0.6, 0.9);
    for (let i = 0; i < 10; i++) {
      const a = (i / 10) * Math.PI * 2;
      this.glow.emit(p.x + Math.cos(a) * 0.22, p.y + 0.02, p.z + Math.sin(a) * 0.22, 0, rand(0.6, 1.2), 0,
        { life: rand(0.5, 0.8), size: 0.06, color: teamRGB, drag: 1 });
    }
  }

  /** A building is finished: a flash of the seam colour from top to bottom. */
  completed(p, teamRGB, height) {
    this.onSound?.('complete', p);
    this.ring(p, teamRGB, 1.4, 0.8, 0.7);
    for (let i = 0; i < 24; i++) {
      this.glow.emit(p.x + rand(-0.45, 0.45), p.y + rand(0, height), p.z + rand(-0.45, 0.45), 0, rand(0.2, 0.6), 0,
        { life: rand(0.5, 1.0), size: rand(0.04, 0.08), color: [1, 0.9, 0.6], drag: 1 });
    }
  }

  /** A building is destroyed: fireball, debris, shockwave, a smoke column and a scorch mark. */
  explosion(p, teamRGB, big = true) {
    this.onSound?.(big ? 'explosionBig' : 'explosion', p);
    const s = big ? 1 : 0.55;
    for (let i = 0; i < 70 * s; i++) {
      const a = Math.random() * Math.PI * 2, u = Math.random();
      const sp = rand(0.5, 3.2) * s;
      this.glow.emit(p.x, p.y + 0.3, p.z, Math.cos(a) * sp * Math.sqrt(1 - u * u), rand(0.6, 3.5) * s, Math.sin(a) * sp * Math.sqrt(1 - u * u),
        { life: rand(0.5, 1.1), size: rand(0.25, 0.5) * s, grow: 1.2, color: [3.2, 2.6, 1.8], cool: 1.6, drag: 2.4, gravity: -0.6 });
    }
    for (let i = 0; i < 30 * s; i++) {
      this.glow.emit(p.x, p.y + 0.3, p.z, rand(-4, 4) * s, rand(1, 5) * s, rand(-4, 4) * s,
        { life: rand(0.5, 1.2), size: rand(0.03, 0.06), color: [1, 0.7, 0.3], gravity: 7, drag: 0.6 });
    }
    for (let i = 0; i < 30 * s; i++) {
      this.soft.emit(p.x + rand(-0.3, 0.3), p.y + rand(0.1, 0.6), p.z + rand(-0.3, 0.3), rand(-0.8, 0.8), rand(0.3, 1.3), rand(-0.8, 0.8),
        { life: rand(1.6, 3.2), size: rand(0.35, 0.6) * s, grow: 2.5, color: [0.16, 0.14, 0.13], alpha: 0.7, drag: 1.2 });
    }
    const greys = [[0.3, 0.27, 0.23], [0.2, 0.18, 0.16], [0.26, 0.16, 0.09], teamRGB.map((c) => c * 0.6)];
    for (let i = 0; i < 26 * s; i++) {
      if (this.debris.items.length >= this.debris.max) break;
      const c = greys[i % greys.length];
      this.debris.items.push({
        x: p.x + rand(-0.3, 0.3), y: p.y + rand(0.2, 0.6), z: p.z + rand(-0.3, 0.3),
        vx: rand(-2.6, 2.6) * s, vy: rand(1.5, 4.5) * s, vz: rand(-2.6, 2.6) * s,
        rx: rand(0, 6), ry: rand(0, 6), spin: rand(-12, 12), size: rand(0.6, 1.8) * s, life: rand(2.5, 4), c,
      });
    }
    this.ring(p, [1, 0.7, 0.4], 3.2 * s, 0.6, 0.55);
    this.ring(p, [1, 0.95, 0.85], 1.6 * s, 0.3, 0.5);
    this.emitters.push({ kind: 'smoke', x: p.x, y: p.y + 0.3, z: p.z, life: big ? 7 : 4, rate: big ? 22 : 12, acc: 0 });
    this.scorch(p, big ? 1.1 : 0.7);
    this.flash.position.set(p.x, p.y + 1.2, p.z);
    this.flash.intensity = big ? 40 : 18;
    this.shake = Math.max(this.shake, big ? 1 : 0.5);
  }

  ring(p, rgb, radius, life, alpha) {
    const mat = new THREE.MeshBasicMaterial({ color: new THREE.Color(...rgb), transparent: true, opacity: alpha, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide });
    const mesh = new THREE.Mesh(this.ringGeo, mat);
    mesh.position.set(p.x, p.y + 0.04, p.z);
    mesh.renderOrder = 3;
    this.scene.add(mesh);
    this.rings.push({ mesh, radius, life, max: life, alpha });
  }

  scorch(p, r) {
    const mesh = new THREE.Mesh(new THREE.PlaneGeometry(2 * r, 2 * r).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ map: this.scorchTex, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 }));
    mesh.position.set(p.x, p.y + 0.02, p.z);
    mesh.rotation.y = Math.random() * Math.PI;
    mesh.renderOrder = 1;
    this.scene.add(mesh);
    this.scorches.push(mesh);
    // Siege shells leave a lot of these: past a limit the oldest fade from the record.
    if (this.scorches.length > 90) {
      const old = this.scorches.shift();
      this.scene.remove(old); old.geometry.dispose(); old.material.dispose();
    }
  }

  // ---------------------------------------------------------------- projectiles

  /** An arrow lobbed from a to b, landing after `flight` seconds. */
  arrow(a, b, flight, s = 1) {
    if (this.arrows.items.length >= this.arrows.max) return;
    const dist = Math.hypot(b.x - a.x, b.z - a.z);
    this.onSound?.('arrow', a);
    this.arrows.items.push({ a: a.clone(), b: b.clone(), t: 0, flight: Math.max(0.12, flight), arc: 0.15 + dist * 0.12, kind: 'arrow', s });
  }

  /** A guard tower fires: a flare at the battlements, then a fireball streaking down on its target. */
  bolt(a, b, flight) {
    this.onSound?.('tower', a);
    this.glow.emit(a.x, a.y, a.z, 0, 0, 0, { life: 0.22, size: 0.7, color: [2.6, 1.7, 0.8], alpha: 0.9 });
    this.glow.emit(a.x, a.y, a.z, 0, 0, 0, { life: 0.1, size: 0.3, color: [3, 2.8, 2.4] });
    for (let i = 0; i < 12; i++) {
      this.glow.emit(a.x, a.y, a.z, rand(-1.2, 1.2), rand(0, 1.4), rand(-1.2, 1.2),
        { life: rand(0.2, 0.45), size: rand(0.03, 0.06), color: [2.4, 1.5, 0.6], gravity: 3, drag: 2, cool: 1.2 });
    }
    this.soft.emit(a.x, a.y + 0.05, a.z, 0, 0.3, 0, { life: 0.8, size: 0.18, grow: 2.5, color: [0.25, 0.23, 0.22], alpha: 0.4, drag: 1 });
    this.ring(a, [1, 0.65, 0.3], 0.7, 0.3, 0.7);
    const dist = Math.hypot(b.x - a.x, b.z - a.z);
    this.arrows.items.push({ a: a.clone(), b: b.clone(), t: 0, flight: Math.max(0.1, flight), arc: 0.2 + dist * 0.06, kind: 'bolt', last: a.clone() });
  }

  /** A tower's fireball lands: a flash, a burst of fire and sparks, a ring of heat and a puff of smoke. */
  blast(p) {
    this.onSound?.('blast', p);
    this.spark(p, 18, [2.6, 1.5, 0.5], 1.2);
    for (let i = 0; i < 10; i++) {
      this.glow.emit(p.x + rand(-0.08, 0.08), p.y + rand(0, 0.1), p.z + rand(-0.08, 0.08), rand(-0.6, 0.6), rand(0.3, 1.1), rand(-0.6, 0.6),
        { life: rand(0.3, 0.55), size: rand(0.12, 0.22), grow: 0.8, color: [2.6, 1.9, 1.0], cool: 1.6, drag: 3 });
    }
    for (let i = 0; i < 4; i++) {
      this.soft.emit(p.x + rand(-0.1, 0.1), p.y + 0.05, p.z + rand(-0.1, 0.1), rand(-0.3, 0.3), rand(0.3, 0.6), rand(-0.3, 0.3),
        { life: rand(0.9, 1.4), size: rand(0.14, 0.22), grow: 2.5, color: [0.2, 0.18, 0.17], alpha: 0.45, drag: 1.5 });
    }
    this.ring(this.groundAt(p), [1, 0.55, 0.2], 0.9, 0.4, 0.75);
    // Borrow the explosion light unless a building is going up.
    if (this.flash.intensity < 6) {
      this.flash.position.set(p.x, p.y + 0.5, p.z);
      this.flash.intensity = 6;
    }
  }

  // ---------------------------------------------------------------- siege (the view flies the shells)

  /**
   * A siege engine fires from p: a cannon (Snow College) flashes and belches smoke along its barrel
   * (dir, a unit vector); a trebuchet's arm thumps into its stop and shakes dust from the frame.
   */
  shellLaunch(p, cannon, dir) {
    this.onSound?.(cannon ? 'cannon' : 'siege', p);
    if (cannon) {
      this.glow.emit(p.x, p.y, p.z, 0, 0, 0, { life: 0.12, size: 0.55, color: [3, 2.6, 1.8] });
      for (let i = 0; i < 14; i++) {
        const s = rand(1, 3.5);
        this.glow.emit(p.x, p.y, p.z, dir.x * s + rand(-0.4, 0.4), dir.y * s + rand(-0.2, 0.5), dir.z * s + rand(-0.4, 0.4),
          { life: rand(0.12, 0.3), size: rand(0.08, 0.16), grow: 0.6, color: [3, 1.9, 0.8], cool: 2, drag: 5 });
      }
      for (let i = 0; i < 8; i++) {
        const s = rand(0.4, 1.4);
        this.soft.emit(p.x, p.y, p.z, dir.x * s + rand(-0.2, 0.2), dir.y * s + rand(0.1, 0.4), dir.z * s + rand(-0.2, 0.2),
          { life: rand(1.2, 2), size: rand(0.12, 0.2), grow: 2.8, color: [0.72, 0.7, 0.68], alpha: 0.55, drag: 2 });
      }
      if (this.flash.intensity < 5) {
        this.flash.position.set(p.x, p.y + 0.2, p.z);
        this.flash.intensity = 5;
      }
    } else {
      this.dust(this.groundAt(p), 6, [0.62, 0.55, 0.44], 0.5);
    }
  }

  /** A shell's trail as it flies: a wisp of smoke behind a cannonball, grit behind a stone. */
  shellTrail(p, cannon) {
    if (cannon) this.soft.emit(p.x, p.y, p.z, rand(-0.05, 0.05), rand(0, 0.1), rand(-0.05, 0.05), { life: rand(0.5, 0.8), size: 0.07, grow: 2.2, color: [0.5, 0.48, 0.46], alpha: 0.3, drag: 1 });
    else this.soft.emit(p.x, p.y, p.z, 0, 0, 0, { life: 0.3, size: 0.04, grow: 1, color: [0.55, 0.5, 0.42], alpha: 0.35 });
  }

  /**
   * A shell lands on p (on the ground): a heavy thud, a crater of thrown earth and stones, a shockwave
   * and a shake; a cannonball also bursts in a short fireball.
   */
  shellImpact(p, cannon, s = 1) {
    this.onSound?.('shellHit', p);
    if (cannon) {
      this.onSound?.('explosion', p);
      for (let i = 0; i < 26 * s; i++) {
        const a = Math.random() * Math.PI * 2, sp = rand(0.4, 2) * s;
        this.glow.emit(p.x, p.y + 0.1, p.z, Math.cos(a) * sp, rand(0.5, 2.2) * s, Math.sin(a) * sp,
          { life: rand(0.35, 0.7), size: rand(0.16, 0.32) * s, grow: 1, color: [3, 2.3, 1.4], cool: 1.8, drag: 2.6, gravity: -0.4 });
      }
    }
    this.spark(p, 10, [2.2, 1.6, 0.8], 1.2 * s);
    // Earth thrown up in a ring, then a hanging cloud of dust.
    for (let i = 0; i < 24 * s; i++) {
      const a = Math.random() * Math.PI * 2, sp = rand(0.6, 2) * s;
      this.soft.emit(p.x, p.y + 0.05, p.z, Math.cos(a) * sp, rand(1, 3) * s, Math.sin(a) * sp,
        { life: rand(0.5, 0.9), size: rand(0.04, 0.08) * s, color: [0.4, 0.31, 0.22], alpha: 0.95, gravity: 7, drag: 0.8 });
    }
    this.dust(p, 14, [0.6, 0.53, 0.42], 1.1 * s);
    const clods = [[0.38, 0.3, 0.22], [0.3, 0.24, 0.17], [0.5, 0.47, 0.42]];
    for (let i = 0; i < 9 * s; i++) {
      if (this.debris.items.length >= this.debris.max) break;
      this.debris.items.push({
        x: p.x + rand(-0.1, 0.1), y: p.y + 0.08, z: p.z + rand(-0.1, 0.1),
        vx: rand(-1.8, 1.8) * s, vy: rand(1.5, 3.5) * s, vz: rand(-1.8, 1.8) * s,
        rx: rand(0, 6), ry: rand(0, 6), spin: rand(-10, 10), size: rand(0.5, 1.1) * s, life: rand(1.5, 2.5), c: clods[i % clods.length],
      });
    }
    this.ring(p, [1, 0.85, 0.6], 1.5 * s, 0.45, 0.55);
    this.scorch(p, cannon ? 0.42 : 0.32);
    if (this.flash.intensity < 10) {
      this.flash.position.set(p.x, p.y + 0.6, p.z);
      this.flash.intensity = cannon ? 10 : 4;
    }
    this.shake = Math.max(this.shake, 0.3 * s);
  }

  groundAt(p) { return new THREE.Vector3(p.x, this.terrain.heightAt(p.x, p.z), p.z); }

  // ---------------------------------------------------------------- continuous

  /** Called every frame for damaged buildings: smoke, and fire when badly hurt. */
  burning(p, severity, dt) {
    if (Math.random() < dt * 14 * severity) {
      this.soft.emit(p.x + rand(-0.25, 0.25), p.y + rand(0.3, 0.7), p.z + rand(-0.25, 0.25), rand(-0.1, 0.1) + 0.12, rand(0.4, 0.8), rand(-0.1, 0.1),
        { life: rand(1.8, 3.2), size: rand(0.18, 0.3), grow: 3, color: [0.18, 0.17, 0.17], alpha: 0.5 * severity + 0.15, drag: 0.4 });
    }
    if (severity > 0.5 && Math.random() < dt * 30 * severity) {
      this.glow.emit(p.x + rand(-0.25, 0.25), p.y + rand(0.25, 0.6), p.z + rand(-0.25, 0.25), rand(-0.1, 0.1), rand(0.4, 1.0), rand(-0.1, 0.1),
        { life: rand(0.3, 0.6), size: rand(0.12, 0.22), grow: -0.5, color: [2.4, 1.9, 1.1], cool: 1.4, drag: 1 });
    }
  }

  update(dt) {
    if (dt <= 0) return;   // paused: everything holds still, and nothing new is thrown off
    dt = Math.min(dt, 0.1);
    this.glow.update(dt);
    this.soft.update(dt);
    this.shake = Math.max(0, this.shake - dt * 1.8);
    this.flash.intensity *= Math.pow(0.002, dt);
    if (this.flash.intensity < 0.05) this.flash.intensity = 0;

    // Projectiles.
    const m = this.tmp;
    const arrows = this.arrows;
    let ai = 0;
    for (let i = 0; i < arrows.items.length; i++) {
      const p = arrows.items[i];
      p.t += dt / p.flight;
      if (p.t >= 1) {
        if (p.kind === 'bolt') this.blast(p.b);
        else { this.spark(p.b, 6); this.onSound?.('arrowHit', p.b); }
        continue;
      }
      const x = p.a.x + (p.b.x - p.a.x) * p.t, z = p.a.z + (p.b.z - p.a.z) * p.t;
      const y = p.a.y + (p.b.y - p.a.y) * p.t + Math.sin(Math.PI * p.t) * p.arc;
      if (p.kind === 'bolt') {
        // A white-hot core, and a burning tail laid evenly along the path flown since the last frame.
        this.glow.emit(x, y, z, 0, 0, 0, { life: 0.05, size: 0.24, color: [3, 2.7, 2.2] });
        const steps = Math.max(1, Math.ceil(Math.hypot(x - p.last.x, y - p.last.y, z - p.last.z) / 0.04));
        for (let k = 1; k <= steps; k++) {
          const f = k / steps;
          const tx = p.last.x + (x - p.last.x) * f, ty = p.last.y + (y - p.last.y) * f, tz = p.last.z + (z - p.last.z) * f;
          this.glow.emit(tx, ty, tz, rand(-0.1, 0.1), rand(0, 0.2), rand(-0.1, 0.1), { life: rand(0.2, 0.32), size: rand(0.12, 0.17), grow: -0.6, color: [2.6, 1.8, 0.8], cool: 1.5 });
          if (Math.random() < 0.3) {
            this.soft.emit(tx, ty, tz, rand(-0.1, 0.1), rand(0.1, 0.3), rand(-0.1, 0.1), { life: rand(0.5, 0.8), size: 0.08, grow: 2.5, color: [0.22, 0.2, 0.19], alpha: 0.3, drag: 1 });
          }
        }
        if (Math.random() < 0.5) {
          this.glow.emit(x, y, z, rand(-0.5, 0.5), rand(0, 0.6), rand(-0.5, 0.5), { life: rand(0.3, 0.5), size: 0.035, color: [2, 1.2, 0.4], gravity: 3, cool: 1 });
        }
        p.last.set(x, y, z);
      } else {
        const t2 = Math.min(1, p.t + 0.02);
        m.position.set(x, y, z);
        m.lookAt(p.a.x + (p.b.x - p.a.x) * t2, p.a.y + (p.b.y - p.a.y) * t2 + Math.sin(Math.PI * t2) * p.arc, p.a.z + (p.b.z - p.a.z) * t2);
        m.scale.setScalar(p.s ?? 1);
        // A faint streak behind it, so it reads in flight.
        this.glow.emit(x, y, z, 0, 0, 0, { life: 0.2, size: 0.016 * (p.s ?? 1), color: [1.8, 1.6, 1.2], alpha: 0.7 });
        m.updateMatrix();
        arrows.mesh.setMatrixAt(ai++, m.matrix);
      }
    }
    arrows.items = arrows.items.filter((p) => p.t < 1);
    arrows.mesh.count = ai;
    arrows.mesh.instanceMatrix.needsUpdate = true;

    // Debris tumbles, bounces and settles.
    const deb = this.debris;
    let di = 0;
    const colour = new THREE.Color();
    deb.items = deb.items.filter((d) => (d.life -= dt) > 0);
    for (const d of deb.items) {
      d.vy -= 9.8 * dt;
      d.x += d.vx * dt; d.y += d.vy * dt; d.z += d.vz * dt;
      const g = this.terrain.heightAt(d.x, d.z) + 0.03 * d.size;
      if (d.y < g) {
        d.y = g;
        d.vy = Math.abs(d.vy) * 0.3;
        d.vx *= 0.5; d.vz *= 0.5; d.spin *= 0.5;
        if (Math.abs(d.vy) < 0.3) d.vy = 0;
      }
      d.rx += d.spin * dt; d.ry += d.spin * 0.7 * dt;
      m.position.set(d.x, d.y, d.z);
      m.rotation.set(d.rx, d.ry, 0);
      m.scale.setScalar(d.size * Math.min(1, d.life / 0.6));
      m.updateMatrix();
      deb.mesh.setMatrixAt(di, m.matrix);
      deb.mesh.setColorAt(di, colour.setRGB(d.c[0], d.c[1], d.c[2]));
      di++;
    }
    deb.mesh.count = di;
    deb.mesh.instanceMatrix.needsUpdate = true;
    if (deb.mesh.instanceColor) deb.mesh.instanceColor.needsUpdate = true;

    // Rings expand and fade.
    this.rings = this.rings.filter((r) => {
      r.life -= dt;
      if (r.life <= 0) { this.scene.remove(r.mesh); r.mesh.material.dispose(); return false; }
      const t = 1 - r.life / r.max;
      const e = 1 - Math.pow(1 - t, 3);
      r.mesh.scale.setScalar(0.05 + r.radius * e);
      r.mesh.material.opacity = r.alpha * (1 - t);
      return true;
    });

    // Smoke columns.
    this.emitters = this.emitters.filter((e) => {
      e.life -= dt;
      e.acc += dt * e.rate * Math.min(1, e.life / 2);
      while (e.acc >= 1) {
        e.acc -= 1;
        this.soft.emit(e.x + rand(-0.2, 0.2), e.y, e.z + rand(-0.2, 0.2), rand(-0.1, 0.1) + 0.15, rand(0.6, 1.1), rand(-0.1, 0.1),
          { life: rand(2.5, 4), size: rand(0.25, 0.4), grow: 3.5, color: [0.13, 0.12, 0.12], alpha: 0.55, drag: 0.3 });
        if (Math.random() < 0.3) {
          this.glow.emit(e.x + rand(-0.2, 0.2), e.y - 0.15, e.z + rand(-0.2, 0.2), 0, rand(0.3, 0.7), 0,
            { life: rand(0.3, 0.6), size: rand(0.12, 0.2), color: [2.4, 1.6, 0.8], cool: 1.4 });
        }
      }
      return e.life > 0;
    });
  }

  /** The point size scale depends on the viewport height and field of view. */
  resize(heightPx, fovDeg) {
    const s = heightPx / (2 * Math.tan((fovDeg * Math.PI) / 360));
    this.glow.material.uniforms.uScale.value = s;
    this.soft.material.uniforms.uScale.value = s;
  }

  /** Scrubbing the replay: forget everything in flight. */
  clear() {
    this.glow.clear();
    this.soft.clear();
    this.arrows.clear();
    this.debris.clear();
    this.emitters.length = 0;
    this.rings.forEach((r) => { this.scene.remove(r.mesh); r.mesh.material.dispose(); });
    this.rings.length = 0;
    this.scorches.forEach((s) => { this.scene.remove(s); s.geometry.dispose(); s.material.dispose(); });
    this.scorches.length = 0;
    this.flash.intensity = 0;
    this.shake = 0;
  }

  dispose() {
    this.clear();
    for (const o of [this.glow.points, this.soft.points, this.arrows.mesh, this.debris.mesh, this.flash]) {
      this.scene.remove(o);
      o.geometry?.dispose();
      o.material?.dispose();
    }
    this.ringGeo.dispose();
    this.scorchTex.dispose();
  }
}

function radialTexture(stops) {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d');
  const gr = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  for (const [o, s] of stops) gr.addColorStop(o, s);
  g.fillStyle = gr;
  g.fillRect(0, 0, 128, 128);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

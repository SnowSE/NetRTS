// 3D match view: the land. Open tiles are a gently rolling valley floor; rock tiles rise into
// sandstone plateaus with layered cliffs; past the map's edge the ground climbs into mountains so the
// battlefield sits in a basin. Everything is derived from the map, so a match always looks the same.
import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { fbm, noise2, hash2, rng, smoothstep, clamp, lerp } from './noise.js';

const MARGIN = 44;          // tiles of scenery beyond each edge of the map

const C = (hex) => new THREE.Color(hex);
const PAL = {
  grassA: C('#5f8636'), grassB: C('#86a04a'), dry: C('#a59a5c'), sage: C('#7d9268'),
  dirt: C('#7f6547'), path: C('#9a8462'),
  strata: [C('#b8946a'), C('#9a7656'), C('#c8a77f'), C('#866d57'), C('#a9825c')],
  rockDark: C('#5e5248'), scrub: C('#6e7d45'), peak: C('#8d8a84'),
};

export class Terrain {
  /** relief scales the plateaus (1 = full height; lower keeps low camera shots clear). */
  constructor(map, { relief = 1 } = {}) {
    this.relief = relief;
    this.map = map;
    this.W = map.width;
    this.H = map.height;
    this.rock = new Uint8Array(this.W * this.H);
    let seed = 7;
    for (let y = 0; y < this.H; y++) {
      const row = map.terrain[y] || '';
      for (let x = 0; x < this.W; x++) {
        const r = row[x] === '#';
        this.rock[y * this.W + x] = r ? 1 : 0;
        if (r) seed = (seed * 31 + x * 7 + y * 13) | 0;
      }
    }
    this.seed = seed & 0xffff;
    this.group = new THREE.Group();
    this.uniforms = {
      uGrid: { value: 0 },
      uMapSize: { value: new THREE.Vector2(this.W, this.H) },
      uVis: { value: null },
      uVisOn: { value: 0 },
      uTime: { value: 0 },
    };
    this.buildGround();
    this.buildScenery();
  }

  isRock(tx, ty) {
    if (tx < 0 || ty < 0 || tx >= this.W || ty >= this.H) return true;
    return this.rock[ty * this.W + tx] === 1;
  }

  /** 0 on open ground .. 1 on rock, bilinear between tile centres. Off the map counts as rock. */
  rockiness(x, z) {
    const u = x - 0.5, v = z - 0.5;
    const x0 = Math.floor(u), z0 = Math.floor(v);
    const fx = u - x0, fz = v - z0;
    const a = this.isRock(x0, z0), b = this.isRock(x0 + 1, z0);
    const c = this.isRock(x0, z0 + 1), d = this.isRock(x0 + 1, z0 + 1);
    return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
  }

  outside(x, z) {
    const dx = Math.max(0, -x, x - this.W), dz = Math.max(0, -z, z - this.H);
    return Math.hypot(dx, dz);
  }

  /** How much of the plateau is here: 0 valley floor .. 1 plateau top. */
  cliff(x, z) {
    const s = this.seed;
    const jitter = (fbm(x * 0.45, z * 0.45, s) - 0.5) * 0.3;
    return smoothstep(0.37, 0.63, this.rockiness(x, z) + jitter);
  }

  heightAt(x, z) {
    const s = this.seed;
    const c = this.cliff(x, z);
    const ground = (fbm(x * 0.08, z * 0.08, s + 3) - 0.5) * 0.4;
    const plateau = 1.3 + fbm(x * 0.1, z * 0.1, s + 5) * 1.3 + (noise2(x * 1.1, z * 1.1, s + 9) - 0.5) * 0.14;
    let h = ground * (1 - c * 0.6) + c * plateau * this.relief;
    const d = this.outside(x, z);
    if (d > 0) {
      const k = smoothstep(0.5, 7, d);
      const ridges = 1 - Math.abs(noise2(x * 0.07, z * 0.07, s + 21) * 2 - 1);
      h += k * Math.pow(d, 1.12) * 0.4 * (0.45 + fbm(x * 0.05, z * 0.05, s + 11) * 0.8 + ridges * 0.45);
    }
    return h;
  }

  /** Grid coordinates: dense over the map, stretching out into the mountains. */
  static axis(len, res) {
    const out = [];
    const inner0 = -3, inner1 = len + 3;
    const outer = 22;
    for (let i = outer; i >= 1; i--) out.push(inner0 - (MARGIN + inner0) * Math.pow(i / outer, 1.6));
    const n = Math.round((inner1 - inner0) * res);
    for (let i = 0; i <= n; i++) out.push(inner0 + (i / n) * (inner1 - inner0));
    for (let i = 1; i <= outer; i++) out.push(inner1 + (MARGIN - 3) * Math.pow(i / outer, 1.6));
    return out;
  }

  buildGround() {
    const { W, H } = this;
    const res = clamp(Math.floor(560 / (Math.max(W, H) + 6)), 2, 5);
    const xs = Terrain.axis(W, res), zs = Terrain.axis(H, res);
    const nx = xs.length, nz = zs.length;
    const pos = new Float32Array(nx * nz * 3);
    const uv = new Float32Array(nx * nz * 2);
    const cliffs = new Float32Array(nx * nz);
    for (let j = 0; j < nz; j++) {
      for (let i = 0; i < nx; i++) {
        const k = j * nx + i, x = xs[i], z = zs[j];
        pos[k * 3] = x; pos[k * 3 + 1] = this.heightAt(x, z); pos[k * 3 + 2] = z;
        uv[k * 2] = x * 0.3; uv[k * 2 + 1] = z * 0.3;
        cliffs[k] = this.cliff(x, z);
      }
    }
    const idx = new Uint32Array((nx - 1) * (nz - 1) * 6);
    let t = 0;
    for (let j = 0; j < nz - 1; j++) {
      for (let i = 0; i < nx - 1; i++) {
        const a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
        // Alternate the diagonal so slopes don't all crease the same way.
        if ((i + j) & 1) { idx[t++] = a; idx[t++] = c; idx[t++] = b; idx[t++] = b; idx[t++] = c; idx[t++] = d; }
        else { idx[t++] = a; idx[t++] = c; idx[t++] = d; idx[t++] = a; idx[t++] = d; idx[t++] = b; }
      }
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.setIndex(new THREE.BufferAttribute(idx, 1));
    geo.computeVertexNormals();

    const nrm = geo.attributes.normal.array;
    const col = new Float32Array(nx * nz * 3);
    const tmp = new THREE.Color(), tmp2 = new THREE.Color();
    const s = this.seed;
    for (let k = 0; k < nx * nz; k++) {
      const x = pos[k * 3], h = pos[k * 3 + 1], z = pos[k * 3 + 2];
      const slope = 1 - nrm[k * 3 + 1];
      const c = cliffs[k];
      const d = this.outside(x, z);
      // Valley floor: grass with dry patches and a little dirt.
      const patch = fbm(x * 0.13, z * 0.13, s + 31);
      tmp.copy(PAL.grassA).lerp(PAL.grassB, smoothstep(0.35, 0.7, patch));
      tmp.lerp(PAL.dry, smoothstep(0.62, 0.85, fbm(x * 0.06 + 40, z * 0.06, s + 41)) * 0.7);
      tmp.lerp(PAL.sage, smoothstep(0.55, 0.8, noise2(x * 0.5, z * 0.5, s + 7)) * 0.35);
      // Dirt gathers at the foot of the cliffs.
      tmp.lerp(PAL.dirt, smoothstep(0.02, 0.3, c) * 0.75);
      // Plateau tops: scrub.
      if (c > 0.5) {
        tmp2.copy(PAL.scrub).lerp(PAL.sage, noise2(x * 0.7, z * 0.7, s + 13));
        tmp.lerp(tmp2, smoothstep(0.7, 0.98, c));
      }
      // Cliff faces: banded sandstone.
      const rockMix = smoothstep(0.22, 0.42, slope);
      if (rockMix > 0) {
        const band = h * 2.1 + (noise2(x * 0.35, z * 0.35, s + 17) - 0.5) * 0.9;
        const L = PAL.strata.length;
        const b0 = ((Math.floor(band) % L) + L) % L;
        tmp2.copy(PAL.strata[b0]).lerp(PAL.strata[(b0 + 1) % L], smoothstep(0.75, 1, band - Math.floor(band)));
        tmp2.lerp(PAL.rockDark, smoothstep(0.55, 0.9, slope) * 0.45 + (noise2(x * 2.3, z * 2.3, s) - 0.5) * 0.2);
        tmp.lerp(tmp2, rockMix);
      }
      // High mountains turn grey.
      if (d > 0) tmp.lerp(PAL.peak, smoothstep(9, 22, h) * 0.8);
      // Tiny per-vertex variation breaks up flat colour.
      const v = 0.93 + hash2(Math.round(x * 7), Math.round(z * 7), s) * 0.12;
      col[k * 3] = tmp.r * v; col[k * 3 + 1] = tmp.g * v; col[k * 3 + 2] = tmp.b * v;
    }
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));

    const mat = new THREE.MeshStandardMaterial({
      vertexColors: true, roughness: 0.94, metalness: 0, map: detailTexture(),
    });
    const u = this.uniforms;
    mat.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, u);
      sh.vertexShader = sh.vertexShader
        .replace('#include <common>', '#include <common>\nvarying vec3 vWorld; varying vec3 vWorldN;')
        .replace('#include <worldpos_vertex>', `#include <worldpos_vertex>
          vWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;
          vWorldN = normalize(mat3(modelMatrix) * objectNormal);`);
      sh.fragmentShader = sh.fragmentShader
        // Triplanar detail texture, so cliff faces aren't smeared.
        .replace('#include <map_fragment>', `
          #ifdef USE_MAP
            vec3 bw = pow(abs(normalize(vWorldN)), vec3(4.0));
            bw /= bw.x + bw.y + bw.z;
            vec4 sampledDiffuseColor = texture2D(map, vWorld.zy * 0.3) * bw.x + texture2D(map, vWorld.xz * 0.3) * bw.y
              + texture2D(map, vWorld.xy * 0.3) * bw.z;
            diffuseColor *= sampledDiffuseColor;
          #endif`)
        .replace('#include <common>', `#include <common>
          varying vec3 vWorld; varying vec3 vWorldN;
          uniform float uGrid; uniform vec2 uMapSize; uniform sampler2D uVis; uniform float uVisOn; uniform float uTime;
          float hsh(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
          float vnoise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hsh(i), hsh(i + vec2(1.0, 0.0)), f.x), mix(hsh(i + vec2(0.0, 1.0)), hsh(i + vec2(1.0, 1.0)), f.x), f.y);
          }`)
        .replace('#include <color_fragment>', `#include <color_fragment>
          {
            vec2 p = vWorld.xz;
            float inMap = step(0.0, p.x) * step(0.0, p.y) * step(p.x, uMapSize.x) * step(p.y, uMapSize.y);
            // Tile grid, anti-aliased with screen-space derivatives.
            vec2 w = fwidth(p);
            vec2 f = fract(p);
            vec2 l = smoothstep(vec2(0.0), w * 1.2, f) * smoothstep(vec2(0.0), w * 1.2, 1.0 - f);
            float line = (1.0 - min(l.x, l.y)) * inMap * uGrid * clamp(1.6 - length(w) * 6.0, 0.0, 1.0);
            diffuseColor.rgb = mix(diffuseColor.rgb, vec3(1.0, 0.97, 0.88), line * 0.45);
            // The edge of the playing field.
            vec2 e = min(p, uMapSize - p);
            float edge = (1.0 - smoothstep(0.0, 0.18, abs(min(e.x, e.y)))) * step(-0.2, min(e.x, e.y));
            diffuseColor.rgb = mix(diffuseColor.rgb, vec3(1.0, 0.9, 0.6), edge * 0.2);
            // Cloud shadows drifting across the valley.
            vec2 wind = vec2(uTime * 0.35, uTime * 0.2);
            float cloud = vnoise((p + wind) * 0.045) * 0.65 + vnoise((p + wind * 1.4) * 0.11) * 0.35;
            diffuseColor.rgb *= 1.0 - smoothstep(0.55, 0.78, cloud) * 0.24;
            // Fog of war for the chosen player.
            if (uVisOn > 0.5 && inMap > 0.5) {
              float vis = texture2D(uVis, p / uMapSize).r;
              float g = dot(diffuseColor.rgb, vec3(0.3, 0.59, 0.11));
              diffuseColor.rgb = mix(vec3(g) * vec3(0.32, 0.36, 0.45), diffuseColor.rgb, vis);
            }
          }`);
    };
    const mesh = new THREE.Mesh(geo, mat);
    mesh.receiveShadow = true;
    mesh.castShadow = true;
    mesh.name = 'ground';
    this.ground = mesh;
    this.group.add(mesh);
  }

  /** Pines on the plateaus and mountainsides, boulders below the cliffs, sage brush on the valley floor. */
  buildScenery() {
    const { W, H } = this;
    const R = rng(this.seed * 7919 + 1);
    const starts = this.map.startPositions || [];
    const nearStart = (x, z, r) => starts.some((p) => Math.hypot(p.x + 0.5 - x, p.y + 0.5 - z) < r);
    const pines = [], rocks = [], bushes = [];
    const lo = -MARGIN + 4;
    const slopeAt = (x, z) => {
      const e = 0.25;
      return Math.hypot(this.heightAt(x + e, z) - this.heightAt(x - e, z), this.heightAt(x, z + e) - this.heightAt(x, z - e)) / (2 * e);
    };
    for (let z = lo; z < H + MARGIN - 4; z += 0.5) {
      for (let x = lo; x < W + MARGIN - 4; x += 0.5) {
        const far = this.outside(x, z) > 2;
        // Mountainsides are sampled more coarsely, with the density scaled up to match.
        if (far && ((Math.floor(x * 2) | Math.floor(z * 2)) & 1)) continue;
        const jx = x + (R() - 0.5) * 0.5, jz = z + (R() - 0.5) * 0.5;
        const d = this.outside(jx, jz);
        const c = this.cliff(jx, jz);
        const forest = fbm(jx * 0.09, jz * 0.09, this.seed + 61);
        const h = this.heightAt(jx, jz);
        const r = R();
        if (d > 1.5) {
          // Mountainside forest thins out with height.
          const slope = slopeAt(jx, jz);
          const dens = 4 * (d > 30 ? 0.02 : smoothstep(0.35, 0.65, forest) * 0.4 * (1 - smoothstep(10, 20, h)) * (slope < 1.3 ? 1 : 0.2));
          if (r < dens) pines.push([jx, h, jz, 1.1 + R() * 1.4]);
          else if (r < dens + 0.015 && slope < 1.8) rocks.push([jx, h, jz, 0.4 + R() * 0.9]);
        } else if (c > 0.92 && slopeAt(jx, jz) < 0.5) {
          if (r < 0.1 + smoothstep(0.4, 0.7, forest) * 0.25) pines.push([jx, h, jz, 0.7 + R() * 0.6]);
          else if (r < 0.2) bushes.push([jx, h, jz, 0.6 + R() * 0.6]);
        } else if (c > 0.15 && c < 0.6 && d === 0) {
          if (r < 0.06) rocks.push([jx, h, jz, 0.12 + R() * 0.22]);
        } else if (c < 0.05 && d === 0 && !nearStart(jx, jz, 5)) {
          if (r < 0.018) bushes.push([jx, h, jz, 0.35 + R() * 0.45]);
          else if (r < 0.022) rocks.push([jx, h, jz, 0.06 + R() * 0.08]);
        }
      }
    }
    this.group.add(pineForest(pines, R), boulderField(rocks, R), sageBrush(bushes, R));
  }

  /** Visibility rows ('1' seen / '0' fogged) for the fog-of-war overlay, or null to clear it. */
  setVisibility(rows) {
    if (!rows?.length) { this.uniforms.uVisOn.value = 0; return; }
    const { W, H } = this;
    if (!this.visTex) {
      this.visData = new Uint8Array(W * H * 4);
      this.visTex = new THREE.DataTexture(this.visData, W, H);
      this.visTex.magFilter = THREE.LinearFilter;
      this.visTex.minFilter = THREE.LinearFilter;
      this.uniforms.uVis.value = this.visTex;
    }
    for (let y = 0; y < H; y++) {
      const row = rows[y] || '';
      for (let x = 0; x < W; x++) {
        const v = row[x] === '0' ? 0 : 255;
        const k = (y * W + x) * 4;
        this.visData[k] = v; this.visData[k + 1] = v; this.visData[k + 2] = v; this.visData[k + 3] = 255;
      }
    }
    this.visTex.needsUpdate = true;
    this.uniforms.uVisOn.value = 1;
  }

  dispose() {
    this.group.traverse((o) => {
      o.geometry?.dispose();
      if (o.material) [].concat(o.material).forEach((m) => { m.map?.dispose(); m.dispose(); });
    });
    this.visTex?.dispose();
  }
}

/** Tileable grey speckle that gives the ground grain up close. */
function detailTexture() {
  const N = 256, P = 16;
  const c = document.createElement('canvas');
  c.width = c.height = N;
  const g = c.getContext('2d');
  const img = g.createImageData(N, N);
  const pnoise = (x, y, p, seed) => {
    const xi = Math.floor(x), yi = Math.floor(y);
    const f = (t) => t * t * (3 - 2 * t);
    const xf = f(x - xi), yf = f(y - yi);
    const h = (a, b) => hash2(((a % p) + p) % p, ((b % p) + p) % p, seed);
    const A = h(xi, yi), B = h(xi + 1, yi), Cc = h(xi, yi + 1), D = h(xi + 1, yi + 1);
    return lerp(lerp(A, B, xf), lerp(Cc, D, xf), yf);
  };
  for (let y = 0; y < N; y++) {
    for (let x = 0; x < N; x++) {
      let v = 0, amp = 0.5, norm = 0;
      for (let o = 0; o < 4; o++) {
        const p = P << o;
        v += pnoise((x / N) * p, (y / N) * p, p, o * 5) * amp;
        norm += amp; amp *= 0.5;
      }
      v /= norm;
      const speck = hash2(x, y, 99) > 0.95 ? -0.06 : 0;
      const l = clamp(0.84 + v * 0.22 + speck, 0, 1) * 255;
      const k = (y * N + x) * 4;
      img.data[k] = l; img.data[k + 1] = l; img.data[k + 2] = l; img.data[k + 3] = 255;
    }
  }
  g.putImageData(img, 0, 0);
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 8;
  return tex;
}

function instanced(geo, mat, items, place, colour) {
  const mesh = new THREE.InstancedMesh(geo, mat, Math.max(1, items.length));
  mesh.count = items.length;
  const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(), v = new THREE.Vector3(), s = new THREE.Vector3();
  items.forEach((it, i) => {
    place(it, v, e, s);
    m.compose(v, q.setFromEuler(e), s);
    mesh.setMatrixAt(i, m);
    if (colour) mesh.setColorAt(i, colour(it, i));
  });
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  mesh.computeBoundingSphere();
  return mesh;
}

function pineForest(items, R) {
  // Each tree's pose is drawn once and shared by its trunk and its foliage.
  const poses = items.map(([x, y, z, sc]) => ({
    x, y, z, sc, ex: (R() - 0.5) * 0.12, ey: R() * Math.PI * 2, ez: (R() - 0.5) * 0.12, sy: 0.85 + R() * 0.4,
    h: 0.3 + R() * 0.07, s: 0.28 + R() * 0.18, l: 0.1 + R() * 0.07,
  }));
  const place = (p, v, e, s) => { v.set(p.x, p.y - 0.03, p.z); e.set(p.ex, p.ey, p.ez); s.set(p.sc, p.sc * p.sy, p.sc); };
  const trunk = new THREE.CylinderGeometry(0.035, 0.05, 0.3, 5).translate(0, 0.15, 0);
  const cones = [[0.26, 0.4, 0.38], [0.2, 0.34, 0.58], [0.13, 0.28, 0.76]].map(([r, h, y]) =>
    new THREE.ConeGeometry(r, h, 7).translate(0, y, 0));
  const group = new THREE.Group();
  group.add(instanced(trunk, new THREE.MeshStandardMaterial({ color: '#5a4030', roughness: 0.95, flatShading: true }), poses, place));
  const tint = new THREE.Color();
  group.add(instanced(mergeGeometries(cones), new THREE.MeshStandardMaterial({ roughness: 0.9, flatShading: true }), poses, place,
    (p) => tint.setHSL(p.h, p.s, p.l).clone()));
  return group;
}

function boulderField(items, R) {
  const geo = new THREE.IcosahedronGeometry(1, 1);
  const p = geo.attributes.position;
  for (let i = 0; i < p.count; i++) {
    const k = 0.75 + hash2(Math.round(p.getX(i) * 50), Math.round(p.getY(i) * 50) + Math.round(p.getZ(i) * 31), 3) * 0.45;
    p.setXYZ(i, p.getX(i) * k, p.getY(i) * k * 0.7, p.getZ(i) * k);
  }
  geo.computeVertexNormals();
  const mat = new THREE.MeshStandardMaterial({ roughness: 0.95, flatShading: true });
  const tint = new THREE.Color();
  return instanced(geo, mat, items, ([x, y, z, sc], v, e, s) => {
    v.set(x, y + sc * 0.15, z); e.set(R() * 0.5, R() * Math.PI * 2, R() * 0.5); s.set(sc, sc, sc);
  }, () => tint.setHSL(0.07 + R() * 0.04, 0.1 + R() * 0.08, 0.26 + R() * 0.1).clone());
}

function sageBrush(items, R) {
  const parts = [];
  for (let i = 0; i < 4; i++) {
    const a = (i / 4) * Math.PI * 2;
    parts.push(new THREE.IcosahedronGeometry(0.12 + i * 0.015, 0).translate(Math.cos(a) * 0.08, 0.08 + (i % 2) * 0.04, Math.sin(a) * 0.08));
  }
  const geo = mergeGeometries(parts);
  const mat = new THREE.MeshStandardMaterial({ roughness: 1, flatShading: true });
  const tint = new THREE.Color();
  return instanced(geo, mat, items, ([x, y, z, sc], v, e, s) => {
    v.set(x, y, z); e.set(0, R() * Math.PI * 2, 0); s.set(sc, sc * 0.8, sc);
  }, () => tint.setHSL(0.18 + R() * 0.1, 0.18 + R() * 0.15, 0.36 + R() * 0.12).clone());
}

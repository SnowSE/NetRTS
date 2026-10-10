// Original soundtrack + sound effects for a cut, synthesised from scratch (no samples).
// node music.mjs short|long  -> audio-<cut>.wav, reading cues-<cut>.json from info.mjs.
import { readFileSync, writeFileSync } from 'node:fs';

const cut = process.argv[2] ?? 'short';
const { duration, cues } = JSON.parse(readFileSync(`cues-${cut}.json`, 'utf8'));
const SR = 48000, BPM = 120, BEAT = 60 / BPM, BAR = BEAT * 4, S16 = BEAT / 4;
const LEN = Math.ceil((duration + 1.5) * SR);
const L = new Float32Array(LEN), R = new Float32Array(LEN);
const verbL = new Float32Array(LEN), verbR = new Float32Array(LEN);   // reverb send
const dlyL = new Float32Array(LEN), dlyR = new Float32Array(LEN);     // delay send
const duck = new Float32Array(LEN).fill(1);                           // sidechain from the kick

let seed = 12345;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const noise = () => rnd() * 2 - 1;
const hz = (m) => 440 * Math.pow(2, (m - 69) / 12);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

// ---------- arrangement: section levels over time

const SECTIONS = {
  short: [
    [0, 2.0, { pad: 0.7, keys: 0.5 }],
    [2.0, 9.5, { pad: 0.8, arp: 1, kick: 1, bass: 1, hats: 1, snare: 1 }],
    [9.5, 15.5, { pad: 0.6, arp: 1, kick: 1, bass: 1, hats: 1, snare: 0.6 }],
    [15.5, 22.0, { pad: 0.7, arp: 0.8, kick: 1, bass: 0.9, hats: 0.8, snare: 1 }],
    [22.0, 26.5, { pad: 0.9, arp: 0.6, kick: 0.8, bass: 0.8, hats: 0.5 }],
    [26.5, 32, { pad: 1, keys: 0.6 }],
  ],
  long: [
    [0, 5.5, { pad: 0.6, keys: 0.5 }],
    [5.5, 13, { pad: 0.8, arp: 0.9, kick: 0.7, bass: 0.8 }],
    [13, 27, { pad: 0.7, arp: 1, kick: 1, bass: 1, hats: 1, snare: 1 }],
    [27, 35, { pad: 0.5, arp: 0.5, kick: 0.8, bass: 0.8, hats: 0.6 }],
    [35, 39, { pad: 0.8, arp: 0.6, hats: 0.4 }],
    [39, 54, { pad: 0.6, arp: 0.7, hats: 0.5, bass: 0.5, keys: 0.3 }],
    [54, 62, { pad: 0.9, bass: 0.5, hats: 0.3 }],
    [62, 70, { pad: 0.8, arp: 1, kick: 1, bass: 1, hats: 1, snare: 1 }],
    [70, 76, { pad: 0.9, keys: 0.6 }],
    [76, 80, { pad: 0.7, arp: 0.6, hats: 0.5 }],
    [80, 92, { pad: 0.8, arp: 1, kick: 1, bass: 1, hats: 1, snare: 1 }],
    [92, 114, { pad: 0.6, arp: 0.8, kick: 0.9, bass: 0.9, hats: 0.8, snare: 0.7 }],
    [114, 125, { pad: 1, keys: 0.6 }],
  ],
}[cut];
const level = (t, part) => {
  let v = 0;
  for (const [a, b, lv] of SECTIONS) {
    const x = lv[part] ?? 0;
    if (t >= a && t < b) {
      // short crossfades at section edges
      const fin = clamp((t - a) / 0.15, 0, 1), fout = clamp((b - t) / 0.15, 0, 1);
      v = Math.max(v, x * Math.min(fin, fout));
    }
  }
  return v;
};

// Dm – Bb – F – C, one chord per bar.
const CHORDS = [[50, 53, 57], [46, 50, 53], [41, 45, 48], [48, 52, 55]];
const ROOTS = [38, 34, 41, 36];
const chordAt = (t) => Math.floor(t / BAR) % 4;

// ---------- primitives

function add(buf, i, v) { if (i >= 0 && i < LEN) buf[i] += v; }
function voice(t0, dur, f, { amp = 0.2, type = 'saw', attack = 0.005, release = 0.2, cutoff = 2000, cutEnv = 0, pan = 0, verb = 0.2, dly = 0, detune = 0, sidechain = false }) {
  const n0 = Math.floor(t0 * SR), n = Math.floor((dur + release) * SR);
  let lp = 0, ph = rnd(), ph2 = rnd();
  const gl = Math.cos((pan + 1) * Math.PI / 4), gr = Math.sin((pan + 1) * Math.PI / 4);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const env = t < attack ? t / attack : t < dur ? 1 : Math.max(0, 1 - (t - dur) / release);
    if (env <= 0 && t > dur) break;
    const fc = cutoff * (1 + cutEnv * Math.exp(-t * 12));
    const a = 1 - Math.exp(-2 * Math.PI * Math.min(fc, 18000) / SR);
    ph = (ph + f / SR) % 1; ph2 = (ph2 + f * (1 + detune) / SR) % 1;
    let s;
    if (type === 'saw') s = (2 * ph - 1) + (detune ? 2 * ph2 - 1 : 0);
    else if (type === 'square') s = ph < 0.5 ? 1 : -1;
    else if (type === 'tri') s = 1 - 4 * Math.abs(ph - 0.5);
    else s = Math.sin(2 * Math.PI * ph);
    lp += a * (s - lp);
    const idx = n0 + i;
    const v = lp * env * amp * (sidechain && idx < LEN ? duck[idx] : 1);
    add(L, idx, v * gl); add(R, idx, v * gr);
    if (verb) { add(verbL, idx, v * gl * verb); add(verbR, idx, v * gr * verb); }
    if (dly) { add(dlyL, idx, v * gl * dly); add(dlyR, idx, v * gr * dly); }
  }
}
function noiseHit(t0, dur, { amp = 0.2, hp = 0, lp = 18000, decay = 20, pan = 0, verb = 0.1, sweep = null }) {
  const n0 = Math.floor(t0 * SR), n = Math.floor(dur * SR);
  let l1 = 0, l2 = 0;
  const gl = Math.cos((pan + 1) * Math.PI / 4), gr = Math.sin((pan + 1) * Math.PI / 4);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const env = sweep ? sweep.env(t / dur) : Math.exp(-t * decay);
    const cut = sweep ? sweep.f(t / dur) : lp;
    const a = 1 - Math.exp(-2 * Math.PI * cut / SR);
    const ah = 1 - Math.exp(-2 * Math.PI * Math.max(hp, 1) / SR);
    const x = noise();
    l1 += a * (x - l1);
    l2 += ah * (l1 - l2);
    const v = (hp ? l1 - l2 : l1) * env * amp;
    add(L, n0 + i, v * gl); add(R, n0 + i, v * gr);
    add(verbL, n0 + i, v * gl * verb); add(verbR, n0 + i, v * gr * verb);
  }
}
function kick(t0, amp = 0.9) {
  const n0 = Math.floor(t0 * SR), n = Math.floor(0.45 * SR);
  let ph = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const f = 45 + 110 * Math.exp(-t * 30);
    ph += f / SR;
    const v = Math.sin(2 * Math.PI * ph) * Math.exp(-t * 7) * amp + (t < 0.004 ? noise() * 0.3 * amp : 0);
    add(L, n0 + i, v); add(R, n0 + i, v);
  }
  for (let i = 0; i < 0.35 * SR; i++) { const k = n0 + i; if (k < LEN) duck[k] = Math.min(duck[k], 1 - 0.65 * Math.exp(-(i / SR) / 0.09)); }
}
function boom(t0, amp = 1) {
  kick(t0, amp * 1.1);
  const n0 = Math.floor(t0 * SR), n = Math.floor(2.2 * SR);
  let ph = 0;
  for (let i = 0; i < n; i++) { const t = i / SR; ph += (32 + 30 * Math.exp(-t * 3)) / SR; const v = Math.sin(2 * Math.PI * ph) * Math.exp(-t * 1.6) * 0.55 * amp; add(L, n0 + i, v); add(R, n0 + i, v); }
  noiseHit(t0, 2.4, { amp: 0.32 * amp, lp: 7000, decay: 2.2, verb: 0.6 });
}

// ---------- music

const end = duration + 1;
for (let bar = 0; bar * BAR < end; bar++) {
  const t = bar * BAR, c = CHORDS[bar % 4], root = ROOTS[bar % 4];
  // Pad: detuned saws, slow attack, lots of reverb, ducked by the kick.
  const pl = level(t + 0.01, 'pad');
  if (pl > 0) c.forEach((m, i) => [0, 12].forEach((o) => voice(t, BAR, hz(m + o), { amp: 0.035 * pl, detune: 0.006, attack: 0.5, release: 0.9, cutoff: 1400 + 600 * pl, pan: (i - 1) * 0.5, verb: 0.55, sidechain: true })));
  // Keys: sparse bell tones in quiet sections.
  const kl = level(t + 0.01, 'keys');
  if (kl > 0) [0, 3, 6, 10].forEach((s16, i) => voice(t + s16 * S16 * 2, 0.08, hz(c[i % 3] + 24), { amp: 0.06 * kl, type: 'tri', release: 1.2, cutoff: 5000, verb: 0.7, dly: 0.35, pan: i % 2 ? 0.4 : -0.4 }));
  for (let s = 0; s < 16; s++) {
    const ts = t + s * S16;
    if (ts > end) break;
    // Arp: 16ths over two octaves of the chord.
    const al = level(ts, 'arp');
    if (al > 0) {
      const seq = [0, 1, 2, 3, 2, 1, 2, 3, 4, 3, 2, 1, 2, 3, 5, 3];
      const k = seq[s], m = c[k % 3] + 12 * Math.floor(k / 3) + 12;
      voice(ts, S16 * 0.6, hz(m), { amp: 0.055 * al, type: 'square', release: 0.12, cutoff: 900, cutEnv: 3.5, pan: s % 2 ? 0.35 : -0.35, verb: 0.25, dly: 0.3 });
    }
    // Bass: 8ths on the root with an octave pop on the offbeat.
    const bl = level(ts, 'bass');
    if (bl > 0 && s % 2 === 0) voice(ts, S16 * 1.6, hz(root + (s % 8 === 6 ? 12 : 0)), { amp: 0.16 * bl, type: 'saw', release: 0.06, cutoff: 220, cutEnv: 2.5, verb: 0, sidechain: true });
    // Drums.
    const kk = level(ts, 'kick');
    if (kk > 0 && s % 4 === 0) kick(ts, 0.85 * kk);
    const sn = level(ts, 'snare');
    if (sn > 0 && (s === 4 || s === 12)) { noiseHit(ts, 0.25, { amp: 0.22 * sn, hp: 900, lp: 7000, decay: 16, verb: 0.35 }); voice(ts, 0.05, 190, { amp: 0.12 * sn, type: 'sine', release: 0.08, cutoff: 1000, verb: 0.2 }); }
    const hh = level(ts, 'hats');
    if (hh > 0) noiseHit(ts, 0.06, { amp: (s % 2 ? 0.05 : 0.08) * hh, hp: 7000, lp: 16000, decay: s % 4 === 2 ? 30 : 60, pan: 0.25, verb: 0.05 });
  }
}

// ---------- sound effects on the composition's cues

for (const c of cues) {
  const t = c.t;
  switch (c.type) {
    case 'key': noiseHit(t, 0.025, { amp: 0.11 * (c.v ?? 0.7), hp: 2500, lp: 9000, decay: 260, pan: (rnd() - 0.5) * 0.3, verb: 0.03 }); break;
    case 'enter': noiseHit(t, 0.05, { amp: 0.2, hp: 1200, lp: 6000, decay: 120, verb: 0.08 }); voice(t, 0.02, 140, { amp: 0.15, type: 'sine', release: 0.05, verb: 0 }); break;
    case 'whoosh': noiseHit(t - 0.35, 0.7, { amp: 0.16, verb: 0.3, sweep: { env: (x) => Math.sin(Math.PI * x) ** 2, f: (x) => 400 + 5200 * Math.sin(Math.PI * x) } }); break;
    case 'hit': kick(t, 0.6); noiseHit(t, 1.2, { amp: 0.18, lp: 9000, decay: 4.5, verb: 0.5 }); break;
    case 'boom': boom(t); break;
    case 'riser': noiseHit(t, 3.4, { amp: 0.14, verb: 0.3, sweep: { env: (x) => x * x, f: (x) => 300 + 7000 * x * x } }); for (let i = 0; i < 3; i++) voice(t, 3.3, hz(62 + i * 0.07), { amp: 0.03, detune: 0.01, attack: 3, release: 0.1, cutoff: 3000, verb: 0.4 }); break;
    case 'glitch': for (let i = 0; i < 6; i++) noiseHit(t + i * 0.035, 0.025, { amp: 0.16, hp: 1500, lp: 10000, decay: 80, pan: i % 2 ? 0.6 : -0.6, verb: 0 }); break;
    case 'blip': voice(t, 0.06, 1760, { amp: 0.08, type: 'square', release: 0.08, cutoff: 6000, verb: 0.3, dly: 0.3 }); voice(t + 0.07, 0.06, 2349, { amp: 0.07, type: 'square', release: 0.1, cutoff: 6000, verb: 0.3, dly: 0.3 }); break;
    case 'tick': voice(t, 0.015, 2600, { amp: 0.06, type: 'sine', release: 0.04, verb: 0.15 }); break;
    case 'tick2': voice(t, 0.04, 330, { amp: 0.14, type: 'sine', release: 0.25, cutoff: 2000, verb: 0.3 }); noiseHit(t, 0.08, { amp: 0.06, hp: 3000, decay: 50 }); break;
  }
}

// ---------- effects buses

// Ping-pong delay, dotted eighth.
{
  const d = Math.floor(BEAT * 0.75 * SR), fb = 0.38;
  for (let i = d; i < LEN; i++) {
    dlyL[i] += dlyR[i - d] * fb;
    dlyR[i] += dlyL[i - d] * fb;
  }
  for (let i = d; i < LEN; i++) { L[i] += dlyR[i - d] * 0.5; R[i] += dlyL[i - d] * 0.5; verbL[i] += dlyL[i] * 0.2; verbR[i] += dlyR[i] * 0.2; }
}
// Schroeder reverb.
function reverb(inp, out, offs) {
  const combs = [1557, 1617, 1491, 1422, 1277, 1356].map((x) => Math.floor((x + offs) * SR / 44100));
  const acc = new Float32Array(LEN);
  for (const d of combs) {
    const buf = new Float32Array(d); let k = 0, lp = 0;
    for (let i = 0; i < LEN; i++) {
      const y = buf[k];
      lp = y * 0.6 + lp * 0.4;
      buf[k] = inp[i] + lp * 0.84;
      acc[i] += y;
      k = (k + 1) % d;
    }
  }
  for (const d of [556, 441].map((x) => Math.floor((x + offs) * SR / 44100))) {
    const buf = new Float32Array(d); let k = 0;
    for (let i = 0; i < LEN; i++) { const b = buf[k]; const y = -acc[i] + b; buf[k] = acc[i] + b * 0.5; acc[i] = y; k = (k + 1) % d; }
  }
  for (let i = 0; i < LEN; i++) out[i] += acc[i] * 0.09;
}
reverb(verbL, L, 0);
reverb(verbR, R, 23);

// ---------- master: fade, soft clip, normalise

const fadeIn = 0.02, fadeOut = 1.2;
// Normalise so the loudest 0.1% of samples sit at 0.8, then soft-clip only the rare peaks above that.
const mags = new Float32Array(LEN / 16 | 0);
for (let i = 0; i < mags.length; i++) mags[i] = Math.max(Math.abs(L[i * 16]), Math.abs(R[i * 16]));
mags.sort();
const pre = 0.8 / mags[Math.floor(mags.length * 0.999)];
let peak = 0;
for (let i = 0; i < LEN; i++) {
  const t = i / SR;
  const g = pre * Math.min(1, t / fadeIn) * clamp((duration + 0.3 - t) / fadeOut, 0, 1);
  L[i] = Math.tanh(L[i] * g); R[i] = Math.tanh(R[i] * g);
  peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
}
const norm = 0.89 / peak;
const out = Buffer.alloc(44 + LEN * 4);
out.write('RIFF', 0); out.writeUInt32LE(36 + LEN * 4, 4); out.write('WAVE', 8); out.write('fmt ', 12);
out.writeUInt32LE(16, 16); out.writeUInt16LE(1, 20); out.writeUInt16LE(2, 22); out.writeUInt32LE(SR, 24); out.writeUInt32LE(SR * 4, 28); out.writeUInt16LE(4, 32); out.writeUInt16LE(16, 34);
out.write('data', 36); out.writeUInt32LE(LEN * 4, 40);
for (let i = 0; i < LEN; i++) { out.writeInt16LE(Math.round(clamp(L[i] * norm, -1, 1) * 32767), 44 + i * 4); out.writeInt16LE(Math.round(clamp(R[i] * norm, -1, 1) * 32767), 46 + i * 4); }
writeFileSync(`audio-${cut}.wav`, out);
console.log(`audio-${cut}.wav`, (LEN / SR).toFixed(1), 's, peak', peak.toFixed(2));

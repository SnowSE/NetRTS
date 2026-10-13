// Sound for the 3D view: an 8-bit soundtrack, chip sound effects placed in the scene, and the players'
// voices. The music and effects are synthesized with Web Audio as they play (two pulse channels, a
// triangle and noise, like an old console); only the voices are recorded (see tools/voices).
//
// The soundtrack is composed once, from a fixed seed, as six movements in different keys, tempos and
// grooves, built from phrases with their own melodies and chord progressions. It runs a little over
// ten minutes before it loops.

const STORE = 'netrts.audio';
const DEFAULTS = { music: 0.5, sfx: 0.7, voice: 0.85, muted: false, captions: true };
const MIN_SONG_SECONDS = 600;

function loadSettings() {
  try { return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(STORE) || '{}') }; } catch { return { ...DEFAULTS }; }
}

/** Small seeded random numbers, so the soundtrack is the same every time. */
function rng(seed) {
  let s = seed >>> 0 || 1;
  return () => {
    s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0;
    return s / 4294967296;
  };
}
const pick = (r, list) => list[Math.floor(r() * list.length)];
const midiHz = (m) => 440 * Math.pow(2, (m - 69) / 12);

export class GameAudio {
  constructor() {
    this.settings = loadSettings();
    this.ctx = null;
    this.active = false;
    this.voiceData = null;     // promise of voices.json
    this.voiceFiles = new Map(); // key -> promise of ArrayBuffer
    this.clipCache = new Map();  // "voice|line" -> AudioBuffer (a few dozen)
    this.lastSfx = new Map();    // kind -> ctx time, to thin out bursts
    this.playing = 0;            // sound effects sounding now
    this.onChange = null;
    this.onVisibility = () => this.sync();
    document.addEventListener('visibilitychange', this.onVisibility);
  }

  /** Builds the audio graph the first time sound is wanted. */
  ensure() {
    if (this.ctx) return this.ctx;
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return null;
    const ctx = new AC();
    this.ctx = ctx;
    this.master = ctx.createGain();
    const limiter = ctx.createDynamicsCompressor();
    limiter.threshold.value = -10;
    limiter.ratio.value = 6;
    this.master.connect(limiter).connect(ctx.destination);
    this.bus = {};
    for (const k of ['music', 'sfx', 'voice']) {
      this.bus[k] = ctx.createGain();
      this.bus[k].connect(this.master);
    }
    this.music = new Chiptune(ctx, this.bus.music);
    this.synth = new ChipSfx(ctx, this.bus.sfx);
    this.apply();
    return ctx;
  }

  /** The 3D view is showing (or not): sound plays only while it is, and while the tab is visible. */
  setActive(on) {
    this.active = on;
    if (on) this.ensure();
    this.sync();
  }

  sync() {
    const ctx = this.ctx;
    if (!ctx) return;
    const on = this.active && !document.hidden;
    if (on) {
      ctx.resume().catch(() => {});
      this.music.start();
    } else {
      this.music.stop();
      ctx.suspend().catch(() => {});
    }
  }

  /** Browsers start audio only after the viewer has clicked or pressed a key; call this when they do. */
  unlock() {
    if (this.active && this.ctx && this.ctx.state !== 'running' && !document.hidden) this.ctx.resume().catch(() => {});
  }

  /** Change a setting (music, sfx, voice: 0-1; muted, captions: true/false) and remember it. */
  set(key, value) {
    this.settings[key] = value;
    try { localStorage.setItem(STORE, JSON.stringify(this.settings)); } catch { /* storage blocked */ }
    this.apply();
    this.onChange?.(this.settings);
  }

  apply() {
    if (!this.ctx) return;
    const s = this.settings, t = this.ctx.currentTime;
    // Sliders feel even when the level follows a curve rather than a straight line.
    const level = (v) => Math.pow(Math.max(0, Math.min(1, v)), 1.6);
    this.master.gain.setTargetAtTime(s.muted ? 0 : 1, t, 0.03);
    this.bus.music.gain.setTargetAtTime(level(s.music) * 0.55, t, 0.05);
    this.bus.sfx.gain.setTargetAtTime(level(s.sfx) * 0.9, t, 0.05);
    this.bus.voice.gain.setTargetAtTime(level(s.voice) * 1.1, t, 0.05);
  }

  get live() { return !!(this.active && this.ctx && this.ctx.state === 'running' && !this.settings.muted); }

  /** A sound effect at volume gain (0-1, already scaled for distance) and pan (-1 left, 1 right). */
  sfx(kind, gain = 1, pan = 0) {
    if (!this.live || !(this.settings.sfx > 0) || gain < 0.02) return;
    const now = this.ctx.currentTime;
    const gap = SFX_GAP[kind] ?? 0.04;
    if (now - (this.lastSfx.get(kind) ?? -1) < gap || this.playing > 28) return;
    this.lastSfx.set(kind, now);
    this.playing++;
    const dur = this.synth.play(kind, Math.min(1, gain), Math.max(-1, Math.min(1, pan)));
    setTimeout(() => { this.playing--; }, (dur + 0.05) * 1000);
  }

  // ---------------------------------------------------------------- voices

  /** The recorded lines and voices (loaded once). */
  voices() {
    this.voiceData ??= fetch('/audio/voices.json').then((r) => (r.ok ? r.json() : null)).catch(() => null);
    return this.voiceData;
  }

  /** Fetch a voice's recordings ahead of time, so its first line isn't late. */
  async preload(voiceIndex) {
    const data = await this.voices();
    const v = data?.voices[voiceIndex % data.voices.length];
    if (!v) return;
    if (!this.voiceFiles.has(v.key)) {
      this.voiceFiles.set(v.key, fetch(v.file).then((r) => (r.ok ? r.arrayBuffer() : null)).catch(() => null));
    }
  }

  /** Speak a line; resolves to its length in seconds once it has started, or 0 if it couldn't play. */
  async speak(voiceIndex, lineIndex) {
    if (!this.live || !(this.settings.voice > 0)) return 0;
    const data = await this.voices();
    const v = data?.voices[voiceIndex % data.voices.length];
    if (!v) return 0;
    await this.preload(voiceIndex);
    const key = `${v.key}|${lineIndex}`;
    let buf = this.clipCache.get(key);
    if (!buf) {
      const file = await this.voiceFiles.get(v.key);
      const [off, len] = v.clips[lineIndex] ?? [];
      if (!file || len == null) return 0;
      try {
        buf = await this.ctx.decodeAudioData(file.slice(off, off + len));
      } catch { return 0; }
      this.clipCache.set(key, buf);
      if (this.clipCache.size > 60) this.clipCache.delete(this.clipCache.keys().next().value);
    }
    if (!this.live) return 0;
    const t = this.ctx.currentTime + 0.02;
    this.synth.chirp(this.bus.voice, t);   // a radio blip before they talk
    const src = this.ctx.createBufferSource();
    src.buffer = buf;
    src.connect(this.bus.voice);
    src.start(t + 0.09);
    return buf.duration + 0.09;
  }

  dispose() {
    document.removeEventListener('visibilitychange', this.onVisibility);
    this.music?.stop();
    this.ctx?.close().catch(() => {});
    this.ctx = null;
  }
}

// ---------------------------------------------------------------- the soundtrack

const SCALES = {
  minor: [0, 2, 3, 5, 7, 8, 10],
  dorian: [0, 2, 3, 5, 7, 9, 10],
  phrygian: [0, 1, 3, 5, 7, 8, 10],
  major: [0, 2, 4, 5, 7, 9, 11],
  mixolydian: [0, 2, 4, 5, 7, 9, 10],
};
const PROGRESSIONS = {
  minor: [[0, 5, 2, 6], [0, 3, 4, 0], [0, 6, 5, 4], [0, 2, 3, 4], [5, 6, 0, 0], [0, 3, 6, 2], [3, 4, 0, 0], [0, 5, 3, 4]],
  major: [[0, 4, 5, 3], [0, 5, 3, 4], [0, 3, 4, 4], [5, 3, 0, 4], [0, 2, 3, 4], [3, 4, 2, 5], [0, 3, 0, 4], [5, 4, 3, 4]],
};
// The six movements: a march, a rush, a calm valley morning, a siege, a proud anthem and the final push.
const MOVEMENTS = [
  { name: 'Marching Orders', root: 57, scale: 'minor', bpm: 132, drums: 'march', bass: 'root8', harm: 'arp16', duty: 0.25 },
  { name: 'Grub Rush', root: 62, scale: 'dorian', bpm: 150, drums: 'drive', bass: 'gallop', harm: 'stab', duty: 0.125 },
  { name: 'Valley Morning', root: 53, scale: 'major', bpm: 112, drums: 'light', bass: 'walk', harm: 'arp8', duty: 0.25 },
  { name: 'Bell Tower Siege', root: 52, scale: 'phrygian', bpm: 144, drums: 'four', bass: 'octave', harm: 'arp16', duty: 0.5 },
  { name: 'Badger Pride', root: 58, scale: 'mixolydian', bpm: 138, drums: 'rock', bass: 'walk', harm: 'counter', duty: 0.25 },
  { name: 'Final Push', root: 60, scale: 'minor', bpm: 160, drums: 'drive', bass: 'gallop', harm: 'arp16', duty: 0.125 },
];
// Each movement's shape: [part, bars]. Parts share material within a movement (A, A2 and A3 vary A).
const FORM = [['intro', 4], ['A', 8], ['A2', 8], ['B', 8], ['A', 8], ['C', 8], ['break', 4], ['A3', 8], ['outro', 4]];

// Lead rhythms over two bars of sixteenths: [step, length].
const RHYTHMS = [
  [[0, 3], [4, 2], [6, 2], [8, 4], [12, 4], [16, 3], [20, 2], [22, 2], [24, 6]],
  [[0, 2], [2, 2], [4, 4], [8, 2], [10, 2], [12, 4], [16, 2], [18, 2], [20, 4], [24, 8]],
  [[0, 6], [6, 2], [8, 4], [12, 2], [14, 2], [16, 6], [22, 2], [24, 4], [28, 4]],
  [[0, 1], [2, 1], [4, 2], [6, 2], [8, 1], [10, 1], [12, 4], [16, 1], [18, 1], [20, 2], [22, 2], [24, 8]],
  [[0, 4], [4, 4], [8, 2], [10, 2], [12, 2], [14, 2], [16, 8], [24, 2], [26, 2], [28, 4]],
  [[2, 2], [4, 2], [6, 4], [10, 2], [12, 4], [18, 2], [20, 2], [22, 4], [26, 6]],
];

class Chiptune {
  constructor(ctx, out) {
    this.ctx = ctx;
    this.out = out;
    this.timer = 0;
    this.nextTime = 0;
    this.cursor = { block: 0, bar: 0, step: 0 };
    this.waves = {};
    for (const d of [0.125, 0.25, 0.5]) this.waves[d] = pulseWave(ctx, d);
    this.noise = noiseBuffer(ctx);
    // The lead runs through a little echo.
    this.leadBus = ctx.createGain();
    this.leadBus.connect(out);
    const echo = ctx.createDelay(1);
    const fb = ctx.createGain(), wet = ctx.createGain(), tone = ctx.createBiquadFilter();
    fb.gain.value = 0.3; wet.gain.value = 0.22; tone.type = 'lowpass'; tone.frequency.value = 2600;
    this.leadBus.connect(echo); echo.connect(tone).connect(fb).connect(echo); tone.connect(wet).connect(out);
    this.echo = echo;
    this.blocks = compose();
    this.length = this.blocks.reduce((s, b) => s + b.bars * 4 * 60 / b.mv.bpm, 0);
  }

  start() {
    if (this.timer) return;
    this.nextTime = this.ctx.currentTime + 0.08;
    this.timer = setInterval(() => this.schedule(), 25);
  }

  stop() {
    clearInterval(this.timer);
    this.timer = 0;
  }

  schedule() {
    const ctx = this.ctx;
    if (ctx.state !== 'running') return;
    // After a stall (a busy tab), skip ahead rather than playing a burst of late notes.
    if (this.nextTime < ctx.currentTime - 0.1) this.nextTime = ctx.currentTime + 0.05;
    while (this.nextTime < ctx.currentTime + 0.15) {
      const c = this.cursor, block = this.blocks[c.block];
      const stepDur = 60 / block.mv.bpm / 4;
      if (c.bar === 0 && c.step === 0) this.echo.delayTime.setValueAtTime(stepDur * 3, this.nextTime);
      for (const n of block.notes[c.bar][c.step] || []) this.play(n, this.nextTime, stepDur);
      this.nextTime += stepDur;
      if (++c.step === 16) {
        c.step = 0;
        if (++c.bar === block.bars) { c.bar = 0; c.block = (c.block + 1) % this.blocks.length; }
      }
    }
  }

  play(n, t, stepDur) {
    const ctx = this.ctx, dur = n.len * stepDur;
    if (n.ch === 'kick' || n.ch === 'snare' || n.ch === 'hat' || n.ch === 'open') { drum(ctx, this.out, this.noise, n.ch, t, n.vol); return; }
    const osc = ctx.createOscillator(), g = ctx.createGain();
    if (n.ch === 'bass') osc.type = 'triangle';
    else osc.setPeriodicWave(this.waves[n.duty ?? 0.5]);
    osc.frequency.setValueAtTime(midiHz(n.midi), t);
    const peak = n.vol, hold = Math.max(0.03, dur - 0.02);
    g.gain.setValueAtTime(0, t);
    g.gain.linearRampToValueAtTime(peak, t + 0.004);
    if (n.ch !== 'bass') g.gain.setTargetAtTime(peak * 0.62, t + 0.01, 0.06);
    g.gain.setValueAtTime(g.gain.value, t + hold);
    g.gain.setTargetAtTime(0, t + hold, 0.012);
    // Vibrato on the lead's long notes.
    if (n.ch === 'lead' && dur > 0.3) {
      const lfo = ctx.createOscillator(), depth = ctx.createGain();
      lfo.frequency.value = 5.5;
      depth.gain.setValueAtTime(0, t);
      depth.gain.linearRampToValueAtTime(14, t + Math.min(0.35, dur * 0.6));
      lfo.connect(depth).connect(osc.detune);
      lfo.start(t); lfo.stop(t + dur + 0.1);
    }
    osc.connect(g).connect(n.ch === 'lead' ? this.leadBus : this.out);
    osc.start(t);
    osc.stop(t + dur + 0.08);
  }
}

/** Write the whole soundtrack as blocks of bars, each bar a list of notes per sixteenth step. */
function compose() {
  const blocks = [];
  MOVEMENTS.forEach((mv, mi) => {
    const r = rng(9001 + mi * 7919);
    const family = mv.scale === 'major' || mv.scale === 'mixolydian' ? 'major' : 'minor';
    const progs = { A: pick(r, PROGRESSIONS[family]), B: pick(r, PROGRESSIONS[family]), C: pick(r, PROGRESSIONS[family]) };
    while (progs.B.join() === progs.A.join()) progs.B = pick(r, PROGRESSIONS[family]);
    const motifs = { A: motif(r), B: motif(r), C: motif(r) };
    for (const [part, bars] of FORM) blocks.push(writeBlock(mv, part, bars, progs, motifs, rng(mi * 131 + part.length * 17 + bars)));
  });
  // Never shorter than ten minutes: repeat the last movement's A section until it is.
  let secs = blocks.reduce((s, b) => s + b.bars * 4 * 60 / b.mv.bpm, 0);
  const tail = blocks.filter((b) => b.mv === MOVEMENTS[MOVEMENTS.length - 1] && b.part === 'A')[0];
  while (secs < MIN_SONG_SECONDS) {
    blocks.splice(blocks.length - 1, 0, tail);
    secs += tail.bars * 4 * 60 / tail.mv.bpm;
  }
  return blocks;
}

/** A two-bar melody: rhythm plus scale degrees relative to the chord's root (0, 2, 4 are chord tones). */
function motif(r) {
  const rhythm = pick(r, RHYTHMS);
  let deg = pick(r, [0, 2, 4, 7]);
  return rhythm.map(([step, len], i) => {
    if (i > 0) {
      const strong = step % 4 === 0;
      deg += pick(r, [-2, -1, -1, 1, 1, 2, 3, -3]);
      if (strong) deg = [0, 2, 4, 7, 9].reduce((a, b) => (Math.abs(b - deg) < Math.abs(a - deg) ? b : a));
      deg = Math.max(-3, Math.min(10, deg));
    }
    return { step, len, deg };
  });
}

function writeBlock(mv, part, bars, progs, motifs, r) {
  const scale = SCALES[mv.scale];
  const base = part === 'B' ? 'B' : part === 'C' ? 'C' : 'A';
  const prog = progs[base];
  // Scale degree -> semitones above the movement's root; minor keys raise the 7th over the V chord.
  const semis = (deg, chord) => {
    const o = Math.floor(deg / 7), d = ((deg % 7) + 7) % 7;
    let s = scale[d] + 12 * o;
    if ((mv.scale === 'minor') && ((chord % 7) + 7) % 7 === 4 && d === 6) s += 1;
    return s;
  };
  const chordTones = (c) => [0, 2, 4].map((k) => semis(c + k, c));
  const notes = [];
  const lead = part === 'intro' || part === 'break' ? null : motifs[base];
  for (let bar = 0; bar < bars; bar++) {
    const steps = Array.from({ length: 16 }, () => []);
    const chord = prog[bar % prog.length];
    const tones = chordTones(chord);
    const last = bar === bars - 1;
    const add = (step, n) => steps[step].push(n);

    // Drums.
    drumBar(mv.drums, part, bar, last, add, r);

    // Bass.
    const root = mv.root - 12 + tones[0];
    if (part !== 'break' || bar >= 2) bassBar(mv.bass, root, tones.map((t) => mv.root - 12 + t), add);

    // Harmony: arpeggios, stabs or a counter line.
    if (part !== 'break') {
      const h = part === 'intro' && mv.harm === 'counter' ? 'arp8' : mv.harm;
      const chordMidi = tones.map((t) => mv.root + t);
      if (h === 'arp16' || h === 'arp8') {
        const every = h === 'arp16' ? 1 : 2, seq = [0, 1, 2, 1, 0, 1, 2, 3];
        for (let s = 0; s < 16; s += every) {
          const k = seq[(s / every) % seq.length];
          add(s, { ch: 'harm', midi: (k === 3 ? chordMidi[0] + 12 : chordMidi[k]) + 12, len: every, vol: 0.055, duty: 0.25 });
        }
      } else if (h === 'stab') {
        for (const s of [2, 6, 10, 14]) chordMidi.forEach((m, i) => i < 2 && add(s, { ch: 'harm', midi: m + 12, len: 1, vol: 0.05, duty: 0.5 }));
      } else if (h === 'counter') {
        for (const s of [0, 8]) add(s, { ch: 'harm', midi: chordMidi[s ? 2 : 1] + 12, len: 7, vol: 0.05, duty: 0.5 });
      }
    }

    // Lead: the motif follows the chords, with a new ending on repeats.
    if (lead) {
      const half = bar % 2;
      for (const n of lead) {
        if ((n.step >= 16) !== (half === 1)) continue;
        let deg = n.deg;
        if (part !== 'A' && half === 1 && n.step >= 24) deg += pick(r, [-2, 2, 0, 4]);
        if (part === 'A3' && r() < 0.15) deg += 7;     // the last time round, some notes leap up an octave
        const midi = mv.root + 12 + semis(chord + deg, chord);
        add(n.step % 16, { ch: 'lead', midi, len: n.len, vol: part === 'B' ? 0.1 : 0.12, duty: part === 'C' ? 0.5 : mv.duty });
      }
    }
    notes.push(steps);
  }
  return { mv, part, bars, notes };
}

function drumBar(style, part, bar, last, add, r) {
  const k = (s, v = 0.9) => add(s, { ch: 'kick', vol: v });
  const sn = (s, v = 0.5) => add(s, { ch: 'snare', vol: v });
  const h = (s, v = 0.25) => add(s, { ch: 'hat', vol: v });
  if (part === 'break' && bar < 2) { k(0); if (bar === 1) for (let s = 8; s < 16; s += 2) sn(s, 0.2 + s * 0.02); return; }
  if (part === 'intro' && bar < 2) { h(0); h(4); h(8); h(12); return; }
  switch (style) {
    case 'march':
      k(0); k(8); sn(4); sn(12); sn(14, 0.25); sn(15, 0.3);
      for (let s = 0; s < 16; s += 2) h(s, 0.18);
      break;
    case 'drive':
      k(0); k(6); k(8); k(11); sn(4); sn(12);
      for (let s = 0; s < 16; s++) h(s, s % 2 ? 0.12 : 0.22);
      break;
    case 'light':
      k(0); k(10); sn(8, 0.35);
      for (const s of [2, 6, 10, 14]) h(s, 0.2);
      break;
    case 'four':
      for (const s of [0, 4, 8, 12]) k(s);
      sn(4, 0.4); sn(12, 0.4);
      for (const s of [2, 6, 10, 14]) add(s, { ch: 'open', vol: 0.18 });
      break;
    default: // rock
      k(0); k(8); k(10); sn(4); sn(12);
      for (let s = 0; s < 16; s += 2) h(s);
  }
  // A fill into the next phrase.
  if (last && part !== 'outro') for (let s = 12; s < 16; s++) sn(s, 0.3 + (s - 12) * 0.08);
  if (last && part === 'outro') add(0, { ch: 'open', vol: 0.3 });
  if (part === 'A3' && bar % 4 === 3 && r() < 0.5) sn(15, 0.4);
}

function bassBar(style, root, chord, add) {
  const n = (s, midi, len, vol = 0.32) => add(s, { ch: 'bass', midi, len, vol });
  switch (style) {
    case 'gallop':
      for (let b = 0; b < 4; b++) { n(b * 4, root, 2); n(b * 4 + 2, root, 1); n(b * 4 + 3, b === 3 ? root + 12 : root, 1); }
      break;
    case 'walk':
      n(0, root, 4); n(4, chord[1], 4); n(8, chord[2], 4); n(12, chord[2] + 2 <= root + 12 ? chord[2] + 2 : root + 12, 4);
      break;
    case 'octave':
      for (let s = 0; s < 16; s += 2) n(s, s % 4 ? root + 12 : root, 2);
      break;
    default: // root8
      for (let s = 0; s < 16; s += 2) n(s, s === 14 ? root + 12 : root, 2);
  }
}

function drum(ctx, out, noise, kind, t, vol) {
  const g = ctx.createGain();
  g.connect(out);
  if (kind === 'kick') {
    const o = ctx.createOscillator();
    o.type = 'triangle';
    o.frequency.setValueAtTime(160, t);
    o.frequency.exponentialRampToValueAtTime(42, t + 0.09);
    g.gain.setValueAtTime(vol * 0.75, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.16);
    o.connect(g); o.start(t); o.stop(t + 0.18);
    return;
  }
  const src = ctx.createBufferSource(), f = ctx.createBiquadFilter();
  src.buffer = noise;
  const len = kind === 'snare' ? 0.13 : kind === 'open' ? 0.12 : 0.035;
  f.type = kind === 'snare' ? 'bandpass' : 'highpass';
  f.frequency.value = kind === 'snare' ? 1800 : 7000;
  g.gain.setValueAtTime(vol * (kind === 'snare' ? 0.5 : 0.22), t);
  g.gain.exponentialRampToValueAtTime(0.001, t + len);
  src.connect(f).connect(g);
  src.start(t, Math.random() * 0.5);
  src.stop(t + len + 0.02);
  if (kind === 'snare') {
    const o = ctx.createOscillator(), og = ctx.createGain();
    o.type = 'square';
    o.frequency.setValueAtTime(220, t);
    o.frequency.exponentialRampToValueAtTime(120, t + 0.05);
    og.gain.setValueAtTime(vol * 0.08, t);
    og.gain.exponentialRampToValueAtTime(0.001, t + 0.06);
    o.connect(og).connect(out); o.start(t); o.stop(t + 0.07);
  }
}

/** A pulse wave with the given duty cycle, from its Fourier series. */
function pulseWave(ctx, duty) {
  const n = 64, real = new Float32Array(n), imag = new Float32Array(n);
  for (let k = 1; k < n; k++) {
    real[k] = (2 / (k * Math.PI)) * Math.sin(k * Math.PI * duty) * Math.cos(k * Math.PI * duty);
    imag[k] = (2 / (k * Math.PI)) * Math.sin(k * Math.PI * duty) * Math.sin(k * Math.PI * duty);
  }
  return ctx.createPeriodicWave(real, imag);
}

function noiseBuffer(ctx) {
  const len = ctx.sampleRate;
  const buf = ctx.createBuffer(1, len, ctx.sampleRate);
  const d = buf.getChannelData(0);
  // Sample-and-hold noise: a little crunchier than plain white noise, like the old chips.
  let v = 0;
  for (let i = 0; i < len; i++) { if (i % 2 === 0) v = Math.random() * 2 - 1; d[i] = v; }
  return buf;
}

// ---------------------------------------------------------------- sound effects

/** The shortest gap between two of the same effect, in seconds: a big fight shouldn't be a roar. */
const SFX_GAP = { sword: 0.05, arrow: 0.045, arrowHit: 0.05, mine: 0.07, hammer: 0.08, chip: 0.06, death: 0.06, spawn: 0.08, tower: 0.08, blast: 0.07, explosion: 0.15, explosionBig: 0.2, complete: 0.3 };

class ChipSfx {
  constructor(ctx, out) {
    this.ctx = ctx;
    this.out = out;
    this.square = pulseWave(ctx, 0.5);
    this.thin = pulseWave(ctx, 0.125);
    this.noise = noiseBuffer(ctx);
  }

  /** Plays an effect; returns how long it lasts. */
  play(kind, gain, pan) {
    const ctx = this.ctx, t = ctx.currentTime + 0.005;
    const out = ctx.createGain(), p = ctx.createStereoPanner();
    out.gain.value = gain;
    p.pan.value = pan;
    out.connect(p).connect(this.out);
    const v = (x) => x * (0.9 + Math.random() * 0.2);   // no two hits quite alike
    switch (kind) {
      case 'sword':
        this.tone(out, t, this.thin, v(1900), v(1400), 0.06, 0.18);
        this.tone(out, t, this.square, v(2650), v(2200), 0.05, 0.08);
        this.hiss(out, t, 0.04, 5000, 0.25, 'highpass');
        return 0.08;
      case 'chip':
        this.tone(out, t, this.square, v(320), 140, 0.07, 0.2);
        this.hiss(out, t, 0.06, 1400, 0.35, 'bandpass');
        return 0.08;
      case 'arrow':
        this.hiss(out, t, 0.12, 3200, 0.22, 'bandpass', 900);
        this.tone(out, t, this.thin, v(1200), 1500, 0.03, 0.06);
        return 0.13;
      case 'arrowHit':
        this.tone(out, t, this.square, v(240), 90, 0.05, 0.16);
        this.hiss(out, t, 0.03, 2000, 0.15, 'bandpass');
        return 0.06;
      case 'tower':
        this.tone(out, t, this.square, v(880), 160, 0.2, 0.17);
        this.tone(out, t, this.thin, v(1320), 240, 0.16, 0.08);
        this.hiss(out, t, 0.18, 2400, 0.2, 'bandpass', 600);
        return 0.22;
      case 'blast':
        this.hiss(out, t, 0.32, 1600, 0.6, 'lowpass', 180);
        this.tone(out, t, this.square, 140, 40, 0.18, 0.25);
        return 0.34;
      case 'explosion':
      case 'explosionBig': {
        const big = kind === 'explosionBig', len = big ? 1.4 : 0.8;
        this.hiss(out, t, len, 2400, big ? 1 : 0.8, 'lowpass', 90);
        this.tone(out, t, this.square, 110, 30, len * 0.5, 0.35);
        this.tone(out, t + 0.06, this.square, 80, 25, len * 0.6, 0.25);
        return len;
      }
      case 'death':
        [740, 560, 380].forEach((f, i) => this.tone(out, t + i * 0.05, this.square, v(f), f * 0.8, 0.05, 0.12));
        return 0.18;
      case 'spawn':
        [523, 659, 784].forEach((f, i) => this.tone(out, t + i * 0.045, this.thin, f, f, 0.045, 0.1));
        return 0.16;
      case 'complete':
        [523, 659, 784, 1047].forEach((f, i) => this.tone(out, t + i * 0.07, this.square, f, f, i === 3 ? 0.22 : 0.07, 0.12));
        return 0.45;
      case 'mine':
        this.tone(out, t, this.thin, v(2100), 1700, 0.025, 0.08);
        this.hiss(out, t, 0.03, 3500, 0.12, 'bandpass');
        return 0.04;
      case 'hammer':
        this.tone(out, t, this.square, v(420), 300, 0.035, 0.12);
        this.hiss(out, t, 0.02, 2500, 0.15, 'bandpass');
        return 0.05;
      default:
        return 0;
    }
  }

  /** The two-note blip of a radio before a player speaks. */
  chirp(bus, t) {
    const g = this.ctx.createGain();
    g.connect(bus);
    this.tone(g, t, this.thin, 1568, 1568, 0.035, 0.05);
    this.tone(g, t + 0.04, this.thin, 2093, 2093, 0.035, 0.05);
  }

  tone(out, t, wave, f0, f1, len, vol) {
    const o = this.ctx.createOscillator(), g = this.ctx.createGain();
    o.setPeriodicWave(wave);
    o.frequency.setValueAtTime(f0, t);
    if (f1 !== f0) o.frequency.exponentialRampToValueAtTime(Math.max(20, f1), t + len);
    g.gain.setValueAtTime(vol, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + len);
    o.connect(g).connect(out);
    o.start(t);
    o.stop(t + len + 0.02);
  }

  hiss(out, t, len, freq, vol, type, freqEnd = freq) {
    const src = this.ctx.createBufferSource(), f = this.ctx.createBiquadFilter(), g = this.ctx.createGain();
    src.buffer = this.noise;
    f.type = type;
    f.frequency.setValueAtTime(freq, t);
    if (freqEnd !== freq) f.frequency.exponentialRampToValueAtTime(freqEnd, t + len);
    g.gain.setValueAtTime(vol, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + len);
    src.connect(f).connect(g).connect(out);
    src.start(t, Math.random() * 0.5);
    src.stop(t + len + 0.02);
  }
}

/** For tests and the console: the soundtrack's length in seconds and its movements. */
export function soundtrackInfo() {
  const blocks = compose();
  return {
    seconds: blocks.reduce((s, b) => s + b.bars * 4 * 60 / b.mv.bpm, 0),
    movements: MOVEMENTS.map((m) => m.name),
    bars: blocks.reduce((s, b) => s + b.bars, 0),
  };
}

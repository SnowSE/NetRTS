// Sound for the 3D view: a synthwave soundtrack in the style of the commercial, chip sound effects
// placed in the scene, and the players' voices. The music and effects are synthesized with Web Audio
// as they play; only the voices are recorded (see tools/voices). The soundtrack runs a little over
// eleven minutes before it loops.

const STORE = 'netrts.audio';
const DEFAULTS = { music: 0.5, sfx: 0.7, voice: 0.85, muted: false, captions: true };
const MIN_SONG_SECONDS = 600;

function loadSettings() {
  try { return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(STORE) || '{}') }; } catch { return { ...DEFAULTS }; }
}

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
    this.music = new Soundtrack(ctx, this.bus.music);
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

// The commercial's sound (promo/music.mjs), played live: detuned saw pads that pump against the kick,
// a filtered square arpeggio through a ping-pong delay, a filtered saw bass, kick, snare and hats,
// and bell keys in the quiet stretches, all in a big reverb. Six movements, each its own key, tempo,
// chords and arpeggio, follow the commercial's shape: quiet open, build, full, breakdown, full, out.

const MINOR = [0, 2, 3, 5, 7, 8, 10];
// key: the tonic as a MIDI note; prog: scale degrees, one chord per bar (4 = V, major); arp: the
// pattern over the chord's notes (0-2 one octave, 3-5 the next).
const MOVEMENTS = [
  { name: 'Opening Moves', key: 50, bpm: 120, prog: [0, 5, 2, 6], arp: [0, 1, 2, 3, 2, 1, 2, 3, 4, 3, 2, 1, 2, 3, 5, 3] },
  { name: 'Grub Run', key: 57, bpm: 124, prog: [0, 6, 5, 6], arp: [0, 2, 1, 3, 2, 4, 3, 5, 4, 3, 2, 1, 3, 2, 1, 0] },
  { name: 'Valley at Dusk', key: 52, bpm: 116, prog: [5, 6, 0, 0], arp: [0, 1, 2, 1, 3, 2, 1, 2, 0, 1, 2, 1, 4, 3, 2, 1] },
  { name: 'Siege', key: 48, bpm: 126, prog: [0, 3, 5, 4], arp: [0, 3, 1, 4, 2, 5, 1, 4, 0, 3, 1, 4, 2, 5, 3, 4] },
  { name: 'Bell Tower', key: 55, bpm: 118, prog: [0, 5, 3, 4], arp: [0, 1, 2, 3, 4, 5, 4, 3, 2, 3, 4, 5, 4, 3, 2, 1] },
  { name: 'Last Stand', key: 54, bpm: 128, prog: [0, 5, 2, 6], arp: [0, 2, 3, 2, 1, 3, 4, 3, 2, 4, 5, 4, 3, 2, 1, 2] },
];
// Each movement's shape, as in the commercial: [bars, levels per part].
const SHAPE = [
  [4, { pad: 0.7, keys: 0.5 }],
  [8, { pad: 0.8, arp: 0.9, kick: 0.7, bass: 0.8 }],
  [16, { pad: 0.8, arp: 1, kick: 1, bass: 1, hats: 1, snare: 1 }],
  [8, { pad: 0.5, arp: 0.5, kick: 0.8, bass: 0.8, hats: 0.6 }],
  [4, { pad: 0.9, keys: 0.6 }],
  [16, { pad: 0.7, arp: 1, kick: 1, bass: 1, hats: 1, snare: 1, keys: 0.25 }],
  [4, { pad: 0.8, arp: 0.6, hats: 0.4 }],
];

class Soundtrack {
  constructor(ctx, out) {
    this.ctx = ctx;
    this.timer = 0;
    this.nextTime = 0;
    this.cursor = { block: 0, bar: 0, step: 0 };
    this.noise = noiseBuffer(ctx);
    this.square = pulseWave(ctx, 0.5);

    // Dry mix, a ducked bus for pad and bass, a reverb and a ping-pong delay.
    this.dry = ctx.createGain();
    this.dry.connect(out);
    this.duck = ctx.createGain();
    this.duck.connect(this.dry);
    this.verb = ctx.createConvolver();
    this.verb.buffer = impulse(ctx, 2.2);
    const verbOut = ctx.createGain();
    verbOut.gain.value = 0.55;
    this.verb.connect(verbOut).connect(out);
    this.verbIn = ctx.createGain();
    this.verbIn.connect(this.verb);
    this.dlyIn = ctx.createGain();
    const dl = ctx.createDelay(2), dr = ctx.createDelay(2), fl = ctx.createGain(), fr = ctx.createGain();
    const merge = ctx.createChannelMerger(2), dlyOut = ctx.createGain();
    fl.gain.value = fr.gain.value = 0.38;
    dlyOut.gain.value = 0.5;
    this.dlyIn.connect(dl);
    dl.connect(fr).connect(dr).connect(fl).connect(dl);
    dl.connect(merge, 0, 1); dr.connect(merge, 0, 0);
    merge.connect(dlyOut).connect(out);
    dlyOut.connect(this.verbIn);
    this.delays = [dl, dr];

    this.blocks = arrange();
    this.length = this.blocks.reduce((s, b) => s + b.bars * 240 / b.mv.bpm, 0);
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
    if (this.nextTime < ctx.currentTime - 0.1) this.nextTime = ctx.currentTime + 0.05;   // after a stall, skip ahead
    while (this.nextTime < ctx.currentTime + 0.2) {
      const c = this.cursor, b = this.blocks[c.block];
      const beat = 60 / b.mv.bpm, s16 = beat / 4;
      if (c.bar === 0 && c.step === 0) for (const d of this.delays) d.delayTime.setValueAtTime(beat * 0.75, this.nextTime);
      this.step(b, c.bar, c.step, this.nextTime, s16);
      this.nextTime += s16;
      if (++c.step === 16) {
        c.step = 0;
        if (++c.bar === b.bars) { c.bar = 0; c.block = (c.block + 1) % this.blocks.length; }
      }
    }
  }

  /** Everything that starts on one sixteenth. */
  step(b, bar, s, t, s16) {
    const lv = b.levels, mv = b.mv;
    const { chord, root } = chordOf(mv, mv.prog[bar % mv.prog.length]);
    const barLen = s16 * 16;
    if (s === 0) {
      // Pad: detuned saws an octave apart, slow attack, lots of reverb, ducked by the kick.
      if (lv.pad) chord.forEach((m, i) => [0, 12].forEach((o) => this.voice(t, barLen, m + o, {
        amp: 0.035 * lv.pad, type: 'sawtooth', detune: 10, attack: 0.5, release: 0.9, cutoff: 1400 + 600 * lv.pad, pan: (i - 1) * 0.5, verb: 0.55, ducked: true,
      })));
      // Keys: sparse bell tones.
      if (lv.keys) [0, 3, 6, 10].forEach((x, i) => this.voice(t + x * s16 * 2, 0.08, chord[i % 3] + 24, {
        amp: 0.06 * lv.keys, type: 'triangle', release: 1.2, cutoff: 5000, verb: 0.7, dly: 0.35, pan: i % 2 ? 0.4 : -0.4,
      }));
    }
    // Arp: sixteenths over two octaves of the chord.
    if (lv.arp) {
      const k = mv.arp[s];
      this.voice(t, s16 * 0.6, chord[k % 3] + 12 * Math.floor(k / 3) + 12, {
        amp: 0.055 * lv.arp, type: 'square', release: 0.12, cutoff: 900, cutEnv: 3.5, pan: s % 2 ? 0.35 : -0.35, verb: 0.25, dly: 0.3,
      });
    }
    // Bass: eighths on the root with an octave pop.
    if (lv.bass && s % 2 === 0) this.voice(t, s16 * 1.6, root + (s % 8 === 6 ? 12 : 0), {
      amp: 0.16 * lv.bass, type: 'sawtooth', release: 0.06, cutoff: 220, cutEnv: 2.5, ducked: true,
    });
    // Drums, with a snare fill into the next section.
    if (lv.kick && s % 4 === 0) this.kick(t, 0.85 * lv.kick);
    const last = bar === b.bars - 1;
    if (lv.snare && (s === 4 || s === 12 || (last && s >= 13))) this.snare(t, lv.snare * (last && s >= 13 ? 0.5 + (s - 13) * 0.15 : 1));
    if (lv.hats) this.hat(t, (s % 2 ? 0.05 : 0.08) * lv.hats, s % 4 === 2 ? 30 : 60);
  }

  voice(t, dur, midi, { amp = 0.2, type = 'sawtooth', attack = 0.005, release = 0.2, cutoff = 2000, cutEnv = 0, pan = 0, verb = 0.2, dly = 0, detune = 0, ducked = false }) {
    const ctx = this.ctx, f = midiHz(midi), end = t + dur + release;
    const g = ctx.createGain(), lp = ctx.createBiquadFilter();
    lp.type = 'lowpass';
    lp.Q.value = 0.5;
    lp.frequency.setValueAtTime(Math.min(18000, cutoff * (1 + cutEnv)), t);
    if (cutEnv) lp.frequency.setTargetAtTime(cutoff, t, 1 / 12);
    g.gain.setValueAtTime(0, t);
    g.gain.linearRampToValueAtTime(amp, t + attack);
    g.gain.setValueAtTime(amp, t + Math.max(attack, dur));
    g.gain.linearRampToValueAtTime(0, end);
    const oscs = [ctx.createOscillator()];
    if (detune) oscs.push(ctx.createOscillator());
    oscs.forEach((o, i) => {
      if (type === 'square') o.setPeriodicWave(this.square); else o.type = type;
      o.frequency.value = f;
      if (i) o.detune.value = detune;
      o.connect(lp);
      o.start(t);
      o.stop(end + 0.02);
    });
    lp.connect(g).connect(this.bus(pan, verb, dly, ducked));
  }

  /**
   * A shared mix bus for one placement in the stereo field and one amount of reverb and delay, so
   * each note costs an oscillator, a filter and a gain, not a panner and effect sends of its own.
   */
  bus(pan, verb, dly, ducked, filter = null) {
    const key = `${pan}|${verb}|${dly}|${ducked}|${filter ?? ''}`;
    this.buses ??= new Map();
    let input = this.buses.get(key);
    if (input) return input;
    const ctx = this.ctx;
    input = ctx.createGain();
    let tail = input;
    if (filter) {
      const [hp, lp] = filter;
      const h = ctx.createBiquadFilter();
      h.type = 'highpass'; h.frequency.value = hp;
      tail = tail.connect(h);
      if (lp) { const l = ctx.createBiquadFilter(); l.type = 'lowpass'; l.frequency.value = lp; tail = tail.connect(l); }
    }
    const p = ctx.createStereoPanner();
    p.pan.value = pan;
    tail.connect(p).connect(ducked ? this.duck : this.dry);
    for (const [amount, to] of [[verb, this.verbIn], [dly, this.dlyIn]]) {
      if (!amount) continue;
      const g = ctx.createGain();
      g.gain.value = amount;
      p.connect(g).connect(to);
    }
    this.buses.set(key, input);
    return input;
  }

  kick(t, amp) {
    const ctx = this.ctx, o = ctx.createOscillator(), g = ctx.createGain();
    o.frequency.setValueAtTime(155, t);
    o.frequency.setTargetAtTime(45, t, 1 / 30);
    g.gain.setValueAtTime(amp * 0.9, t);
    g.gain.setTargetAtTime(0, t, 1 / 7);
    o.connect(g).connect(this.dry);
    o.start(t);
    o.stop(t + 0.5);
    // The pads and bass duck under every kick, then swell back.
    this.duck.gain.setValueAtTime(0.35, t);
    this.duck.gain.setTargetAtTime(1, t, 0.09);
  }

  snare(t, amp) {
    this.noiseHit(t, 0.25, amp * 0.22, 900, 7000, 16, 0, 0.35);
    this.voice(t, 0.05, 54.4, { amp: 0.12 * amp, type: 'sine', release: 0.08, cutoff: 1000, verb: 0.2 });   // ~190 Hz body
  }

  hat(t, amp, decay) {
    this.noiseHit(t, 0.06, amp, 7000, 16000, decay, 0.25, 0.05);
  }

  noiseHit(t, dur, amp, hp, lp, decay, pan, verb) {
    const ctx = this.ctx, src = ctx.createBufferSource(), g = ctx.createGain();
    src.buffer = this.noise;
    g.gain.setValueAtTime(amp, t);
    g.gain.setTargetAtTime(0, t, 1 / decay);
    src.connect(g).connect(this.bus(pan, verb, 0, false, [hp, lp >= 15000 ? 0 : lp]));
    src.start(t, Math.random() * 0.5);
    src.stop(t + dur + 0.05);
  }
}

/** The chord for a scale degree: its triad voiced around D3, and its root an octave or two below. */
function chordOf(mv, degree) {
  const tone = (d) => mv.key + MINOR[d % 7] + 12 * Math.floor(d / 7);
  const chord = [degree, degree + 2, degree + 4].map((d, i) => {
    let m = tone(d);
    if (degree % 7 === 4 && i === 1) m += 1;   // the V chord is major
    while (m < 46) m += 12;
    while (m > 58) m -= 12;
    return m;
  }).sort((a, b) => a - b);
  let root = tone(degree);
  while (root > 44) root -= 12;
  while (root < 33) root += 12;
  return { chord, root };
}

/** All six movements, back to back: a little over eleven minutes before it loops. */
function arrange() {
  const blocks = [];
  for (const mv of MOVEMENTS) for (const [bars, levels] of SHAPE) blocks.push({ mv, bars, levels });
  let secs = blocks.reduce((s, b) => s + b.bars * 240 / b.mv.bpm, 0);
  while (secs < MIN_SONG_SECONDS) {   // never shorter than ten minutes
    const b = blocks[blocks.length - 2];
    blocks.splice(blocks.length - 1, 0, b);
    secs += b.bars * 240 / b.mv.bpm;
  }
  return blocks;
}

/** A stereo reverb tail: decaying noise, darker as it fades. */
function impulse(ctx, seconds) {
  const n = Math.floor(ctx.sampleRate * seconds), buf = ctx.createBuffer(2, n, ctx.sampleRate);
  for (let ch = 0; ch < 2; ch++) {
    const d = buf.getChannelData(ch);
    let lp = 0;
    for (let i = 0; i < n; i++) {
      const x = i / n, k = 0.6 - x * 0.45;   // the low-pass closes as the tail decays
      lp += k * ((Math.random() * 2 - 1) - lp);
      d[i] = lp * Math.pow(1 - x, 2.4) * (i < ctx.sampleRate * 0.012 ? i / (ctx.sampleRate * 0.012) : 1);
    }
  }
  return buf;
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

/**
 * Render part of the soundtrack offline (for checking it, or saving a clip): seconds of music from
 * the start of a movement, as an AudioBuffer.
 */
export async function renderSoundtrack(seconds, movement = 0) {
  const ctx = new OfflineAudioContext(2, Math.ceil(48000 * seconds), 48000);
  const music = new Soundtrack(ctx, ctx.destination);
  const c = music.cursor;
  c.block = music.blocks.findIndex((b) => b.mv === MOVEMENTS[movement]);
  for (let t = 0; t < seconds;) {
    const b = music.blocks[c.block], s16 = 15 / b.mv.bpm;
    if (c.bar === 0 && c.step === 0) for (const d of music.delays) d.delayTime.setValueAtTime(s16 * 3, t);
    music.step(b, c.bar, c.step, t, s16);
    t += s16;
    if (++c.step === 16) { c.step = 0; if (++c.bar === b.bars) { c.bar = 0; c.block = (c.block + 1) % music.blocks.length; } }
  }
  return ctx.startRendering();
}

/** For tests and the console: the soundtrack's length in seconds and its movements. */
export function soundtrackInfo() {
  const blocks = arrange();
  return {
    seconds: blocks.reduce((s, b) => s + b.bars * 240 / b.mv.bpm, 0),
    movements: MOVEMENTS.map((m) => m.name),
    bars: blocks.reduce((s, b) => s + b.bars, 0),
  };
}

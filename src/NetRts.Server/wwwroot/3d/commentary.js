// What the players say in the 3D view. Each player has their own voice; they speak up when something
// happens to them (a unit trained, a building finished or lost, a kill, an attack, research done, the
// end of the match), and trade taunts when it's quiet. The lines and recordings come from
// tools/voices (audio/voices.json). Only one player talks at a time, and nobody talks too often.

const TOWER_RANGE = 5;

// How keen a player is to comment on each kind of event (a chance), and how long they then keep
// quiet about it (seconds of match time). Bigger moments jump the queue.
const CATS = {
  start: { p: 1, quiet: 0, rank: 5 },
  victory: { p: 1, quiet: 0, rank: 10 },
  defeat: { p: 1, quiet: 0, rank: 9 },
  eliminated: { p: 1, quiet: 0, rank: 9 },
  destroyed_building: { p: 0.9, quiet: 12, rank: 7 },
  lost_building: { p: 1, quiet: 12, rank: 7 },
  under_attack: { p: 0.9, quiet: 30, rank: 6 },
  research: { p: 0.9, quiet: 10, rank: 4 },
  built: { p: 0.7, quiet: 15, rank: 4 },
  attack: { p: 0.8, quiet: 40, rank: 6 },
  kill: { p: 0.22, quiet: 12, rank: 3 },
  lost_unit: { p: 0.15, quiet: 15, rank: 3 },
  tower: { p: 0.25, quiet: 30, rank: 3 },
  build: { p: 0.35, quiet: 25, rank: 2 },
  train: { p: 0.12, quiet: 25, rank: 1 },
  gather: { p: 1, quiet: 90, rank: 1 },
  ahead: { p: 1, quiet: 120, rank: 2 },
  behind: { p: 1, quiet: 120, rank: 2 },
  taunt: { p: 1, quiet: 20, rank: 0 },
  chatter: { p: 1, quiet: 20, rank: 0 },
};

export class Commentary {
  /**
   * audio: GameAudio. badger: the Snow College theme (picks the theme's lines). names(): slot -> name.
   * onCaption(slot, name, text): show what was said.
   */
  constructor(audio, { badger, names, onCaption }) {
    this.audio = audio;
    this.badger = badger;
    this.names = names;
    this.onCaption = onCaption;
    this.data = null;
    this.byCat = null;
    audio.voices().then((d) => this.load(d));
    this.reset();
  }

  load(data) {
    if (!data) return;
    this.data = data;
    this.byCat = new Map();
    data.lines.forEach((l, i) => {
      if (l.theme && (l.theme === 'snow') !== this.badger) return;
      if (!this.byCat.has(l.cat)) this.byCat.set(l.cat, []);
      this.byCat.get(l.cat).push(i);
    });
  }

  /** Forget the moment (a jump in a replay). */
  reset() {
    this.queue = [];
    this.talking = false;
    this.spoke = new Map();      // slot -> performance.now() when they last spoke
    this.quietUntil = 0;
    this.lastSaid = new Map();   // "slot|cat" -> tick
    this.recent = [];            // recently used line indexes, not to be repeated soon
    this.lastLine = -1e9;        // tick of the last line anyone said
    this.tickNow = 0;
    this.lastScoreTalk = 0;
    this.started = false;
    this.ended = false;
    this.voiceShift = null;
  }

  /** Each player's voice: different for every slot, and shuffled between matches. */
  voiceOf(slot) {
    const n = this.data?.voices.length || 16;
    return (slot + (this.voiceShift ?? 0)) % n;
  }

  /**
   * A new frame: compare it with the previous one and decide who has something to say. later(fn)
   * runs fn when the picture reaches this frame.
   */
  update(prev, s, later) {
    if (!this.byCat || !s) return;
    if (this.voiceShift == null && s.players?.length) {
      this.voiceShift = [...String(s.players[0].playerId ?? '')].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7) % 16;
      for (const p of s.players) this.audio.preload(this.voiceOf(p.slot));
    }
    const now = s.tick;   // comments are paced in ticks (a second each at normal speed)
    this.tickNow = now;
    const out = [];
    const say = (slot, cat, sub) => out.push({ slot, cat, sub });
    const alive = (s.players || []).filter((p) => !p.eliminated);

    if (!this.started) {
      this.started = true;
      if (s.tick < 20) for (const p of s.players || []) say(p.slot, 'start');
    }
    if (s.outcome && !this.ended) {
      this.ended = true;
      for (const p of s.players || []) say(p.slot, p.playerId === s.outcome.winnerId ? 'victory' : 'defeat');
    }
    if (prev && s.tick > prev.tick && !s.outcome) {
      this.diff(prev, s, say);
      // Every two minutes or so, the leader and the trailer weigh in.
      if (now - this.lastScoreTalk > 120 && alive.length > 1 && s.tick > 90) {
        const scored = alive.filter((p) => p.score).map((p) => ({ p, t: total(p.score) })).sort((a, b) => b.t - a.t);
        if (scored.length > 1 && scored[0].t > scored[scored.length - 1].t * 1.3) {
          this.lastScoreTalk = now;
          say(scored[0].p.slot, 'ahead');
          say(scored[scored.length - 1].p.slot, 'behind');
        }
      }
      // Quiet for a while: someone fills the silence.
      if (now - this.lastLine > 22 && !this.talking && alive.length && Math.random() < 0.08) {
        const p = alive[Math.floor(Math.random() * alive.length)];
        say(p.slot, Math.random() < 0.6 && alive.length > 1 ? 'taunt' : 'chatter');
      }
    }

    for (const c of out) this.consider(c, now, later);
  }

  /** What changed between two frames, for each player. */
  diff(prev, s, say) {
    const units = new Map(s.units.map((u) => [u.id, u]));
    const prevUnits = new Map(prev.units.map((u) => [u.id, u]));
    const bldgs = new Map(s.buildings.map((b) => [b.id, b]));
    const attackersOf = (list, id) => list.filter((u) => u.activity === 'Attacking' && u.targetId === id);

    // Units trained, lost and killed.
    for (const u of s.units) {
      if (!prevUnits.has(u.id) && s.tick > 5) say(u.owner, 'train', u.type);
    }
    for (const u of prev.units) {
      if (units.has(u.id)) continue;
      say(u.owner, 'lost_unit');
      const killer = attackersOf(prev.units, u.id).find((a) => a.owner !== u.owner)?.owner ?? this.towerNear(prev, u);
      if (killer != null) say(killer, 'kill');
    }
    // Hurt by a tower (nobody else was hitting it).
    for (const u of s.units) {
      const before = prevUnits.get(u.id);
      if (before && u.hp < before.hp && !attackersOf(s.units, u.id).length) {
        const tower = this.towerNear(s, u);
        if (tower != null) say(tower, 'tower');
      }
    }
    // Buildings started, finished, hit and destroyed.
    for (const b of s.buildings) {
      const before = prev.buildings.find((x) => x.id === b.id);
      if (!before) { if (s.tick > 5) say(b.owner, 'build'); continue; }
      if (b.completed && !before.completed) say(b.owner, 'built', b.type);
      if (b.completed && b.hp < before.hp) say(b.owner, 'under_attack');
    }
    for (const b of prev.buildings) {
      if (bldgs.has(b.id)) continue;
      say(b.owner, 'lost_building');
      const by = attackersOf(prev.units, b.id).find((a) => a.owner !== b.owner)?.owner;
      if (by != null) say(by, 'destroyed_building');
    }
    // Research finished, players knocked out, armies on the move, and workers early on.
    const prevPlayers = new Map((prev.players || []).map((p) => [p.playerId, p]));
    for (const p of s.players || []) {
      const was = prevPlayers.get(p.playerId);
      if (!was) continue;
      if ((p.upgrades?.length ?? 0) > (was.upgrades?.length ?? 0)) say(p.slot, 'research');
      if (p.eliminated && !was.eliminated) say(p.slot, 'eliminated');
      const fighting = (list) => list.filter((u) => u.owner === p.slot && u.type !== 'Worker' && u.activity === 'Attacking').length;
      const now = fighting(s.units), before = fighting(prev.units);
      if (now >= 4 && now - before >= 3) say(p.slot, 'attack');
      if (s.tick > 15 && s.tick < 200 && Math.random() < 0.01) say(p.slot, 'gather');
    }
  }

  /** The owner of an enemy guard tower within reach of u, if there is one. */
  towerNear(s, u) {
    for (const b of s.buildings) {
      if (b.type === 'GuardTower' && b.completed && b.owner !== u.owner
        && Math.max(Math.abs(b.x - u.x), Math.abs(b.y - u.y)) <= TOWER_RANGE) return b.owner;
    }
    return null;
  }

  consider({ slot, cat, sub }, now, later) {
    const rule = CATS[cat];
    const key = `${slot}|${cat}`;
    if (now - (this.lastSaid.get(key) ?? -1e9) < rule.quiet) return;
    if (Math.random() > rule.p) return;
    // Some kinds have a line for each building or unit type.
    const full = sub && this.byCat.has(`${cat}_${sub}`) ? `${cat}_${sub}` : cat;
    if (!this.byCat.has(full)) return;
    this.lastSaid.set(key, now);
    const item = { slot, cat: full, rank: rule.rank, at: performance.now() };
    later(() => this.enqueue(item));
  }

  enqueue(item) {
    // The queue is short: big moments push ahead, stale small talk is dropped.
    this.queue.push(item);
    this.queue.sort((a, b) => b.rank - a.rank);
    if (this.queue.length > 4) this.queue.length = 4;
    this.next();
  }

  async next() {
    if (this.talking) return;
    const now = performance.now();
    this.queue = this.queue.filter((q) => now - q.at < (q.rank >= 6 ? 9000 : 4500));
    // Real time matters too (a replay at 32 ticks a second would otherwise be all talk): a pause
    // between lines, and each player speaks at most every few seconds unless it's a big moment.
    const big = (q) => q.rank >= 7;
    if (now < this.quietUntil && !this.queue.some(big)) {
      clearTimeout(this.wake);
      this.wake = setTimeout(() => this.next(), this.quietUntil - now + 20);
      return;
    }
    const i = this.queue.findIndex((q) => big(q) || now - (this.spoke.get(q.slot) ?? -1e9) > 7000);
    if (i < 0) return;
    const [item] = this.queue.splice(i, 1);
    this.spoke.set(item.slot, now);
    const lines = this.byCat.get(item.cat).filter((i) => !this.recent.includes(i));
    const pool = lines.length ? lines : this.byCat.get(item.cat);
    const line = pool[Math.floor(Math.random() * pool.length)];
    this.recent.push(line);
    if (this.recent.length > 40) this.recent.shift();
    this.talking = true;
    this.lastLine = this.tickNow ?? 0;
    let secs = 0;
    try { secs = await this.audio.speak(this.voiceOf(item.slot), line); } catch { secs = 0; }
    if (secs > 0) this.onCaption?.(item.slot, this.names().get(item.slot) ?? `Player ${item.slot + 1}`, this.data.lines[line].text, secs);
    setTimeout(() => {
      this.talking = false;
      this.lastLine = this.tickNow ?? 0;
      this.quietUntil = performance.now() + 2000 + Math.random() * 2000;
      this.next();
    }, (secs > 0 ? secs + 0.3 : 0.2) * 1000);
  }
}

const total = (score) => score.total ?? score.destruction + score.economy + score.survival;

// The pick-up game's cards and the plan's rules of thumb (what each step needs, what it says). Costs,
// stats and prerequisites come from GET /api/v1/rules (setCatalog), names from lore.js; only the course
// numbers on the class cards and the one-line blurbs live here. Items are API names on the wire
// (Barracks, TechLab, Weapons1...) and Snow names on the page.
import { unitName, upgradeName, buildingName, badgerize } from '../lore.js';

export const START_GRUBS = 1500;
export const MAX_STEPS = 6;
export const MAX_FOREVER = 3;
export const MAX_EACH_BUILDING = 3;
/** The building every player gets for free, besides the HQ. */
export const FREE_BUILDING = 'Barracks';

/** What the start pile can buy. */
export const START_ITEMS = ['Soldier', 'Archer', 'Scout', 'Worker', 'Barracks', 'ResourceDepot', 'TechLab', 'GuardTower'];
/** Every card, in hand order. */
export const ALL_ITEMS = ['Worker', 'Soldier', 'Archer', 'Scout', 'Trebuchet', 'Barracks', 'ResourceDepot', 'TechLab', 'GuardTower',
  'SiegeWorkshop', 'Weapons1', 'Weapons2', 'Armor1', 'Armor2', 'Mobility1', 'Harvesting1', 'Fortify1', 'SiegeEngineering'];

/** Class cards show a course number instead of a picture (display only). */
const COURSE = {
  Weapons1: 'CHEM 1110', Weapons2: 'CHEM 2310', Armor1: 'CS 2450', Armor2: 'CS 3450',
  Mobility1: 'CS 2420', Harvesting1: 'BIOL 1010', Fortify1: 'CMGT 1010', SiegeEngineering: 'WELD 1010',
};
const BLURB = {
  Worker: 'Mines grubs', Soldier: 'Tough, fights up close', Archer: 'Shoots from a distance', Scout: 'Fast and weak',
  Trebuchet: 'Long-range cannon, slow', Barracks: 'Trains Soldiers, Archers, Scouts', ResourceDepot: 'Closer drop-off, faster mining',
  TechLab: 'Unlocks classes', GuardTower: 'Shoots attackers', SiegeWorkshop: 'Builds Snow Mobiles',
};
/** Pictures: renders of the game's own 3D models. */
export const art = (id) => `/images/pickup/${id}.webp`;
export const course = (id) => COURSE[id] ?? null;

/** id -> { id, name, type: 'unit' | 'building' | 'research', cost, needs: [id], stats, blurb } */
let ITEMS = {};
let BASE_WORKERS = 5;

/** Builds the cards from GET /api/v1/rules. */
export function setCatalog(rules) {
  const unit = (t) => rules.units.find((u) => u.type === t);
  const building = (t) => rules.buildings.find((b) => b.type === t);
  const upgrade = (t) => rules.upgrades.find((u) => u.type === t);
  const lab = rules.buildings.find((b) => b.canResearch)?.type ?? 'TechLab';
  const always = new Set(['CommandCenter']);
  ITEMS = {};
  for (const id of ALL_ITEMS) {
    let it;
    const u = unit(id), b = building(id), r = upgrade(id);
    if (u) {
      it = { type: 'unit', name: unitName(id), cost: u.cost, needs: [u.producedAt].filter((x) => x && !always.has(x)),
        stats: { hp: u.maxHp, atk: u.damage, arm: u.armor, rng: u.range } };
    } else if (b) {
      it = { type: 'building', name: buildingName(id).replace(/^The /, ''), cost: b.cost,
        needs: [...(b.requires || []), b.requiresUpgrade].filter((x) => x && !always.has(x)),
        stats: { hp: b.maxHp, atk: b.damage || null, arm: b.armor, rng: b.range || null } };
    } else if (r) {
      it = { type: 'research', name: upgradeName(id), cost: r.cost, needs: [lab, r.requires].filter(Boolean), stats: null, blurb: effectText(r.effect) };
    } else continue;
    ITEMS[id] = { id, blurb: BLURB[id] ?? '', ...it };
  }
  BASE_WORKERS = rules.economy?.startingWorkers ?? 5;
}

/** An upgrade's effect from the rules, in the page's words. */
function effectText(effect) {
  return badgerize(effect ?? '')
    .replace(/\bore\b/g, 'grub').replace(/\bguard towers\b/g, `${item('GuardTower')?.name ?? 'tower'}s`)
    .replace(/\bTrebuchets\b/g, `${unitName('Trebuchet')}s`).replace(/\s*\(tenths of a tile per tick\)/, '');
}

export const item = (id) => ITEMS[id] ?? null;
export const ready = () => Object.keys(ITEMS).length > 0;
/** unit | building | research | worker: the card's colour. */
export const tcls = (id) => (id === 'Worker' ? 'worker' : ITEMS[id]?.type ?? 'unit');
export const nameOf = (id) => ITEMS[id]?.name ?? id;
export function plural(id, n) {
  const nm = nameOf(id);
  return n === 1 || id === 'Barracks' ? nm : `${nm}s`;
}
export const clock = (t) => { t = Math.max(0, Math.round(t)); return `${Math.floor(t / 60)}:${String(t % 60).padStart(2, '0')}`; };
export const ordinal = (n) => `${n}${n % 100 >= 11 && n % 100 <= 13 ? 'th' : ['th', 'st', 'nd', 'rd'][n % 10] ?? 'th'}`;

// ------------------------------------------------------------------ the plan

export const emptyPlan = () => ({
  start: [], steps: [], forever: ['Soldier'],
  attack: { trigger: 'army', army: 12, atSecond: 90, target: 'base', defend: true },
});

/** The plan with every field in a fixed order and shape, so two plans compare as JSON. */
export function normalize(p) {
  const a = p?.attack ?? {};
  return {
    start: (p?.start || []).map((x) => ({ item: x.item, count: x.count | 0 })),
    steps: (p?.steps || []).map((s) => ({ kind: s.kind, item: s.kind === 'workers' ? null : s.item ?? null, count: s.count | 0, keep: !!s.keep })),
    forever: [...(p?.forever || [])],
    attack: { trigger: a.trigger === 'time' ? 'time' : 'army', army: a.army ?? 12, atSecond: a.atSecond ?? 90, target: a.target === 'nearest' ? 'nearest' : 'base', defend: a.defend !== false },
  };
}
export const samePlan = (a, b) => JSON.stringify(normalize(a)) === JSON.stringify(normalize(b));

export const startN = (plan, id) => plan.start.find((x) => x.item === id)?.count || 0;
export const spent = (plan) => plan.start.reduce((a, x) => a + (ITEMS[x.item]?.cost ?? 0) * x.count, 0);
export const left = (plan) => START_GRUBS - spent(plan);
export const stepItem = (s) => (s.kind === 'workers' ? 'Worker' : s.item);

/** What the player has before step idx: the free buildings, the start pile's, and earlier build and class steps. */
export function owned(plan, idx) {
  const o = new Set([FREE_BUILDING]);
  plan.start.forEach((x) => { if (ITEMS[x.item]?.type === 'building') o.add(x.item); });
  plan.steps.slice(0, idx).forEach((s) => { if (s.kind === 'build' || s.kind === 'research') o.add(s.item); });
  return o;
}
export const missing = (id, o) => (ITEMS[id]?.needs || []).filter((n) => !o.has(n)).map(nameOf);

export function workersBefore(plan, i) {
  let w = BASE_WORKERS + startN(plan, 'Worker');
  plan.steps.slice(0, i).forEach((s) => { if (s.kind === 'workers') w = Math.max(w, s.count); });
  return w;
}
export function haveBefore(plan, id, idx) {
  let c = startN(plan, id);
  plan.steps.slice(0, idx).forEach((s) => { if (s.kind === 'train' && s.item === id) c = Math.max(c, s.count); });
  return c;
}

/** Why step i won't do anything (or can't start), or null. */
export function issue(plan, i) {
  const s = plan.steps[i], o = owned(plan, i);
  if (s.kind === 'workers') { const w = workersBefore(plan, i); return s.count <= w ? `You already have ${w} workers` : null; }
  if (s.kind === 'research' && o.has(s.item)) return 'Already taken';
  const m = missing(s.item, o);
  if (m.length) return `Needs ${m.join(' and ')} first`;
  if (s.kind === 'train') { const h = haveBefore(plan, s.item, i); if (s.count <= h) return `You already have ${h}`; }
  return null;
}

export function stepText(s) {
  switch (s.kind) {
    case 'workers': return `Workers up to ${s.count}`;
    case 'train': return `${plural(s.item, 2)} up to ${s.count}`;
    case 'build': return s.item === FREE_BUILDING ? `Build more ${nameOf(s.item)}` : `Build a ${nameOf(s.item)}`;
    default: return `Take ${nameOf(s.item)}`;
  }
}
export const stepSub = (s) => (s.keep ? 'Kept at this level: replaces any that die' : s.kind === 'workers' ? 'More workers mine grubs faster' : ITEMS[s.item]?.blurb ?? '');
export const foreverText = (plan) => plan.forever.map(nameOf).join(', ');

/** Whether an item can be bought for the start pile, and why not. */
export function startBlock(plan, id) {
  const it = ITEMS[id];
  if (it.type === 'building' && startN(plan, id) >= MAX_EACH_BUILDING) return 'Three at most';
  if (it.cost > left(plan)) return 'Not enough grubs';
  return '';
}

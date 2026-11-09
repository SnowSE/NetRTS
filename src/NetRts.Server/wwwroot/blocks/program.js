// Block Bot programs: the blocks (conditions, actions, rules), the starter strategies, the words the
// editor shows for them, and the game's rules from the server.
import { api } from '../util.js';
import { SNOW, say, unitName, upgradeName, buildingName, ORE, badgerize, spaced } from '../lore.js';

// ------------------------------------------------------------------ from the server

// What a program may say (GET /api/v1/blocks/vocabulary: the kinds of check and action, the choices for
// each, the limits and the defaults, from the server's validator) and the balance numbers (GET
// /api/v1/rules: units, buildings and upgrades, and what each does), so neither is typed twice. Loaded
// when a Block Bot page mounts (mountBlocks waits for them); a failure is tried again on the next mount.
// Everything below that reads them runs only once they're in.
export let VOCAB = null;
export let RULES = null;
let loading = null;
export function loadVocabulary() {
  if (VOCAB && RULES) return Promise.resolve();
  loading ??= Promise.all([api('/api/v1/blocks/vocabulary'), api('/api/v1/rules')])
    .then(([vocab, rules]) => { VOCAB = vocab; RULES = rules; })
    .finally(() => { loading = null; });
  return loading;
}

/** maxRules, maxActionsPerRule, maxDepth, maxSwitches, maxNameLength, maxSwitchNameLength, maxNumber, maxMixUnits. */
export const LIMITS = () => VOCAB.limits;
/** What the server assumes when a block leaves a choice out: site, who, target, scoutTarget, expandBuilding. */
const DEFAULTS = () => VOCAB.defaults;
/** Whether the server knows this kind of check ('cond') or action: the editor only offers those. */
export const knows = (type, kind) => (type === 'cond' ? VOCAB.conditionKinds : VOCAB.actionKinds).includes(kind);

// ------------------------------------------------------------------ words

// The server's lists, in its order, except buildings: in the order a base usually goes up (anything new
// goes last).
const BUILDING_ORDER = ['Barracks', 'TechLab', 'GuardTower', 'ResourceDepot', 'SiegeWorkshop', 'CommandCenter'];
const byHint = (list, hint) => list
  .map((x, i) => [x, hint.includes(x) ? hint.indexOf(x) : hint.length + i])
  .sort((a, b) => a[1] - b[1]).map(([x]) => x);

export const UNITS = () => RULES.units.map((u) => u.type);
/** The units that fight: the ones a rule can send ("who"). */
export const FIGHTERS = () => UNITS().filter((t) => VOCAB.who.includes(t));
export const BUILDINGS = () => byHint(RULES.buildings.map((b) => b.type), BUILDING_ORDER);
export const UPGRADES = () => RULES.upgrades.map((u) => u.type);
const OP_WORDS = { '<=': '≤', '>=': '≥' };
export const OPS = () => VOCAB.ops.map((o) => [o, OP_WORDS[o] ?? o]);

export function unitWord(t, many = false) {
  const n = unitName(t);
  if (n === 'Snow Mobile') return many ? 'Snow Mobiles' : 'Snow Mobile';
  return many ? `${n.toLowerCase()}s` : n.toLowerCase();
}
/** Building names without a leading "The" ("build a Bell Tower", not "build a The Bell Tower"). */
export const bWord = (t) => buildingName(t).replace(/^The /, '');

/**
 * What an upgrade ("class") does, in the player's words: `short` for its menu entry ("+2 damage"),
 * `detail` and `meta` for the line under the rule. Null until the rules are in.
 */
export function upgradeInfo(type) {
  const u = RULES?.upgrades?.find((x) => x.type === type);
  if (!u) return null;
  const tier = /2$/.test(type) ? 2 : 1;
  const m = /^\+(\d+) (?:more )?(damage|armor|speed|ore)\b/.exec(u.effect ?? '');
  const n = m ? Number(m[1]) : 0;
  const short = !m ? (type === 'SiegeEngineering' ? `unlocks ${unitWord('Trebuchet', true)}` : badgerize(u.effect))
    : `+${n}${tier > 1 ? ' more' : ''} ${m[2] === 'ore' ? `${n === 1 && SNOW ? 'grub' : ORE} per dig` : m[2]}`;
  let detail = badgerize(u.effect).replace(/\bore\b/g, ORE);
  if (SNOW) {
    detail = detail.replace(/\bguard towers\b/g, `${bWord('GuardTower')}s`)
      .replace(/\bTrebuchets\b/g, unitWord('Trebuchet', true))
      .replace(/\b1 grubs\b/g, '1 grub');
  }
  if (tier > 1 && u.requires && (m?.[2] === 'damage' || m?.[2] === 'armor')) detail += ` (+${n * tier} in all, with ${upgradeName(u.requires)})`;
  if (m?.[2] === 'speed') {
    const from = (RULES.units?.find((x) => x.type === 'Soldier')?.speed ?? 10) + n * (tier - 1);
    detail = detail.replace(/\s*\(tenths of a tile per tick\)/, '') + ` (a ${unitWord('Soldier')} goes ${from} → ${from + n} tenths of a tile a tick)`;
  } else if (m?.[2] === 'ore') {
    const from = (RULES.economy?.gatherPerTick ?? 2) + n * (tier - 1);
    detail += ` (${from} → ${from + n})`;
  }
  const lab = RULES.buildings?.find((b) => b.canResearch)?.type;
  const meta = [
    `${u.cost} ${ORE}`,
    `${u.researchTicks} ticks`,
    u.requires ? `after ${upgradeName(u.requires)}` : '',
    lab ? `at the ${bWord(lab)}` : '',
  ].filter(Boolean).join(' · ');
  return { short, detail, meta };
}
/** What a count can count: fighters, workers and grubs first, then each fighter, then each building. */
export function WHAT() {
  const can = VOCAB.countable;
  const words = { fighters: 'fighters', workers: 'workers', grubs: ORE };
  const units = UNITS();
  const listed = [
    ...Object.keys(words).filter((k) => can.includes(k)).map((k) => [k, words[k]]),
    ...FIGHTERS().filter((t) => can.includes(t)).map((t) => [t, unitWord(t, true)]),
    ...BUILDINGS().filter((b) => can.includes(b)).map((b) => [b, bWord(b)]),
  ];
  // Workers are counted as "workers"; anything else the server adds is shown by its own name.
  const rest = can.filter((k) => !listed.some(([v]) => v === k) && !units.includes(k)).map((k) => [k, spaced(k).toLowerCase()]);
  return [...listed, ...rest];
}
export const WHO = () => VOCAB.who.map((w) => [w, w === 'fighters' ? 'all fighters' : `my ${unitWord(w, true)}`]);
const word = (words, k) => words[k] ?? spaced(k).toLowerCase();
export const SITES = () => VOCAB.sites.map((s) => [s, word({ TowardCenter: 'toward the middle', NearOre: `near the ${ORE}`, TowardEnemy: 'toward the enemy' }, s)]);
export const TARGETS = () => VOCAB.targets.map((t) => [t, word({ enemyBase: 'the enemy base', nearestEnemy: 'the nearest enemy' }, t)]);
export const SCOUT_TARGETS = () => VOCAB.scoutTargets.map((t) => [t, word({ enemyBase: 'find the enemy base', explore: `explore (find more ${ORE})` }, t)]);
export const EXPAND_BUILDINGS = () => VOCAB.expandBuildings.map((b) => [b, bWord(b)]);
/** The server's default for a choice a block leaves out (site, who, target, scoutTarget, expandBuilding). */
export const byDefault = (key) => DEFAULTS()[key];

// ------------------------------------------------------------------ blocks

export const C = {
  always: () => ({ kind: 'always' }),
  enemyNearBase: () => ({ kind: 'enemyNearBase' }),
  seeEnemy: () => ({ kind: 'seeEnemy' }),
  count: (what = 'fighters', op = '>=', value = 8) => ({ kind: 'count', what, op, value }),
  minute: (op = '>=', value = 3) => ({ kind: 'minute', op, value }),
  grubsLeft: (op = '<=', value = 300) => ({ kind: 'grubsLeft', op, value }),
  switch: (name) => ({ kind: 'switch', name }),
  and: (a, b) => ({ kind: 'and', a, b }),
  or: (a, b) => ({ kind: 'or', a, b }),
  not: (a) => ({ kind: 'not', a }),
};
export const A = {
  train: (unit = 'Soldier') => ({ kind: 'train', unit }),
  trainMix: (units = ['Soldier', 'Soldier', 'Archer']) => ({ kind: 'trainMix', units }),
  build: (building = 'Barracks', site = byDefault('site')) => ({ kind: 'build', building, site }),
  research: (upgrade = 'Weapons1') => ({ kind: 'research', upgrade }),
  attack: (who = byDefault('who'), target = byDefault('target')) => ({ kind: 'attack', who, target }),
  defend: (who = byDefault('who')) => ({ kind: 'defend', who }),
  retreat: (who = byDefault('who')) => ({ kind: 'retreat', who }),
  scout: (target = byDefault('scoutTarget')) => ({ kind: 'scout', target }),
  expand: (building = byDefault('expandBuilding')) => ({ kind: 'expand', building }),
};
export const R = (when, ...actions) => ({ enabled: true, when, do: actions });

/** How deep the check at path sits: a rule's own check ('rules.3.when') is 1, each and/or/not inside it one more. */
export const condDepth = (path) => path.split('.').length - 2;
/** How many levels a check has: 1, or one more than its deepest part for and, or and not. */
export function condHeight(c) {
  if (c?.kind === 'and' || c?.kind === 'or') return 1 + Math.max(condHeight(c.a), condHeight(c.b));
  if (c?.kind === 'not') return 1 + condHeight(c.a);
  return 1;
}
/** Whether the check at path can go inside an and, or or not without nesting deeper than the server allows. */
export const canCombine = (program, path) => condDepth(path) + condHeight(at(program, path)) <= LIMITS().maxDepth;

/** The starter strategies, made when asked for (they use the server's defaults). */
export const starters = () => ({
  blank: { label: 'Blank', program: { name: 'New strategy', switches: [], rules: [] } },
  first: {
    label: 'My First Bot',
    program: {
      name: 'My First Bot',
      switches: [],
      rules: [
        R(C.count('workers', '<', 10), A.train('Worker')),
        R(C.count('Barracks', '<', 1), A.build('Barracks')),
        R(C.always(), A.train('Soldier')),
        R(C.count('fighters', '>=', 6), A.attack()),
      ],
    },
  },
  rush: {
    label: say('Quick Rush', 'Honey Rush'),
    program: {
      name: say('Quick Rush', 'Honey Rush'),
      switches: ['Attack!'],
      rules: [
        R(C.enemyNearBase(), A.defend()),
        R(C.count('workers', '<', 12), A.train('Worker')),
        R(C.and(C.count('Barracks', '<', 2), C.count('grubs', '>=', 150)), A.build('Barracks', 'TowardEnemy')),
        R(C.or(C.switch('Attack!'), C.count('fighters', '>=', 8)), A.attack()),
        R(C.always(), A.trainMix(['Soldier', 'Soldier', 'Archer'])),
      ],
    },
  },
  turtle: {
    label: 'Turtle Town',
    program: {
      name: 'Turtle Town',
      switches: ['Turtle up', 'Attack!'],
      rules: [
        R(C.enemyNearBase(), A.defend()),
        R(C.switch('Turtle up'), A.retreat()),
        R(C.count('workers', '<', 16), A.train('Worker')),
        R(C.count('ResourceDepot', '<', 1), A.build('ResourceDepot', 'NearOre')),
        R(C.grubsLeft('<=', 300), A.expand('ResourceDepot')),
        R(C.count('Barracks', '<', 1), A.build('Barracks')),
        R(C.count('TechLab', '<', 1), A.build('TechLab')),
        R(C.count('GuardTower', '<', 2), A.build('GuardTower', 'TowardEnemy')),
        R(C.always(), A.research('Armor1'), A.research('Weapons1')),
        R(C.or(C.switch('Attack!'), C.count('fighters', '>=', 16)), A.attack()),
        R(C.always(), A.trainMix(['Soldier', 'Archer', 'Archer'])),
      ],
    },
  },
});

export const at = (obj, path) => path.split('.').reduce((o, k) => o?.[k], obj);
export function setAt(obj, path, value) {
  const keys = path.split('.');
  const last = keys.pop();
  const parent = keys.reduce((o, k) => o[k], obj);
  parent[last] = value;
}

/** For comparing a program here with the one the server runs: no nulls, same key order. */
export function canon(x) {
  if (Array.isArray(x)) return x.map(canon);
  if (x && typeof x === 'object') {
    const out = {};
    for (const k of Object.keys(x).sort()) {
      if (x[k] !== null && x[k] !== undefined) out[k] = canon(x[k]);
    }
    return out;
  }
  return x;
}
export const sameProgram = (a, b) => JSON.stringify(canon(a)) === JSON.stringify(canon(b));

/** Reads a downloaded strategy file; returns the program or throws with a message a kid can read. */
export async function readStrategyFile(file) {
  const text = await file.text();
  let data;
  try { data = JSON.parse(text); } catch { throw new Error('That file isn’t a strategy file.'); }
  const program = data?.program ?? data;
  if (!program || !Array.isArray(program.rules)) throw new Error('That file has no rules in it.');
  program.switches = Array.isArray(program.switches) ? program.switches : [];
  program.name = String(program.name || file.name.replace(/\.json$/i, '')).slice(0, LIMITS().maxNameLength);
  await api('/api/v1/blocks/validate', { method: 'POST', body: program });
  return program;
}

export const describeRules = (p) => `${p.rules.length} rule${p.rules.length === 1 ? '' : 's'}${p.switches?.length ? ` · ${p.switches.length} switch${p.switches.length === 1 ? '' : 'es'}` : ''}`;

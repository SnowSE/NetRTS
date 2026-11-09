// Block Bot programs: the blocks (conditions, actions, rules), the starter strategies, the words the
// editor shows for them, and the game's rules from the server.
import { api } from '../util.js';
import { SNOW, say, unitName, upgradeName, buildingName, ORE, badgerize } from '../lore.js';

// ------------------------------------------------------------------ words

export const UNITS = ['Worker', 'Soldier', 'Archer', 'Scout', 'Trebuchet'];
export const FIGHTERS = UNITS.slice(1);
export const BUILDINGS = ['Barracks', 'TechLab', 'GuardTower', 'ResourceDepot', 'SiegeWorkshop', 'CommandCenter'];
export const UPGRADES = ['Weapons1', 'Weapons2', 'Armor1', 'Armor2', 'Mobility1', 'Mobility2', 'Harvesting1', 'Harvesting2', 'SiegeEngineering'];
export const OPS = [['<', '<'], ['<=', '≤'], ['>', '>'], ['>=', '≥'], ['=', '=']];

export function unitWord(t, many = false) {
  const n = unitName(t);
  if (n === 'Snow Mobile') return many ? 'Snow Mobiles' : 'Snow Mobile';
  return many ? `${n.toLowerCase()}s` : n.toLowerCase();
}
/** Building names without a leading "The" ("build a Bell Tower", not "build a The Bell Tower"). */
export const bWord = (t) => buildingName(t).replace(/^The /, '');

// The balance numbers, from GET /api/v1/rules (GameRules on the server), so what a class does is
// never typed twice. Loaded when a Block Bot page mounts (mountBlocks waits for it); a failure is tried
// again on the next mount.
export let RULES = null;
let loading = null;
export function loadRules() {
  if (RULES) return Promise.resolve();
  loading ??= api('/api/v1/rules')
    .then((r) => { RULES = r; })
    .finally(() => { loading = null; });
  return loading;
}

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
/** The site theme (the global Snow College / original toggle), so the server explains itself in the same words. */
export const THEME = SNOW ? 'snow' : 'classic';
export const WHAT = () => [
  ['fighters', 'fighters'], ['workers', 'workers'], ['grubs', ORE],
  ...FIGHTERS.map((t) => [t, unitWord(t, true)]),
  ...BUILDINGS.map((b) => [b, bWord(b)]),
];
export const WHO = () => [['fighters', 'all fighters'], ...FIGHTERS.map((t) => [t, `my ${unitWord(t, true)}`])];
export const SITES = () => [['TowardCenter', 'toward the middle'], ['NearOre', `near the ${ORE}`], ['TowardEnemy', 'toward the enemy']];
export const TARGETS = [['enemyBase', 'the enemy base'], ['nearestEnemy', 'the nearest enemy']];

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
  build: (building = 'Barracks', site = 'TowardCenter') => ({ kind: 'build', building, site }),
  research: (upgrade = 'Weapons1') => ({ kind: 'research', upgrade }),
  attack: (who = 'fighters', target = 'enemyBase') => ({ kind: 'attack', who, target }),
  defend: (who = 'fighters') => ({ kind: 'defend', who }),
  retreat: (who = 'fighters') => ({ kind: 'retreat', who }),
  scout: (target = 'enemyBase') => ({ kind: 'scout', target }),
  expand: (building = 'ResourceDepot') => ({ kind: 'expand', building }),
};
export const R = (when, ...actions) => ({ enabled: true, when, do: actions });

export const STARTERS = {
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
};

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
  program.name = String(program.name || file.name.replace(/\.json$/i, '')).slice(0, 40);
  await api('/api/v1/blocks/validate', { method: 'POST', body: program });
  return program;
}

export const describeRules = (p) => `${p.rules.length} rule${p.rules.length === 1 ? '' : 's'}${p.switches?.length ? ` · ${p.switches.length} switch${p.switches.length === 1 ? '' : 'es'}` : ''}`;

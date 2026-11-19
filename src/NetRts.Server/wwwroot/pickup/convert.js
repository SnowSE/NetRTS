// "Take your bot home": a pick-up plan as a Block Bot program, and that program as the link
// #/blocks/import?program=<base64url JSON>. No imports, so a test can run it under node.
//
// Block Bot checks every rule each tick from the top, so the steps become rules in the same order: the
// defend rule first, one rule per step ("workers < N: train", "Housing < have+1: build", a class once),
// the attack, then the Forever units as a mix. It plays close to the pick-up plan, not exactly: Block
// Bot re-runs steps when units die and there is no starting pile at home.

/** Where a built building goes (Block Bot's sites); anything else goes toward the centre. */
const SITE = { GuardTower: 'TowardEnemy', ResourceDepot: 'NearOre' };
const UNITS = ['Worker', 'Soldier', 'Archer', 'Scout', 'Trebuchet'];
/** The free building every pick-up player starts with. */
const FREE = 'Barracks';
const MAX_NAME = 40;

/** plan: a pick-up plan (API shape); name: the player's pick-up name. */
export function toBlockProgram(plan, name) {
  const rule = (when, act, extra = {}) => ({ enabled: true, when, do: [act], ...extra });
  const count = (what, op, value) => ({ kind: 'count', what, op, value });
  const rules = [];
  const attack = plan.attack ?? {};
  if (attack.defend !== false) rules.push(rule({ kind: 'enemyNearBase' }, { kind: 'defend', who: 'fighters' }));
  const have = {};
  for (const x of plan.start ?? []) have[x.item] = (have[x.item] ?? 0) + x.count;
  have[FREE] = (have[FREE] ?? 0) + 1;
  for (const s of plan.steps ?? []) {
    if (s.kind === 'workers') rules.push(rule(count('workers', '<', s.count), { kind: 'train', unit: 'Worker' }));
    else if (s.kind === 'train') rules.push(rule(count(s.item, '<', s.count), { kind: 'train', unit: s.item }));
    else if (s.kind === 'build') {
      have[s.item] = (have[s.item] ?? 0) + 1;
      rules.push(rule(count(s.item, '<', have[s.item]), { kind: 'build', building: s.item, site: SITE[s.item] ?? 'TowardCenter' }));
    } else if (s.kind === 'research') rules.push(rule({ kind: 'always' }, { kind: 'research', upgrade: s.item }, { once: true }));
  }
  const when = attack.trigger === 'time'
    ? { kind: 'minute', op: '>=', value: Math.max(1, Math.round((attack.atSecond ?? 60) / 60)) }
    : count('fighters', '>=', attack.army ?? 12);
  rules.push(rule(when, { kind: 'attack', who: 'fighters', target: attack.target === 'nearest' ? 'nearestEnemy' : 'enemyBase' }));
  const mix = (plan.forever ?? []).filter((id) => UNITS.includes(id));
  if (mix.length) rules.push(rule({ kind: 'always' }, mix.length === 1 ? { kind: 'train', unit: mix[0] } : { kind: 'trainMix', units: mix }));
  return { name: `${name}'s plan`.slice(0, MAX_NAME), switches: [], rules };
}

/** UTF-8 JSON as base64url (no padding), for a link. */
export function encodeProgram(program) {
  const bytes = new TextEncoder().encode(JSON.stringify(program));
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** encodeProgram backwards; throws on anything that isn't base64url JSON. */
export function decodeProgram(text) {
  const b64 = String(text).replace(/-/g, '+').replace(/_/g, '/');
  const bin = atob(b64 + '='.repeat((4 - (b64.length % 4)) % 4));
  const bytes = Uint8Array.from(bin, (c) => c.charCodeAt(0));
  return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
}

/** The "Take your bot home" link for a plan. */
export const takeHomeLink = (plan, name) => `/#/blocks/import?program=${encodeProgram(toBlockProgram(plan, name))}`;

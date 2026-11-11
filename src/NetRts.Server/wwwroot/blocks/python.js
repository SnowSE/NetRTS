// The Python view of a program: each rule as the code it stands for. The same code goes in the Python bot
// the edit page downloads (export.js), whose Bot class (export/bot.py.txt) has a method for every call here.
import { byDefault } from './program.js';

const pyStr = (s) => JSON.stringify(String(s));
function pyWho(who) {
  const w = who ?? byDefault('who');
  return w === 'fighters' ? 'bot.fighters()' : `bot.units(${pyStr(w)})`;
}
function pyCond(c) {
  switch (c?.kind) {
    case 'always': return 'True';
    case 'enemyNearBase': return 'bot.enemies_near_base()';
    case 'seeEnemy': return 'bot.can_see_enemy()';
    case 'count': {
      const op = c.op === '=' ? '==' : c.op;
      const left = c.what === 'grubs' ? 'bot.ore' : c.what === 'workers' ? 'bot.count("Worker")'
        : c.what === 'fighters' ? 'bot.count("fighters")' : `bot.count(${pyStr(c.what)})`;
      return `${left} ${op} ${c.value}`;
    }
    case 'minute': return `bot.minute() ${c.op === '=' ? '==' : c.op} ${c.value}`;
    case 'grubsLeft': return `bot.ore_left_near_base() ${c.op === '=' ? '==' : c.op} ${c.value}`;
    case 'switch': return `bot.switch(${pyStr(c.name)})`;
    case 'and': return `(${pyCond(c.a)} and ${pyCond(c.b)})`;
    case 'or': return `(${pyCond(c.a)} or ${pyCond(c.b)})`;
    case 'not': return `not ${pyCond(c.a)}`;
    default: return '?';
  }
}
function pyAction(a) {
  switch (a?.kind) {
    case 'train': return `bot.train(${pyStr(a.unit)})`;
    case 'trainMix': return `bot.train_mix([${(a.units || []).map(pyStr).join(', ')}])`;
    case 'build': return `bot.build(${pyStr(a.building)}, place=${pyStr(a.site ?? byDefault('site'))})`;
    case 'research': return `bot.research(${pyStr(a.upgrade)})`;
    case 'attack': return `bot.attack(${(a.target ?? byDefault('target')) === 'nearestEnemy' ? 'bot.nearest_enemy()' : 'bot.enemy_base()'}, ${pyWho(a.who)})`;
    case 'defend': return `bot.defend(${pyWho(a.who)})`;
    case 'retreat': return `bot.go_home(${pyWho(a.who)})`;
    case 'scout': return (a.target ?? byDefault('scoutTarget')) === 'explore' ? 'bot.explore()' : 'bot.scout()';
    case 'expand': return `bot.expand(${pyStr(a.building ?? byDefault('expandBuilding'))})`;
    default: return '?';
  }
}
/** The switches, as the Python bot's SWITCHES dictionary (all off to start). */
export const pySwitches = (program) => `{${program.switches.map((s) => `${pyStr(s)}: False`).join(', ')}}`;

export function pyRule(rule, i) {
  const body = rule.do?.length ? rule.do.map(pyAction) : ['pass'];
  let lines = rule.when?.kind === 'always' ? body : [`if ${pyCond(rule.when)}:`, ...body.map((l) => `    ${l}`)];
  lines = [`# Rule ${i + 1}${rule.enabled === false ? ' (off)' : ''}`, ...(rule.enabled === false ? lines.map((l) => `# ${l}`) : lines)];
  return lines.join('\n');
}

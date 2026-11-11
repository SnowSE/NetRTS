// A program's rules as C#, for the C# bot the edit page downloads (export.js). The template's Bot class
// (export/Bot.cs.txt) has a method for every call here; python.js is the same for Python.
import { byDefault } from './program.js';

const csStr = (s) => JSON.stringify(String(s));   // a JSON string is a valid C# string literal
const csOp = (op) => (op === '=' ? '==' : op);
const csWho = (who) => {
  const w = who ?? byDefault('who');
  return w === 'fighters' ? 'bot.Fighters()' : `bot.Units(${csStr(w)})`;
};

function csCond(c) {
  switch (c?.kind) {
    case 'always': return 'true';
    case 'enemyNearBase': return 'bot.EnemiesNearBase()';
    case 'seeEnemy': return 'bot.CanSeeEnemy()';
    case 'count': {
      const left = c.what === 'grubs' ? 'bot.Ore' : c.what === 'workers' ? 'bot.Count("Worker")' : `bot.Count(${csStr(c.what)})`;
      return `${left} ${csOp(c.op)} ${c.value}`;
    }
    case 'minute': return `bot.Minute ${csOp(c.op)} ${c.value}`;
    case 'grubsLeft': return `bot.OreLeftNearBase() ${csOp(c.op)} ${c.value}`;
    case 'switch': return `bot.Switch(${csStr(c.name)})`;
    case 'and': return `(${csCond(c.a)} && ${csCond(c.b)})`;
    case 'or': return `(${csCond(c.a)} || ${csCond(c.b)})`;
    case 'not': return `!(${csCond(c.a)})`;
    default: throw new Error(`No C# for the check ${c?.kind}`);
  }
}

function csAction(a) {
  switch (a?.kind) {
    case 'train': return `bot.Train(${csStr(a.unit)});`;
    case 'trainMix': return `bot.TrainMix([${(a.units || []).map(csStr).join(', ')}]);`;
    case 'build': return `bot.Build(${csStr(a.building)}, ${csStr(a.site ?? byDefault('site'))});`;
    case 'research': return `bot.Research(${csStr(a.upgrade)});`;
    case 'attack': return `bot.Attack(${(a.target ?? byDefault('target')) === 'nearestEnemy' ? 'bot.NearestEnemy()' : 'bot.EnemyBase()'}, ${csWho(a.who)});`;
    case 'defend': return `bot.Defend(${csWho(a.who)});`;
    case 'retreat': return `bot.GoHome(${csWho(a.who)});`;
    case 'scout': return (a.target ?? byDefault('scoutTarget')) === 'explore' ? 'bot.Explore();' : 'bot.Scout();';
    case 'expand': return `bot.Expand(${csStr(a.building ?? byDefault('expandBuilding'))});`;
    default: throw new Error(`No C# for the action ${a?.kind}`);
  }
}

/** One rule as C# statements (unindented lines). An "every tick" rule needs no if. */
export function csRule(rule, i) {
  const body = (rule.do ?? []).map(csAction);
  let lines = rule.when?.kind === 'always' ? body : [`if (${csCond(rule.when)})`, '{', ...body.map((l) => `    ${l}`), '}'];
  if (rule.enabled === false) lines = lines.map((l) => `// ${l}`);
  return [`// Rule ${i + 1}${rule.enabled === false ? ' (off)' : ''}`, ...lines].join('\n');
}

/** The switches, as entries of the C# bot's Switches dictionary (all off to start). */
export const csSwitches = (program) => program.switches.map((s) => `[${csStr(s)}] = false,`).join('\n');

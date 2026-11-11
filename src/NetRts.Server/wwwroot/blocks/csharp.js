// A program's rules as C#, for the C# bot the edit page downloads (export.js). The template's Bot class
// (export/Bot.cs.txt) has a method for every call here; python.js is the same for Python.
import { byDefault, repeatNote } from './program.js';

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
    case 'enemies': return `bot.Enemies(${csStr(c.what)}) ${csOp(c.op)} ${c.value}`;
    case 'underAttack': return `bot.UnderAttack(${csStr(c.what)})`;
    case 'hqHealth': return `bot.HqHealth() ${csOp(c.op)} ${c.value}`;
    case 'and': return `(${csCond(c.a)} && ${csCond(c.b)})`;
    case 'or': return `(${csCond(c.a)} || ${csCond(c.b)})`;
    case 'not': return `!(${csCond(c.a)})`;
    default: throw new Error(`No C# for the check ${c?.kind}`);
  }
}

/** An action as a C# call (no semicolon: a rule with a limit also notes whether it did anything). */
function csAction(a) {
  switch (a?.kind) {
    case 'train': return `bot.Train(${csStr(a.unit)})`;
    case 'trainMix': return `bot.TrainMix([${(a.units || []).map(csStr).join(', ')}])`;
    case 'build': return `bot.Build(${csStr(a.building)}, ${csStr(a.site ?? byDefault('site'))})`;
    case 'research': return `bot.Research(${csStr(a.upgrade)})`;
    case 'attack': return `bot.Attack(${(a.target ?? byDefault('target')) === 'nearestEnemy' ? 'bot.NearestEnemy()' : 'bot.EnemyBase()'}, ${csWho(a.who)}${a.percent && a.percent < 100 ? `, percent: ${a.percent}` : ''})`;
    case 'defend': return `bot.Defend(${csWho(a.who)})`;
    case 'retreat': return `bot.GoHome(${csWho(a.who)})`;
    case 'scout': return (a.target ?? byDefault('scoutTarget')) === 'explore' ? 'bot.Explore()' : 'bot.Scout()';
    case 'expand': return `bot.Expand(${csStr(a.building ?? byDefault('expandBuilding'))})`;
    case 'setSwitch': return `bot.SetSwitch(${csStr(a.name)}, ${a.on === false ? 'false' : 'true'})`;
    default: throw new Error(`No C# for the action ${a?.kind}`);
  }
}

/** One rule as C# statements (unindented lines). An "every tick" rule needs no if. */
export function csRule(rule, i) {
  const n = i + 1;
  const limit = rule.once ? 'once: true' : rule.everySeconds ? `every: ${rule.everySeconds}` : null;
  const cond = rule.when?.kind === 'always' ? null : csCond(rule.when);
  let lines;
  if (limit) {
    // Only once / at most every N seconds: bot.Ready says whether it may run, bot.Ran notes that it did.
    const body = ['var done = false;', ...(rule.do ?? []).map((a) => `done |= ${csAction(a)};`), `bot.Ran(${n}, done);`];
    lines = [`if (bot.Ready(${n}, ${limit})${cond ? ` && ${cond}` : ''})`, '{', ...body.map((l) => `    ${l}`), '}'];
  } else {
    const body = (rule.do ?? []).map((a) => `${csAction(a)};`);
    lines = cond ? [`if (${cond})`, '{', ...body.map((l) => `    ${l}`), '}'] : body;
  }
  if (rule.enabled === false) lines = lines.map((l) => `// ${l}`);
  return [`// Rule ${n}${repeatNote(rule)}${rule.enabled === false ? ' (off)' : ''}`, ...lines].join('\n');
}

/** The switches, as entries of the C# bot's Switches dictionary (all off to start). */
export const csSwitches = (program) => program.switches.map((s) => `[${csStr(s)}] = false,`).join('\n');

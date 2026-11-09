// #/blocks: Block Bot. Build a bot from rule cards, play it against the house bots or in a real match,
// and change it while the match runs. The server plays the rules (BlockBot in NetRts.Bots), so a match
// keeps going when the tab closes. Strategies live in this browser (localStorage, saved as you go) and
// download as .json files.
//
//   #/blocks                 the locker: your strategies, and where to play
//   #/blocks/edit/<id>       the rule-card editor
//   #/blocks/match/<id>      the cockpit: the match in a simple 3D view, the rules live, switches, Apply
import { api, html, raw, setHtml, displayName, timeAgo, outcomeText, slotVar } from './util.js';
import { GAME, SNOW, say, unitName, upgradeName, buildingName, buildingRole, nickname, ORE, badgerize } from './lore.js';

// ------------------------------------------------------------------ storage

const KEYS = {
  strategies: 'netrts.blocks.strategies',
  player: 'netrts.blocks.player',
  matches: 'netrts.blocks.matches',
  prefs: 'netrts.blocks.prefs',
};

function load(key, fallback) {
  try {
    const v = localStorage.getItem(key);
    return v ? JSON.parse(v) : fallback;
  } catch { return fallback; }
}
function store(key, value) {
  try { localStorage.setItem(key, JSON.stringify(value)); return true; } catch { return false; }
}

const strategies = () => load(KEYS.strategies, {});
const getStrategy = (id) => strategies()[id] ?? null;
function putStrategy(s) {
  const all = strategies();
  all[s.id] = { ...s, updatedAt: new Date().toISOString() };
  return store(KEYS.strategies, all);
}
function deleteStrategy(id) {
  const all = strategies();
  delete all[id];
  store(KEYS.strategies, all);
}
const prefs = () => load(KEYS.prefs, {});
const setPref = (k, v) => store(KEYS.prefs, { ...prefs(), [k]: v });
const matchRecords = () => load(KEYS.matches, {});
function recordMatch(matchId, strategyId) {
  const all = matchRecords();
  all[matchId] = { strategyId, at: new Date().toISOString() };
  // Keep the newest 30.
  const keep = Object.entries(all).sort((a, b) => (b[1].at > a[1].at ? 1 : -1)).slice(0, 30);
  store(KEYS.matches, Object.fromEntries(keep));
}

const newId = () => (crypto.randomUUID ? crypto.randomUUID() : `s${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`);
const clone = (x) => JSON.parse(JSON.stringify(x));

function newStrategy(program) {
  const s = { id: newId(), program: clone(program), createdAt: new Date().toISOString() };
  putStrategy(s);
  return s;
}

const player = () => load(KEYS.player, null);
function authed(path, options = {}) {
  const p = player();
  return api(path, { ...options, headers: { ...(options.headers || {}), authorization: `Bearer ${p?.apiKey ?? ''}` } });
}

// ------------------------------------------------------------------ words

const UNITS = ['Worker', 'Soldier', 'Archer', 'Scout', 'Trebuchet'];
const FIGHTERS = UNITS.slice(1);
const BUILDINGS = ['Barracks', 'TechLab', 'GuardTower', 'ResourceDepot', 'SiegeWorkshop', 'CommandCenter'];
const UPGRADES = ['Weapons1', 'Weapons2', 'Armor1', 'Armor2', 'Mobility1', 'Mobility2', 'Harvesting1', 'Harvesting2', 'SiegeEngineering'];
const OPS = [['<', '<'], ['<=', '≤'], ['>', '>'], ['>=', '≥'], ['=', '=']];

function unitWord(t, many = false) {
  const n = unitName(t);
  if (n === 'Snow Mobile') return many ? 'Snow Mobiles' : 'Snow Mobile';
  return many ? `${n.toLowerCase()}s` : n.toLowerCase();
}
/** Building names without a leading "The" ("build a Bell Tower", not "build a The Bell Tower"). */
const bWord = (t) => buildingName(t).replace(/^The /, '');

// The balance numbers, from GET /api/v1/rules (GameRules on the server), so what a class does is
// never typed twice. Fetched once; anything drawn before it arrives draws again when it does.
let RULES = null;
const rulesReady = api('/api/v1/rules').then((r) => { RULES = r; return r; }).catch(() => null);

/**
 * What an upgrade ("class") does, in the player's words: `short` for its menu entry ("+2 damage"),
 * `detail` and `meta` for the line under the rule. Null until the rules are in.
 */
function upgradeInfo(type) {
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
const THEME = SNOW ? 'snow' : 'classic';
const WHAT = () => [
  ['fighters', 'fighters'], ['workers', 'workers'], ['grubs', ORE],
  ...FIGHTERS.map((t) => [t, unitWord(t, true)]),
  ...BUILDINGS.map((b) => [b, bWord(b)]),
];
const WHO = () => [['fighters', 'all fighters'], ...FIGHTERS.map((t) => [t, `my ${unitWord(t, true)}`])];
const SITES = () => [['TowardCenter', 'toward the middle'], ['NearOre', `near the ${ORE}`], ['TowardEnemy', 'toward the enemy']];
const TARGETS = [['enemyBase', 'the enemy base'], ['nearestEnemy', 'the nearest enemy']];

// ------------------------------------------------------------------ blocks

const C = {
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
const A = {
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
const R = (when, ...actions) => ({ enabled: true, when, do: actions });

const STARTERS = {
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

const at = (obj, path) => path.split('.').reduce((o, k) => o?.[k], obj);
function setAt(obj, path, value) {
  const keys = path.split('.');
  const last = keys.pop();
  const parent = keys.reduce((o, k) => o[k], obj);
  parent[last] = value;
}

/** For comparing a program here with the one the server runs: no nulls, same key order. */
function canon(x) {
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
const sameProgram = (a, b) => JSON.stringify(canon(a)) === JSON.stringify(canon(b));

// ------------------------------------------------------------------ Python view

const pyStr = (s) => JSON.stringify(String(s));
function pyWho(who) {
  return !who || who === 'fighters' ? 'bot.fighters()' : `bot.units(${pyStr(who)})`;
}
function pyCond(c) {
  switch (c?.kind) {
    case 'always': return 'True';
    case 'enemyNearBase': return 'bot.enemies_near_base()';
    case 'seeEnemy': return 'bot.can_see_enemy()';
    case 'count': {
      const op = c.op === '=' ? '==' : c.op;
      const left = c.what === 'grubs' ? 'bot.ore' : c.what === 'workers' ? 'bot.count("Worker")'
        : c.what === 'fighters' ? 'len(bot.fighters())' : `bot.count(${pyStr(c.what)})`;
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
    case 'build': return `bot.build(${pyStr(a.building)}, place=${pyStr(a.site ?? 'TowardCenter')})`;
    case 'research': return `bot.research(${pyStr(a.upgrade)})`;
    case 'attack': return `bot.attack(${a.target === 'nearestEnemy' ? 'bot.nearest_enemy()' : 'bot.enemy_base()'}, ${pyWho(a.who)})`;
    case 'defend': return `bot.defend(${pyWho(a.who)})`;
    case 'retreat': return `bot.go_home(${pyWho(a.who)})`;
    case 'scout': return a.target === 'explore' ? 'bot.explore()' : 'bot.scout()';
    case 'expand': return `bot.expand(${pyStr(a.building ?? 'ResourceDepot')})`;
    default: return '?';
  }
}
function pyRule(rule, i) {
  const body = rule.do?.length ? rule.do.map(pyAction) : ['pass'];
  let lines = rule.when?.kind === 'always' ? body : [`if ${pyCond(rule.when)}:`, ...body.map((l) => `    ${l}`)];
  lines = [`# Rule ${i + 1}${rule.enabled === false ? ' (off)' : ''}`, ...(rule.enabled === false ? lines.map((l) => `# ${l}`) : lines)];
  return lines.join('\n');
}

// ------------------------------------------------------------------ the rule-card editor

/**
 * Rule cards: each rule is a sentence ("When [fighters ≥ 8] do [attack the enemy base with all fighters]")
 * whose pieces are dropdowns. Clicking a piece's ▾ opens the picker beside the cards to swap it for
 * another kind. Every change calls onChange with a fresh copy of the program.
 */
class RuleEditor {
  constructor(root, { program, onChange, onHover, onSave, compact = false, sideTop = null }) {
    this.root = root;
    this.sideTop = sideTop;   // the edit page's buttons, at the top of the side panel so they stay in view
    this.compact = compact;   // a narrow column: no side panel; the picker opens under its rule
    // With onSave (the cockpit), edits go into the running match one rule at a time: changing a rule
    // starts a session on it (a yellow border, Save and Cancel) and locks the others until it ends.
    // One-click changes (on/off, moving, deleting) are saved straight away. Without it, every change
    // goes to onChange as it happens.
    this.onSave = onSave;
    this.session = null;   // { index, snapshot (the program before the edit), inserted }
    this.saving = false;
    this.saveError = '';
    this.program = clone(program);
    this.onChange = onChange;
    this.onHover = onHover;
    this.picker = null;    // { type: 'cond' | 'action', path, mode: 'replace' | 'add' }
    this.status = null;    // the running bot's rule statuses (cockpit)
    this.statusNote = '';
    this.code = !!prefs().code;
    root.addEventListener('click', (e) => this.click(e));
    root.addEventListener('change', (e) => this.change(e));
    root.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && this.session && !this.saving) { e.preventDefault(); this.cancel(); return; }
      // A step's handle moves it with the arrow keys too, for keyboards (and where dragging doesn't work).
      const grip = e.target.closest?.('[data-drag-step]');
      if (grip && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) {
        e.preventDefault();
        const path = grip.dataset.dragStep;
        const j = Number(path.split('.').pop());
        this.moveStep(path, j + (e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 1), { focus: true });
      }
    });
    // Dragging a step by its handle, within its rule (rules move with their Move Up and Move Down
    // buttons): the step follows the pointer, the original fades, and a yellow line shows where it
    // will land, before or after a step of the same rule. It drops exactly there.
    const clearDrop = () => this.root.querySelectorAll('.drop-before, .drop-after, .dragging')
      .forEach((el) => el.classList.remove('drop-before', 'drop-after', 'dragging'));
    const markDrop = (el, before) => {
      this.root.querySelectorAll('.drop-before, .drop-after').forEach((x) => { if (x !== el) x.classList.remove('drop-before', 'drop-after'); });
      el?.classList.toggle('drop-before', before);
      el?.classList.toggle('drop-after', !before);
    };
    const stepDrop = (e) => {
      const el = e.target.closest?.('[data-step]');
      if (!this.dragStep || !el) return null;
      const list = this.dragStep.replace(/\.\d+$/, '');
      if (el.dataset.step.replace(/\.\d+$/, '') !== list) return null;
      const r = el.getBoundingClientRect();
      const before = e.clientX < r.left + r.width / 2;
      const from = Number(this.dragStep.split('.').pop());
      const j = Number(el.dataset.step.split('.').pop());
      let to = before ? j : j + 1;
      if (from < to) to -= 1;
      return { el, before, to: to === from ? null : to };
    };
    root.addEventListener('dragstart', (e) => {
      const stepGrip = e.target.closest('[data-drag-step]');
      if (!stepGrip) return;
      const rule = Number(stepGrip.dataset.dragStep.split('.')[1]);
      if (this.saving || (this.session && this.session.index !== rule)) { e.preventDefault(); return; }
      this.dragStep = stepGrip.dataset.dragStep;
      const el = stepGrip.closest('.bb-step') ?? stepGrip;
      const r = el.getBoundingClientRect();
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', this.dragStep);
      e.dataTransfer.setDragImage(el, Math.max(0, e.clientX - r.left), Math.max(0, e.clientY - r.top));
      requestAnimationFrame(() => el.classList.add('dragging'));   // after the browser has taken its picture
    });
    root.addEventListener('dragover', (e) => {
      if (!this.dragStep) return;
      e.preventDefault();   // anywhere in the editor; a drop where nothing would move does nothing
      const d = stepDrop(e);
      if (d?.to != null) markDrop(d.el, d.before);
      else markDrop(null);
    });
    root.addEventListener('drop', (e) => {
      if (!this.dragStep) return;
      e.preventDefault();
      const d = stepDrop(e);
      const from = this.dragStep;
      this.dragStep = null;
      clearDrop();
      if (d?.to != null) this.moveStep(from, d.to);
    });
    root.addEventListener('dragend', () => { this.dragStep = null; clearDrop(); });
    const hover = (e) => {
      const card = e.target.closest?.('.bb-rule');
      this.onHover?.(card ? Number(card.dataset.rule) : null);
    };
    root.addEventListener('mouseover', hover);
    root.addEventListener('focusin', hover);
    root.addEventListener('mouseleave', () => this.onHover?.(null));
    this.render();
  }

  setProgram(program) { this.program = clone(program); this.session = null; this.picker = null; this.render(); }
  get busy() { return !!this.session; }
  setStatus(rules, note = '') {
    const changed = JSON.stringify(rules) !== JSON.stringify(this.status) || note !== this.statusNote;
    this.status = rules;
    this.statusNote = note;
    if (changed) this.renderStatus();
  }
  setCode(on) { this.code = on; setPref('code', on); this.render(); }

  commit(mutate, { keepPicker = false } = {}) {
    const p = clone(this.program);
    mutate(p);
    this.program = p;
    if (!keepPicker) this.picker = null;
    this.render();
    if (!this.onSave) this.onChange?.(clone(p));
  }

  /** A change inside one rule. In save mode it opens (or continues) that rule's editing session. */
  edit(index, mutate, opts) {
    if (this.onSave) {
      if (this.session && this.session.index !== index) return;   // another rule is being edited
      this.session ??= { index, snapshot: clone(this.program), inserted: false };
      this.saveError = '';
    }
    this.commit(mutate, opts);
  }

  /** A one-click change to the list (on/off, move, delete). In save mode it's saved straight away. */
  async structural(mutate) {
    if (this.session || this.saving) return;
    const before = clone(this.program);
    this.commit(mutate);
    if (!this.onSave) return;
    this.saving = true;
    this.saveError = '';
    try { await this.onSave(clone(this.program)); } catch (e) {
      this.program = before;
      this.saveError = e.message;
    }
    this.saving = false;
    this.render();
  }

  async save() {
    if (!this.session || this.saving) return;
    this.saving = true;
    this.saveError = '';
    this.render();
    try {
      await this.onSave(clone(this.program));
      this.session = null;
      this.picker = null;
    } catch (e) { this.saveError = e.message; }
    this.saving = false;
    this.render();
  }

  cancel() {
    if (!this.session) return;
    this.program = this.session.snapshot;
    this.session = null;
    this.picker = null;
    this.saveError = '';
    this.render();
  }

  /** A step (action) to a new place in its rule. In save mode it's an edit of that rule: Save or Cancel. */
  moveStep(path, to, { focus = false } = {}) {
    const list = path.replace(/\.\d+$/, '');
    const from = Number(path.split('.').pop());
    const n = at(this.program, list)?.length ?? 0;
    if (to < 0 || to >= n || to === from) return;
    this.edit(Number(path.split('.')[1]), (p) => {
      const steps = at(p, list);
      const [a] = steps.splice(from, 1);
      steps.splice(to, 0, a);
    });
    if (focus) this.root.querySelector(`[data-k="${CSS.escape(`stepgrip|${list}.${to}`)}"]`)?.focus();
  }

  move(from, to) {
    if (from === to || from < 0 || to < 0) return;
    this.structural((p) => {
      const [r] = p.rules.splice(from, 1);
      p.rules.splice(to, 0, r);
    });
  }

  click(e) {
    const b = e.target.closest('button[data-act]');
    if (!b || !this.root.contains(b)) return;
    const { act, path } = b.dataset;
    const i = Number(b.dataset.i);
    const ruleOf = (p) => Number(String(p).split('.')[1]);
    switch (act) {
      case 'save-rule': return this.save();
      case 'cancel-rule': return this.cancel();
      case 'pick-cond': this.picker = { type: 'cond', path }; this.render(); this.focusPicker(); return;
      case 'pick-action': this.picker = { type: 'action', path, mode: 'replace' }; this.render(); this.focusPicker(); return;
      case 'add-action': this.picker = { type: 'action', path, mode: 'add' }; this.render(); this.focusPicker(); return;
      case 'insert-action': this.picker = { type: 'action', path, mode: 'insert', at: Number(b.dataset.j) }; this.render(); this.focusPicker(); return;
      case 'close-picker': this.picker = null; this.render(); return;
      case 'choose': return this.choose(b.dataset);
      case 'new-switch': {
        const input = this.root.querySelector('#bb-new-switch');
        const name = (input?.value || '').trim().slice(0, 24);
        if (!name) { input?.focus(); return; }
        const path0 = this.picker?.path;
        const change = (p) => {
          if (!p.switches.some((s) => s.toLowerCase() === name.toLowerCase())) p.switches.push(name);
          if (path0) setAt(p, path0, C.switch(name));
        };
        if (path0) this.edit(ruleOf(path0), change); else this.commit(change);
        return;
      }
      case 'del-switch': return this.commit((p) => {
        p.switches = p.switches.filter((s) => s !== b.dataset.name);
      }, { keepPicker: true });
      case 'del-action': return this.edit(ruleOf(path), (p) => { at(p, path.replace(/\.\d+$/, '')).splice(Number(path.split('.').pop()), 1); });
      case 'unwrap': return this.edit(ruleOf(path), (p) => setAt(p, path, at(p, path).a));
      case 'mix-add': return this.edit(ruleOf(path), (p) => at(p, path).units.push('Soldier'));
      case 'mix-del': return this.edit(ruleOf(path), (p) => { const u = at(p, path).units; if (u.length > 1) u.splice(i, 1); });
      case 'rule-up':
      case 'rule-down': {
        // Focus follows the rule that moved, so pressing again keeps moving it.
        const to = act === 'rule-up' ? i - 1 : Math.min(i + 1, this.program.rules.length - 1);
        this.move(i, to);
        const btn = this.root.querySelector(`[data-k="${act === 'rule-up' ? 'up' : 'down'}|${to}"]`);
        (btn && !btn.disabled ? btn : this.root.querySelector(`[data-k="${act === 'rule-up' ? 'down' : 'up'}|${to}"]`))?.focus();
        return;
      }
      case 'rule-toggle': return this.structural((p) => { p.rules[i].enabled = p.rules[i].enabled === false; });
      case 'rule-del': return this.structural((p) => { p.rules.splice(i, 1); });
      case 'add-rule':
      case 'insert-rule': {
        // The new rule goes where it was asked for, and its check's picker opens so it's ready to fill in.
        // In save mode it starts out being edited: Save sends it in, Cancel takes it away again.
        if (this.session) return;
        const at = act === 'insert-rule' ? i : this.program.rules.length;
        if (this.onSave) this.session = { index: at, snapshot: clone(this.program), inserted: true };
        this.commit((p) => { p.rules.splice(at, 0, R(C.count('fighters', '>=', 8), A.attack())); });
        this.picker = { type: 'cond', path: `rules.${at}.when` };
        this.render();
        this.root.querySelector(`.bb-rule[data-rule="${at}"]`)?.scrollIntoView({ block: 'nearest' });
        this.focusPicker();
        return;
      }
      default:
    }
  }

  choose(d) {
    const pk = this.picker;
    if (!pk) return;
    this.edit(Number(pk.path.split('.')[1]), (p) => {
      if (pk.type === 'cond') {
        const current = at(p, pk.path);
        let node;
        switch (d.kind) {
          case 'and': case 'or': node = C[d.kind](current, C.count('fighters', '>=', 8)); break;
          case 'not': node = C.not(current); break;
          case 'count': node = C.count(d.what, d.op, Number(d.value)); break;
          case 'switch': node = C.switch(d.name); break;
          default: node = C[d.kind]();
        }
        setAt(p, pk.path, node);
      } else {
        const node = d.kind === 'train' ? A.train(d.unit)
          : d.kind === 'scout' ? A.scout(d.target)
          : d.kind === 'build' ? A.build(d.building, d.building === 'ResourceDepot' ? 'NearOre' : d.building === 'GuardTower' ? 'TowardEnemy' : 'TowardCenter')
            : A[d.kind]();
        if (pk.mode === 'add') at(p, pk.path).do.push(node);
        else if (pk.mode === 'insert') at(p, pk.path).do.splice(pk.at, 0, node);
        else setAt(p, pk.path, node);
      }
    });
  }

  change(e) {
    const el = e.target;
    const { path, field } = el.dataset;
    if (!path || !field) return;
    this.edit(Number(path.split('.')[1]), (p) => {
      const node = at(p, path);
      if (field === 'units') node.units[Number(el.dataset.i)] = el.value;
      else if (field === 'value') node.value = Math.max(0, Math.min(100000, Math.round(Number(el.value) || 0)));
      else node[field] = el.value;
    }, { keepPicker: true });
  }

  focusPicker() { this.root.querySelector('.bb-picker [data-act="choose"]')?.focus(); }

  // ---------------------------------------------------------------- rendering

  render() {
    if (!RULES && !this.waitingForRules) {
      this.waitingForRules = true;
      rulesReady.then((r) => { if (r && this.root.isConnected) this.render(); });
    }
    const focused = document.activeElement?.dataset?.k;
    const p = this.program;
    const pickerUnder = (i) => this.picker
      && (this.picker.path === `rules.${i}` || this.picker.path.startsWith(`rules.${i}.`))
      ? html`<aside class="bb-picker inline" aria-label="Pick a block">${this.pickerHtml()}</aside>` : '';
    setHtml(this.root, html`
      <div class="bb-editor${this.code ? ' with-code' : ''}${this.compact ? ' compact' : ''}${this.session ? ' locked' : ''}">
        <div class="bb-rules">
          ${this.saveError && !this.session ? html`<p class="bb-error" role="alert">${this.saveError}</p>` : ''}
          ${p.rules.length ? '' : html`<p class="bb-empty">No rules yet. Add one, or start from one of the starter strategies in the locker.</p>`}
          ${p.rules.map((r, i) => html`<button type="button" class="bb-insert" data-act="insert-rule" data-i="${i}" data-k="insert|${i}" aria-label="Add a rule before rule ${i + 1}"><span>+ rule</span></button>${this.ruleHtml(r, i)}${pickerUnder(i)}`)}
          <button type="button" class="bb-add-rule" data-act="add-rule" data-k="add-rule">+ Add a rule</button>
          <p class="small muted">Rules are checked top to bottom, every tick. Higher rules get first pick of ${ORE} and fighters.
            Two things happen on their own: idle workers dig ${ORE}, and half-built buildings get finished.</p>
        </div>
        ${this.compact ? '' : html`<aside class="bb-picker bb-help" aria-label="How rules work">${this.sideTop ? this.sideTop(this) : ''}${this.helpHtml()}</aside>`}
      </div>`);
    if (focused) this.root.querySelector(`[data-k="${CSS.escape(focused)}"]`)?.focus();
    this.renderStatus();
  }

  renderStatus() {
    this.root.querySelectorAll('.bb-rule').forEach((card) => {
      const i = Number(card.dataset.rule);
      // The statuses are for the running program: around a rule being added, they're one off.
      const ins = this.session?.inserted ? this.session.index : null;
      const s = ins == null ? this.status?.[i] : i === ins ? null : this.status?.[i > ins ? i - 1 : i];
      const box = card.querySelector('.bb-live');
      if (!box) return;
      card.dataset.state = s?.state ?? '';
      if (!s) { box.hidden = true; return; }
      box.hidden = false;
      const bar = s.state === 'idle' && s.progress != null ? html`<span class="bb-bar" role="img" aria-label="${s.progress}% of the way"><span style="width:${s.progress}%"></span></span>` : '';
      setHtml(box, html`<span class="bb-dot" aria-hidden="true"></span><span class="bb-why">${s.why}${this.statusNote ? html` <span class="muted">${this.statusNote}</span>` : ''}</span>${bar}`);
    });
  }

  ruleHtml(r, i) {
    const n = this.program.rules.length;
    const off = r.enabled === false;
    // While a rule is being edited, the one-click buttons wait for its Save or Cancel.
    const wait = this.session || this.saving ? 'disabled title="Save or cancel your change first"' : '';
    return html`
      <div class="bb-row">
        <div class="bb-rule-line">
        <div class="bb-move" role="group" aria-label="Move rule ${i + 1}">
          <button type="button" class="bb-move-btn" data-act="rule-up" data-i="${i}" data-k="up|${i}" aria-label="Move rule ${i + 1} up" ${i === 0 ? 'disabled' : raw(wait)}><span aria-hidden="true">↑</span> Move Up</button>
          <button type="button" class="bb-move-btn" data-act="rule-down" data-i="${i}" data-k="down|${i}" aria-label="Move rule ${i + 1} down" ${i === n - 1 ? 'disabled' : raw(wait)}>Move Down <span aria-hidden="true">↓</span></button>
        </div>
        <article class="bb-rule${off ? ' off' : ''}${this.session?.index === i ? ' editing-rule' : ''}" data-rule="${i}">
          <div class="bb-rule-main">
            <span class="bb-num" aria-hidden="true">${i + 1}</span>
            <div class="bb-sentence">
              ${r.when?.kind === 'always' ? '' : html`<span class="bb-word">When</span>`}
              ${this.condHtml(r.when, `rules.${i}.when`)}
              <span class="bb-word">${r.when?.kind === 'always' ? '' : 'do'}</span>
              ${(r.do || []).map((a, j) => {
                const path = `rules.${i}.do.${j}`;
                // Between two steps, a + adds one there; with two or more, each has a handle to drag it.
                const insert = j > 0 ? html`<button type="button" class="bb-step-insert" data-act="insert-action" data-path="rules.${i}" data-j="${j}" data-k="ins-step|${i}|${j}" aria-label="Add a step between steps ${j} and ${j + 1} of rule ${i + 1}" title="Add a step here">+</button>` : '';
                const grip = r.do.length > 1 ? html`<button type="button" class="bb-step-grip" draggable="true" data-drag-step="${path}" data-k="stepgrip|${path}" aria-label="Move step ${j + 1} of rule ${i + 1}: drag it, or press the left or right arrow" title="Drag to move (or ← →)">${raw(ICON.grip)}</button>` : '';
                return html`${insert}<span class="bb-step" data-step="${path}">${grip}${this.actionHtml(a, path)}</span>`;
              })}
              <button type="button" class="bb-plus" data-act="add-action" data-path="rules.${i}" data-k="add-action|${i}" aria-label="Add something for rule ${i + 1} to do">+ do</button>
            </div>
            <div class="bb-tools">
              <button type="button" class="bb-switch-toggle" role="switch" data-act="rule-toggle" data-i="${i}" data-k="toggle|${i}" aria-checked="${String(!off)}" aria-label="Rule ${i + 1}" title="${off ? 'Off: this rule is skipped. Click to turn it on.' : 'On: click to turn this rule off.'}" ${raw(wait)}><span class="bb-switch-track" aria-hidden="true"><span class="bb-switch-knob"></span></span><span class="bb-switch-text">${off ? 'Off' : 'On'}</span></button>
              <button type="button" class="bb-icon" data-act="rule-del" data-i="${i}" data-k="del|${i}" aria-label="Delete rule ${i + 1}" ${raw(wait)}>×</button>
            </div>
          </div>
          ${(r.do || []).filter((a) => a.kind === 'research').map((a) => {
            const info = upgradeInfo(a.upgrade);
            return info ? html`<p class="bb-note"><b>${upgradeName(a.upgrade)}</b>: ${info.detail}. <span class="muted">${info.meta}</span></p>` : '';
          })}
          <p class="bb-live" hidden></p>
          ${this.session?.index === i ? html`
            <div class="bb-save-row">
              <button type="button" class="btn start small-btn" data-act="save-rule" data-k="save|${i}" ${this.saving ? 'disabled' : ''}>${this.saving ? 'Saving…' : 'Save'}</button>
              <button type="button" class="btn ghost small-btn" data-act="cancel-rule" data-k="cancel|${i}" ${this.saving ? 'disabled' : ''}>Cancel</button>
              <span class="small muted">${this.session.inserted ? 'New rule: it plays once you save it.' : 'Your change plays once you save it.'}</span>
              ${this.saveError ? html`<span class="bb-error" role="alert">${this.saveError}</span>` : ''}
            </div>` : ''}
        </article>
        </div>
        ${this.code ? html`<pre class="bb-py" aria-label="Rule ${i + 1} in Python">${pyRule(r, i)}</pre>` : ''}
      </div>`;
  }

  editing(path) { return this.picker?.path === path ? ' editing' : ''; }

  select(path, field, value, options, label, extra = {}) {
    const k = `${path}|${field}|${extra.i ?? ''}`;
    return html`<select class="bb-select" data-path="${path}" data-field="${field}" data-k="${k}" ${extra.i != null ? html`data-i="${extra.i}"` : ''} aria-label="${label}">
      ${options.map(([v, t]) => html`<option value="${v}" ${String(v) === String(value) ? 'selected' : ''}>${t}</option>`)}
    </select>`;
  }

  condHtml(c, path) {
    const kindBtn = (text, aria = 'Change this check') =>
      html`<button type="button" class="bb-kind" data-act="pick-cond" data-path="${path}" data-k="pick|${path}" aria-label="${aria}">${text}<span aria-hidden="true"> ▾</span></button>`;
    switch (c?.kind) {
      case 'and': case 'or':
        return html`<span class="bb-group${this.editing(path)}">
          ${this.condHtml(c.a, `${path}.a`)}
          ${this.select(path, 'kind', c.kind, [['and', 'and'], ['or', 'or']], 'and or or')}
          ${this.condHtml(c.b, `${path}.b`)}
          <button type="button" class="bb-icon small" data-act="unwrap" data-path="${path}" data-k="unwrap|${path}" aria-label="Remove the second check">×</button>
        </span>`;
      case 'not':
        return html`<span class="bb-group${this.editing(path)}"><button type="button" class="bb-icon small" data-act="unwrap" data-path="${path}" data-k="unwrap|${path}" aria-label="Remove not">not ×</button>${this.condHtml(c.a, `${path}.a`)}</span>`;
      case 'count':
        return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('', 'Change this check')}
          ${this.select(path, 'what', c.what, WHAT(), 'What to count')}
          ${this.select(path, 'op', c.op, OPS, 'Comparison')}
          <input class="bb-num-in" type="number" min="0" max="100000" inputmode="numeric" value="${c.value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="Number">
        </span>`;
      case 'grubsLeft':
        return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn(`${ORE} left near my base`)}
          ${this.select(path, 'op', c.op, OPS, 'Comparison')}
          <input class="bb-num-in" type="number" min="0" max="100000" inputmode="numeric" value="${c.value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="Number">
        </span>`;
      case 'minute':
        return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('minute')}
          ${this.select(path, 'op', c.op, OPS, 'Comparison')}
          <input class="bb-num-in" type="number" min="0" max="999" inputmode="numeric" value="${c.value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="Minute">
        </span>`;
      case 'switch':
        return html`<span class="bb-chip switch${this.editing(path)}">${kindBtn('switch')}
          ${this.select(path, 'name', c.name, (this.program.switches.length ? this.program.switches : [c.name]).map((s) => [s, s]), 'Which switch')}
          <span>is on</span></span>`;
      case 'enemyNearBase': return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('an enemy is near my base')}</span>`;
      case 'seeEnemy': return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('I can see an enemy')}</span>`;
      default: return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('Every tick')}</span>`;
    }
  }

  actionHtml(a, path) {
    const kindBtn = (text) => html`<button type="button" class="bb-kind" data-act="pick-action" data-path="${path}" data-k="pick|${path}" aria-label="Change this action: ${text}">${text}<span aria-hidden="true"> ▾</span></button>`;
    const del = html`<button type="button" class="bb-icon small" data-act="del-action" data-path="${path}" data-k="del|${path}" aria-label="Remove this action">×</button>`;
    const cls = `bb-chip act${this.editing(path)}`;
    switch (a.kind) {
      case 'train':
        return html`<span class="${cls}">${kindBtn('train a')}${this.select(path, 'unit', a.unit, UNITS.map((t) => [t, unitWord(t)]), 'Which unit')}${del}</span>`;
      case 'trainMix':
        return html`<span class="${cls}">${kindBtn('train in turn')}
          ${(a.units || []).map((u, i) => html`${this.select(path, 'units', u, FIGHTERS.map((t) => [t, unitWord(t)]), `Unit ${i + 1} of the mix`, { i })}${a.units.length > 1 ? html`<button type="button" class="bb-icon small" data-act="mix-del" data-path="${path}" data-i="${i}" data-k="mixdel|${path}|${i}" aria-label="Remove unit ${i + 1} from the mix">−</button>` : ''}`)}
          ${a.units.length < 8 ? html`<button type="button" class="bb-icon small" data-act="mix-add" data-path="${path}" data-k="mixadd|${path}" aria-label="Add a unit to the mix">+</button>` : ''}${del}</span>`;
      case 'build':
        return html`<span class="${cls} build">${kindBtn('build a')}${this.select(path, 'building', a.building, BUILDINGS.map((b) => [b, bWord(b)]), 'Which building')}${this.select(path, 'site', a.site ?? 'TowardCenter', SITES(), 'Where')}${del}</span>`;
      case 'research':
        return html`<span class="${cls} build">${kindBtn(say('research', 'take the class'))}${this.select(path, 'upgrade', a.upgrade, UPGRADES.map((u) => { const info = upgradeInfo(u); return [u, info ? `${upgradeName(u)}: ${info.short}` : upgradeName(u)]; }), 'Which upgrade')}${del}</span>`;
      case 'attack':
        return html`<span class="${cls}">${kindBtn('attack')}${this.select(path, 'target', a.target ?? 'enemyBase', TARGETS, 'Attack what')}<span>with</span>${this.select(path, 'who', a.who ?? 'fighters', WHO(), 'Who attacks')}${del}</span>`;
      case 'defend':
        return html`<span class="${cls}">${kindBtn('defend with')}${this.select(path, 'who', a.who ?? 'fighters', WHO(), 'Who defends')}${del}</span>`;
      case 'retreat':
        return html`<span class="${cls}">${kindBtn('bring home')}${this.select(path, 'who', a.who ?? 'fighters', WHO(), 'Who comes home')}${del}</span>`;
      case 'scout':
        return html`<span class="${cls}">${kindBtn('send a scout to')}${this.select(path, 'target', a.target ?? 'enemyBase', [['enemyBase', 'find the enemy base'], ['explore', `explore (find more ${ORE})`]], 'Where the scout goes')}${del}</span>`;
      case 'expand':
        return html`<span class="${cls} build">${kindBtn(`expand to new ${ORE} with a`)}${this.select(path, 'building', a.building ?? 'ResourceDepot', [['ResourceDepot', bWord('ResourceDepot')], ['CommandCenter', bWord('CommandCenter')]], 'What to build there')}${del}</span>`;
      default:
        return html`<span class="${cls}">${kindBtn(a.kind)}${del}</span>`;
    }
  }

  pickerHtml() {
    const pk = this.picker;
    if (!pk) return '';
    return this.pickerBody(pk);
  }

  helpHtml() {
    return html`
        <h2>How rules work</h2>
        <p>Each rule says <b>when</b> and <b>what to do</b>. Every tick (about once a second) your bot checks the rules from the top.</p>
        <p>Click a <b>▾</b> to swap a piece for a different one. Change numbers and choices right in the sentence.</p>
        <p>Add a <b>switch</b> to flip during a match: a rule like "When the <i>Attack!</i> switch is on, attack" lets you send your army in whenever you want.</p>
        <p>To add a step between two others, use the <b>+</b> between them. Drag a step by its <b>⠿</b> handle to change the order (or focus the handle and press ← or →).</p>
        ${this.program.switches.length ? html`<h3>Your switches</h3><ul class="bb-switch-list">${this.program.switches.map((s) => html`<li>${s} <button type="button" class="bb-icon small" data-act="del-switch" data-name="${s}" aria-label="Delete the ${s} switch">×</button></li>`)}</ul>` : ''}`;
  }

  pickerBody(pk) {
    const opt = (label, data) => html`<button type="button" class="bb-opt" data-act="choose" ${Object.entries(data).map(([k, v]) => html`data-${k}="${v}" `)}>${label}</button>`;
    const group = (name, items) => html`<div class="bb-opt-group"><h3>${name}</h3><div>${items}</div></div>`;
    const head = (title) => html`<div class="bb-picker-head"><h2>${title}</h2><button type="button" class="bb-icon" data-act="close-picker" aria-label="Close the picker">×</button></div>`;
    if (pk.type === 'cond') {
      const current = at(this.program, pk.path);
      return html`${head('Pick a check')}
        ${group('My stuff', [
          opt('fighters ≥ 8', { kind: 'count', what: 'fighters', op: '>=', value: 8 }),
          opt('workers < 12', { kind: 'count', what: 'workers', op: '<', value: 12 }),
          opt(`${ORE} ≥ 150`, { kind: 'count', what: 'grubs', op: '>=', value: 150 }),
          opt(`fewer than 2 ${bWord('Barracks')}`, { kind: 'count', what: 'Barracks', op: '<', value: 2 }),
          opt(`${ORE} left near my base ≤ 300`, { kind: 'grubsLeft' }),
        ])}
        ${group('The enemy', [opt('an enemy is near my base', { kind: 'enemyNearBase' }), opt('I can see an enemy', { kind: 'seeEnemy' })])}
        ${group('Time', [opt('minute ≥ 3', { kind: 'minute' }), opt('every tick', { kind: 'always' })])}
        ${group('Switches', html`
          ${this.program.switches.map((s) => opt(`${s} is on`, { kind: 'switch', name: s }))}
          <span class="bb-new-switch"><label class="visually-hidden" for="bb-new-switch">New switch name</label><input id="bb-new-switch" type="text" maxlength="24" placeholder="New switch, e.g. Attack!"><button type="button" class="bb-opt" data-act="new-switch">Add</button></span>`)}
        ${current?.kind !== 'always' ? group('Combine', [opt('both this and…', { kind: 'and' }), opt('either this or…', { kind: 'or' }), opt('not this', { kind: 'not' })]) : ''}`;
    }
    return html`${head(pk.mode === 'add' ? 'Add something to do' : pk.mode === 'insert' ? `Add a step before step ${pk.at + 1}` : 'Pick an action')}
      ${group('Train', UNITS.map((t) => opt(unitWord(t), { kind: 'train', unit: t })).concat([opt('a mix, in turn', { kind: 'trainMix' })]))}
      ${group('Build', BUILDINGS.slice(0, 5).map((b) => opt(bWord(b), { kind: 'build', building: b })).concat([opt(say('research an upgrade', 'take a class'), { kind: 'research' }), opt(`expand to new ${ORE}`, { kind: 'expand' })]))}
      ${group('Army', [opt('attack', { kind: 'attack' }), opt('defend', { kind: 'defend' }), opt('bring home', { kind: 'retreat' }), opt('send a scout to the enemy', { kind: 'scout', target: 'enemyBase' }), opt(`explore for more ${ORE}`, { kind: 'scout', target: 'explore' })])}`;
  }
}

// ------------------------------------------------------------------ shared bits

const ICON = {
  download: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v12"/><polyline points="7 10 12 15 17 10"/><path d="M4 20h16"/></svg>',
  code: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>',
  check: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"/></svg>',
  grip: '<svg width="8" height="14" viewBox="0 0 8 14" fill="currentColor" aria-hidden="true"><circle cx="2" cy="2" r="1.4"/><circle cx="6" cy="2" r="1.4"/><circle cx="2" cy="7" r="1.4"/><circle cx="6" cy="7" r="1.4"/><circle cx="2" cy="12" r="1.4"/><circle cx="6" cy="12" r="1.4"/></svg>',
  lock: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="1"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/></svg>',
};

// Set by the editor's "Play it": the locker opens scrolled to step 3.
let jumpToPlay = false;

// The key file the locker's "Browse for a key file" reads back.
function downloadKey(me) {
  if (!me?.apiKey) return;
  const body = JSON.stringify({ playerId: me.playerId, name: me.name, apiKey: me.apiKey }, null, 2);
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([body], { type: 'application/json' }));
  a.download = `${me.name}-api-key.json`;
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

function download(strategy) {
  const body = JSON.stringify({ format: 'netrts-block-bot', version: 1, program: strategy.program }, null, 2);
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([body], { type: 'application/json' }));
  a.download = `${(strategy.program.name || 'strategy').replace(/[^A-Za-z0-9_-]+/g, '-').replace(/^-|-$/g, '') || 'strategy'}.json`;
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

/** Reads a downloaded strategy file; returns the program or throws with a message a kid can read. */
async function readStrategyFile(file) {
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

const describeRules = (p) => `${p.rules.length} rule${p.rules.length === 1 ? '' : 's'}${p.switches?.length ? ` · ${p.switches.length} switch${p.switches.length === 1 ? '' : 'es'}` : ''}`;

// ------------------------------------------------------------------ router

export function mountBlocks(main, route) {
  if (route?.page === 'edit') return mountEditor(main, route.id);
  if (route?.page === 'match') return mountCockpit(main, route.id);
  return mountLocker(main);
}

// ------------------------------------------------------------------ the locker

function mountLocker(main) {
  let alive = true;
  const st = {
    mode: prefs().mode ?? 'HouseBot',
    one: prefs().one ?? 'sitter',
    many: prefs().many ?? ['rusher', 'economist'],
    fast: prefs().fast ?? false,
    selected: prefs().selected ?? null,
    bots: [],
    waiting: [],
    live: [],
    joinMatch: null,
    real: prefs().real ?? 'join',   // Real match: 'join' one that's waiting, or 'create' one for others to join
    seats: prefs().seats ?? 4,
    matchName: '',
    busy: false,
    switching: false, // "Change" on step 1: show the name / key form again without forgetting the current badger
    error: '',
    fileError: '',
  };

  function ensureStarter() {
    if (Object.keys(strategies()).length === 0) {
      const s = newStrategy(STARTERS.first.program);
      st.selected = s.id;
    }
  }
  ensureStarter();

  async function refresh() {
    try { st.bots = await api('/api/v1/bots'); } catch { st.bots = []; }
    try {
      const waiting = await api('/api/v1/matches?status=Waiting');
      // Waiting practice matches, but not one I'm already in (one I created, say).
      const meId = player()?.playerId;
      st.waiting = waiting.filter((m) => m.category === 'Practice' && !m.heats && !m.players.some((p) => p.playerId === meId));
    } catch { st.waiting = []; }
    if (player()) {
      try { st.live = (await authed('/api/v1/blocks/matches')).filter((m) => m.matchStatus !== 'Completed'); } catch { st.live = []; }
    }
    if (alive) render();
  }

  function sorted() {
    return Object.values(strategies()).sort((a, b) => (b.updatedAt > a.updatedAt ? 1 : -1));
  }

  function render() {
    const list = sorted();
    if (!list.some((s) => s.id === st.selected)) st.selected = list[0]?.id ?? null;
    const me = player();
    const picked = list.find((s) => s.id === st.selected);
    const multi = st.mode === 'FreeForAll';
    const startLabel = {
      Sandbox: 'Start the sandbox',
      HouseBot: `Play vs ${botLabel(st.one)}`,
      FreeForAll: st.many.length >= 2 ? `Start a free-for-all (${st.many.length} bots)` : 'Pick 2 or 3 bots',
      Real: st.real === 'create' ? `Create a match for ${st.seats} players` : st.joinMatch ? 'Join the match' : 'Pick a match',
    }[st.mode];
    const canStart = me && picked && !st.busy && !(multi && (st.many.length < 2 || st.many.length > 3)) && !(st.mode === 'Real' && st.real === 'join' && !st.joinMatch);

    // One column, three steps: name your badger, pick a strategy, pick who to play.
    // Until the badger exists, steps 2 and 3 are dimmed and inert so the name is the only thing to do.
    const locked = !me;
    const lockTag = locked ? html`<span class="bb-lock">${raw(ICON.lock)} After step 1</span>` : '';
    const lockAttr = locked ? 'inert' : '';

    setHtml(main, html`
      <section class="bb-page bb-locker">
        <header class="bb-hero">
          <p class="eyebrow">${GAME}</p>
          <h1 class="headline">Block Bot</h1>
          <p class="lede">Build a bot out of rules, no typing code. The server plays it for you, and you can change the rules while it plays.</p>
        </header>

        ${st.live.length ? html`
          <section class="bb-card" aria-labelledby="bb-live-h">
            <h2 id="bb-live-h">Playing now</h2>
            <ul class="bb-live-list">
              ${st.live.map((m) => html`<li><a class="btn" href="#/blocks/match/${m.matchId}">Open the cockpit</a> <b>${m.programName || 'Strategy'}</b> <span class="muted">· ${m.matchStatus === 'Waiting' ? 'waiting for players' : `tick ${m.tick}`} · version ${m.version}</span></li>`)}
            </ul>
          </section>` : ''}

        ${me && !st.switching ? html`
          <section class="bb-stage" aria-labelledby="bb-s1-h">
            <div class="bb-stage-head">
              <span class="bb-stage-num done" aria-hidden="true">${raw(ICON.check)}</span>
              <h2 id="bb-s1-h">Playing as ${me.name}</h2>
              <button type="button" class="btn ghost small-btn" data-switch="1">Change</button>
            </div>
          </section>` : html`
          <section class="bb-card bb-stage bb-signup" aria-labelledby="bb-s1-h">
            <div class="bb-stage-head">
              <span class="bb-stage-num hot" aria-hidden="true">1</span>
              <h2 id="bb-s1-h">${me ? 'Play as someone else' : `Name your ${say('bot', 'badger')}`}</h2>
            </div>
            ${me ? html`
              <div class="bb-switching">
                <p>You’re playing as <b>${me.name}</b>. Its key is saved only in this browser, so download it if you want to come back to it.</p>
                <div class="bb-actions">
                  <button type="button" class="btn ghost small-btn" data-dlkey="1">${raw(ICON.download)} Download ${me.name}’s key</button>
                  <button type="button" class="btn ghost small-btn" data-switch-cancel="1">Keep playing as ${me.name}</button>
                </div>
              </div>
              <p class="muted">Pick a new name, or load a key you already have.</p>` : html`
              <p class="muted">This is the name other players see on the scoreboard. Letters, numbers, - and _, 3 to 32 of them.</p>`}
            <form id="bb-signup" class="bb-inline-form bb-signup-form">
              <label class="visually-hidden" for="bb-name">Name</label>
              <input id="bb-name" name="name" required minlength="3" maxlength="32" pattern="[A-Za-z0-9_\\-]{3,32}" placeholder="e.g. rowan-b" autocomplete="off">
              <button class="btn" type="submit">Make my ${say('bot', 'badger')}</button>
            </form>
            <details class="small"><summary>I already have an API key</summary>
              <form id="bb-key" class="bb-inline-form">
                <label class="visually-hidden" for="bb-key-in">API key</label>
                <input id="bb-key-in" name="key" required placeholder="Paste your key" autocomplete="off">
                <button class="btn ghost" type="submit">Use this key</button>
                <span class="muted">or</span>
                <label class="btn ghost bb-file">Browse for a key file<input type="file" accept=".txt,.json,.env,.key,text/plain,application/json" id="bb-keyfile"></label>
              </form>
            </details>
            <p class="bb-error" id="bb-signup-err" role="alert">${st.error}</p>
          </section>`}

        <section class="bb-stage${locked ? ' locked' : ''}" aria-labelledby="bb-s2-h" ${lockAttr}>
          <div class="bb-stage-head">
            <span class="bb-stage-num" aria-hidden="true">2</span>
            <h2 id="bb-s2-h">Pick a strategy to bring</h2>
            ${lockTag}
          </div>
          <p class="bb-error" role="alert">${st.fileError}</p>
          <div class="bb-grid">
            ${list.map((s) => html`
              <article class="bb-card bb-strategy${s.id === st.selected ? ' picked' : ''}">
                <button type="button" class="bb-pick" data-pick="${s.id}" aria-pressed="${s.id === st.selected}">
                  <span class="bb-sname">${s.program.name || 'Untitled'}</span>
                  <span class="small muted">${describeRules(s.program)} · changed ${timeAgo(s.updatedAt)}</span>
                  ${s.id === st.selected ? html`<span class="bb-bringing">Bringing this one</span>` : ''}
                </button>
                <div class="bb-actions">
                  <a class="btn small-btn" href="#/blocks/edit/${s.id}">Edit</a>
                  <button type="button" class="btn ghost small-btn" data-download="${s.id}">Download</button>
                  <button type="button" class="btn ghost small-btn" data-copy="${s.id}">Copy</button>
                  <button type="button" class="btn ghost small-btn" data-delete="${s.id}" aria-label="Delete ${s.program.name}">Delete</button>
                </div>
              </article>`)}
            <article class="bb-card bb-new">
              <h3>New strategy</h3>
              <p class="small muted">Start blank, or from a starter and change it.</p>
              <div class="bb-actions">
                ${Object.entries(STARTERS).map(([k, v]) => html`<button type="button" class="btn ${k === 'blank' ? '' : 'ghost'} small-btn" data-new="${k}">${v.label}</button>`)}
              </div>
            </article>
          </div>
          <div class="bb-stage-foot">
            <p class="small muted">Saved in this browser as you work. Download one to keep it safe or move it to another computer.</p>
            <label class="btn ghost small-btn bb-file">Open a strategy file<input type="file" accept=".json,application/json" id="bb-open"></label>
          </div>
        </section>

        <section class="bb-stage${locked ? ' locked' : ''}" id="bb-s3" aria-labelledby="bb-s3-h" ${lockAttr}>
          <div class="bb-stage-head">
            <span class="bb-stage-num" aria-hidden="true">3</span>
            <h2 id="bb-s3-h">Pick who to play</h2>
            ${lockTag}
          </div>
          <fieldset>
            <legend class="bb-step">Where do you want to play?</legend>
            <div class="bb-modes">
              ${[['Sandbox', 'Sandbox', `Try a build against the ${botLabel('sitter')}, who only digs.`],
                 ['HouseBot', 'Vs a house bot', 'One opponent, any time.'],
                 ['FreeForAll', 'Free-for-all', 'You vs 2 or 3 house bots.'],
                 ['Real', 'Real match', 'Join or create a match with other players.']]
                .map(([id, name, desc]) => html`<button type="button" class="bb-mode" data-mode="${id}" aria-pressed="${st.mode === id}"><b>${name}</b><span class="small muted">${desc}</span></button>`)}
            </div>
          </fieldset>

          ${st.mode === 'HouseBot' || st.mode === 'FreeForAll' ? html`
            <fieldset>
              <legend class="bb-step">${multi ? 'Pick 2 or 3 opponents' : 'Pick an opponent'}</legend>
              <div class="bb-bots">
                ${st.bots.map((b) => {
                  const on = multi ? st.many.includes(b.name) : st.one === b.name;
                  return html`<button type="button" class="bb-bot${multi ? ' multi' : ''}" data-bot="${b.name}" aria-pressed="${on}"><span class="bb-mark" aria-hidden="true">${on ? '✓' : ''}</span><span><b>${botLabel(b.name)}</b><span class="small muted">${shortBlurb(b.description)}</span></span></button>`;
                })}
              </div>
            </fieldset>` : ''}

          ${st.mode === 'Real' ? html`
            <div class="bb-seg bb-real-seg" role="group" aria-label="Join or create">
              <button type="button" data-real="join" aria-pressed="${st.real === 'join'}">Join a match</button>
              <button type="button" data-real="create" aria-pressed="${st.real === 'create'}">Create a match</button>
            </div>` : ''}

          ${st.mode === 'Real' && st.real === 'create' ? html`
            <fieldset>
              <legend class="bb-step">How many players?</legend>
              <div class="bb-seg">
                ${[2, 3, 4].map((n) => html`<button type="button" data-seats="${n}" aria-pressed="${st.seats === n}">${n}</button>`)}
              </div>
              <p class="small muted">Everyone brings their own Block Bot (or a bot they wrote). It starts when every seat is taken, or when you start it from the cockpit.</p>
            </fieldset>
            <form id="bb-create" class="bb-inline-form">
              <label for="bb-match-name" class="small muted">A name others can type to find it (optional)</label>
              <input id="bb-match-name" maxlength="32" pattern="[A-Za-z0-9_\-]{3,32}" placeholder="e.g. period-3" autocomplete="off" value="${st.matchName}">
            </form>` : ''}

          ${st.mode === 'Real' && st.real === 'join' ? html`
            <fieldset>
              <legend class="bb-step">Pick a match</legend>
              ${st.waiting.length ? html`<div class="bb-bots">${st.waiting.map((m) => html`
                <button type="button" class="bb-bot" data-match="${m.matchId}" aria-pressed="${st.joinMatch === m.matchId}">
                  <span class="bb-mark" aria-hidden="true">${st.joinMatch === m.matchId ? '✓' : ''}</span>
                  <span><b>${m.name || `Match ${m.matchId.slice(0, 8)}`}</b><span class="small muted">${m.players.length} of ${m.maxPlayers} seats${m.hostedBy ? ` · hosted by ${m.hostedBy}` : ''} · starts when ${m.hostedBy ? 'the host starts it' : 'full, or when its creator starts it'}</span></span>
                </button>`)}</div>`
                : html`<p class="small muted">No matches are waiting for players right now.</p>`}
              <form id="bb-code" class="bb-inline-form">
                <label for="bb-code-in" class="small muted">Or type the match name your teacher gave you</label>
                <input id="bb-code-in" maxlength="40" placeholder="e.g. cs1400-lab" autocomplete="off">
                <button class="btn ghost" type="submit">Find it</button>
              </form>
              <p class="small muted">League matches don't take Block Bots yet.</p>
            </fieldset>` : ''}

          ${st.mode !== 'Real' || st.real === 'create' ? html`
            <fieldset class="bb-speed">
              <legend class="bb-step">Speed</legend>
              <div class="bb-seg">
                <button type="button" data-fast="0" aria-pressed="${!st.fast}">Normal</button>
                <button type="button" data-fast="1" aria-pressed="${st.fast}">Fast (4×)</button>
              </div>
            </fieldset>` : ''}

          <p class="bb-bring">${picked ? html`Bringing <b>${picked.program.name}</b>` : 'Pick a strategy in step 2'}</p>
          <button type="button" class="btn start bb-start" id="bb-start" ${canStart ? '' : 'disabled'}>${me ? startLabel : `Name your ${say('bot', 'badger')} first`}</button>
          <p class="bb-error" role="alert">${me ? st.error : ''}</p>
          <p class="small muted">Your bot plays on the game server. Close this tab and it keeps playing. Come back any time to watch or change it.</p>
        </section>
      </section>`);
  }

  main.addEventListener('click', onClick);
  main.addEventListener('submit', onSubmit);
  main.addEventListener('change', onChange);
  main.addEventListener('input', onInput);

  function onClick(e) {
    const t = e.target.closest('button');
    if (!t) return;
    const d = t.dataset;
    if (d.switch) {
      st.switching = true;
      render();
      main.querySelector('#bb-name')?.focus();
    } else if (d.switchCancel) { st.switching = false; render(); }
    else if (d.dlkey) downloadKey(player());
    else if (d.pick) { st.selected = d.pick; setPref('selected', d.pick); render(); }
    else if (d.download) download(getStrategy(d.download));
    else if (d.copy) {
      const s = getStrategy(d.copy);
      const c = newStrategy({ ...s.program, name: `${s.program.name} copy`.slice(0, 40) });
      st.selected = c.id;
      render();
    } else if (d.delete) {
      const s = getStrategy(d.delete);
      if (t.dataset.confirm === 'yes') { deleteStrategy(d.delete); render(); }
      else { t.dataset.confirm = 'yes'; t.textContent = `Delete ${s.program.name}?`; t.classList.add('danger'); }
    } else if (d.new) {
      const s = newStrategy(STARTERS[d.new].program);
      location.hash = `#/blocks/edit/${s.id}`;
    } else if (d.mode) { st.mode = d.mode; setPref('mode', d.mode); st.error = ''; render(); }
    else if (d.bot) {
      if (st.mode === 'FreeForAll') {
        st.many = st.many.includes(d.bot) ? st.many.filter((x) => x !== d.bot) : [...st.many, d.bot].slice(-3);
        setPref('many', st.many);
      } else { st.one = d.bot; setPref('one', d.bot); }
      render();
    } else if (d.match) { st.joinMatch = d.match; render(); }
    else if (d.real) { st.real = d.real; setPref('real', d.real); st.error = ''; render(); }
    else if (d.seats) { st.seats = Number(d.seats); setPref('seats', st.seats); render(); }
    else if (d.fast) { st.fast = d.fast === '1'; setPref('fast', st.fast); render(); }
    else if (t.id === 'bb-start') start();
  }

  async function onSubmit(e) {
    e.preventDefault();
    const err = main.querySelector('#bb-signup-err');
    if (e.target.id === 'bb-signup') {
      const name = e.target.name.value.trim();
      try {
        const res = await api('/api/v1/players', { method: 'POST', body: { name } });
        store(KEYS.player, { playerId: res.playerId, name: res.name, apiKey: res.apiKey });
        signedIn();
      } catch (ex) { if (err) err.textContent = ex.message; }
    } else if (e.target.id === 'bb-create') {
      start();
    } else if (e.target.id === 'bb-key') {
      useKey(e.target.key.value.trim());
    } else if (e.target.id === 'bb-code') {
      const name = main.querySelector('#bb-code-in').value.trim();
      if (!name) return;
      try {
        const m = await api(`/api/v1/matches/named/${encodeURIComponent(name)}`);
        if (m.status !== 'Waiting') throw new Error(`"${name}" has already ${m.status === 'Completed' ? 'finished' : 'started'}.`);
        if (!st.waiting.some((w) => w.matchId === m.matchId)) st.waiting.unshift(m);
        st.joinMatch = m.matchId;
        st.error = '';
      } catch (ex) { st.error = ex.status === 404 ? `There's no match called "${name}".` : ex.message; }
      render();
    }
  }

  // A new badger (or a different key) is stored: back to the done state and reload what it owns.
  function signedIn() {
    st.switching = false;
    st.error = '';
    st.live = [];
    refresh();
  }

  async function useKey(key) {
    const err = main.querySelector('#bb-signup-err');
    try {
      const me = await api('/api/v1/players/me', { headers: { authorization: `Bearer ${key}` } });
      store(KEYS.player, { playerId: me.playerId, name: me.name, apiKey: key });
      signedIn();
    } catch (ex) { if (err) err.textContent = ex.status === 401 ? 'That key didn’t work.' : ex.message; }
  }

  // A key file is any small text file with an nrts_ key in it: the bare key, the saved
  // registration JSON, a .env line. The first key found wins.
  async function onKeyFile(file) {
    const err = main.querySelector('#bb-signup-err');
    const input = main.querySelector('#bb-key-in');
    if (file.size > 64 * 1024) { if (err) err.textContent = 'That file is too big to be a key file.'; return; }
    const key = (await file.text()).match(/nrts_[A-Za-z0-9_-]{20,}/)?.[0];
    if (!key) { if (err) err.textContent = `There’s no API key (nrts_…) in ${file.name}.`; return; }
    if (input) input.value = key;
    useKey(key);
  }

  function onInput(e) {
    if (e.target.id === 'bb-match-name') st.matchName = e.target.value;
  }

  async function onChange(e) {
    if (e.target.id === 'bb-keyfile' && e.target.files?.[0]) {
      const file = e.target.files[0];
      e.target.value = '';
      return onKeyFile(file);
    }
    if (e.target.id !== 'bb-open' || !e.target.files?.[0]) return;
    try {
      const program = await readStrategyFile(e.target.files[0]);
      const s = newStrategy(program);
      st.selected = s.id;
      st.fileError = '';
    } catch (ex) { st.fileError = ex.message; }
    render();
  }

  async function start() {
    const s = getStrategy(st.selected);
    if (!s || st.busy) return;
    st.busy = true;
    st.error = '';
    render();
    try {
      let match;
      if (st.mode === 'Real' && st.real === 'create') {
        match = await authed('/api/v1/blocks/matches', {
          method: 'POST',
          body: { program: s.program, mode: 'Create', seats: st.seats, name: st.matchName.trim() || null, tickIntervalMs: st.fast ? 250 : null, theme: THEME },
        });
      } else if (st.mode === 'Real') {
        match = await authed(`/api/v1/blocks/matches/${st.joinMatch}/join`, { method: 'POST', body: { program: s.program, theme: THEME } });
      } else {
        match = await authed('/api/v1/blocks/matches', {
          method: 'POST',
          body: {
            program: s.program,
            mode: st.mode,
            opponents: st.mode === 'FreeForAll' ? st.many : st.mode === 'HouseBot' ? [st.one] : [],
            tickIntervalMs: st.fast ? 250 : null,
            theme: THEME,
          },
        });
      }
      recordMatch(match.matchId, s.id);
      location.hash = `#/blocks/match/${match.matchId}`;
    } catch (ex) {
      st.busy = false;
      if (ex.status === 401) {
        store(KEYS.player, null);
        st.error = `This server doesn’t know your ${say('bot', 'badger')} yet. Pick a name above and try again.`;
        if (alive) render();
        return;
      }
      st.error = ex.code === 'INVALID_PROGRAM' ? `Your strategy has a problem: ${ex.message}` : ex.message;
      if (alive) render();
    }
  }

  render();
  if (jumpToPlay) {
    jumpToPlay = false;
    // After the router's scrollTo(0, 0), which runs once this mount returns.
    requestAnimationFrame(() => { if (alive) main.querySelector('#bb-s3')?.scrollIntoView({ block: 'start' }); });
  }
  refresh();
  return () => {
    alive = false;
    main.removeEventListener('click', onClick);
    main.removeEventListener('submit', onSubmit);
    main.removeEventListener('change', onChange);
    main.removeEventListener('input', onInput);
  };
}

function botLabel(name) { return nickname(name) ?? name; }
function shortBlurb(desc) {
  // House bot descriptions start with the nickname ("The Grub Hoarder: booms..."): keep what it does.
  const s = String(desc || '');
  const i = s.indexOf(':');
  const rest = i > 0 && i < 30 ? s.slice(i + 1).trim() : s;
  return rest.charAt(0).toUpperCase() + rest.slice(1);
}

// ------------------------------------------------------------------ the editor page

function mountEditor(main, id) {
  const s = getStrategy(id);
  if (!s) {
    setHtml(main, html`<section class="bb-page"><h1>Strategy not found</h1><p>It isn’t saved in this browser. <a href="#/blocks">Back to my strategies</a></p></section>`);
    return () => {};
  }
  let strategy = s;
  let liveMatch = null;
  let alive = true;

  // Python, Download and Play it: in the top bar on narrow screens (which sticks), at the top of the
  // side panel (which stays in view as the rules scroll) on wide ones.
  const actions = (code) => html`
    <button type="button" class="btn ghost" data-ed="code" aria-pressed="${code}">${raw(ICON.code)} Python</button>
    <button type="button" class="btn ghost" data-ed="dl">${raw(ICON.download)} Download</button>
    <a class="btn start" href="#/blocks" data-ed="play">Play it</a>`;

  setHtml(main, html`
    <section class="bb-page bb-edit-page">
      <div class="bb-bar">
        <a href="#/blocks" class="bb-back">← My strategies</a>
        <label class="visually-hidden" for="bb-sname">Strategy name</label>
        <input id="bb-sname" class="bb-title-in" maxlength="40" value="${strategy.program.name}">
        <span class="bb-saved small muted" id="bb-saved">${raw(ICON.check)} Saved in this browser</span>
        <span class="bb-spacer"></span>
        <div class="bb-edit-actions bb-top-actions">${actions(!!prefs().code)}</div>
      </div>
      <p class="bb-banner" id="bb-livebanner" hidden></p>
      <div id="bb-editor"></div>
    </section>`);

  const savedEl = main.querySelector('#bb-saved');
  const editor = new RuleEditor(main.querySelector('#bb-editor'), {
    program: strategy.program,
    sideTop: (ed) => html`<div class="bb-edit-actions bb-side-actions">${actions(ed.code)}</div>`,
    onChange: (program) => {
      strategy = { ...strategy, program: { ...program, name: strategy.program.name } };
      save();
    },
  });

  function save() {
    const ok = putStrategy(strategy);
    setHtml(savedEl, ok ? html`${raw(ICON.check)} Saved in this browser` : html`Couldn’t save here. Download a copy!`);
  }

  main.querySelector('#bb-sname').addEventListener('input', (e) => {
    strategy = { ...strategy, program: { ...strategy.program, name: e.target.value.slice(0, 40) } };
    editor.program.name = strategy.program.name;
    save();
  });
  const onAction = (e) => {
    const b = e.target.closest('[data-ed]');
    if (!b) return;
    if (b.dataset.ed === 'code') {
      editor.setCode(!editor.code);
      main.querySelectorAll('[data-ed="code"]').forEach((x) => x.setAttribute('aria-pressed', String(editor.code)));
    } else if (b.dataset.ed === 'dl') download(strategy);
    else if (b.dataset.ed === 'play') { setPref('selected', strategy.id); jumpToPlay = true; }
  };
  main.addEventListener('click', onAction);

  // A match already playing this strategy: changes here go live from its cockpit.
  (async () => {
    if (!player()) return;
    const recs = matchRecords();
    try {
      const live = (await authed('/api/v1/blocks/matches')).filter((m) => m.matchStatus !== 'Completed' && recs[m.matchId]?.strategyId === strategy.id);
      liveMatch = live[0] ?? null;
    } catch { liveMatch = null; }
    if (!alive || !liveMatch) return;
    const banner = main.querySelector('#bb-livebanner');
    banner.hidden = false;
    setHtml(banner, html`This strategy is playing a match right now. <a href="#/blocks/match/${liveMatch.matchId}">Open the cockpit</a> to watch it and apply your changes.`);
  })();

  return () => { alive = false; main.removeEventListener('click', onAction); };
}

// ------------------------------------------------------------------ the cockpit

function mountCockpit(main, matchId) {
  let alive = true;
  let summary = null;
  let status = null;
  let strategy = null;      // the strategy being edited here (saved in the locker as you go)
  let view3d = null;
  let es = null;
  let lastState = null;
  let hovered = null;
  let applying = false;
  // Home: your base from above, a little zoomed out. The view starts there; the ⌂ button comes back.
  const HOME_VIEW = { dist: 16, pitch: 1.05 };
  let homed = false;
  let mapData = null;
  let audio = null;
  const QUALITY_NAMES = { low: 'Low', medium: 'Medium', high: 'High' };
  const QUALITY_BARS = { low: 1, medium: 2, high: 3 };
  /** The speaker (sound on or muted) and the signal bars (picture quality: one, two or three filled). */
  function renderViewTools() {
    const q = $('bb-quality'), snd = $('bb-sound');
    if (q && view3d) {
      const n = QUALITY_BARS[view3d.quality] ?? 2;
      const label = `Picture quality: ${QUALITY_NAMES[view3d.quality] ?? 'Medium'}. Click to change.`;
      q.setAttribute('aria-label', label);
      q.title = label;
      setHtml(q, html`<svg width="20" height="20" viewBox="0 0 24 24" aria-hidden="true">${[0, 1, 2].map((i) =>
        html`<rect x="${2 + i * 7.5}" y="${16 - i * 6}" width="5" height="${6 + i * 6}" rx="1" fill="${i < n ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="1.5"/>`)}</svg>`);
    }
    if (snd && audio) {
      const muted = audio.settings.muted;
      const label = muted ? 'Sound is off: turn it on' : 'Sound is on: mute';
      snd.setAttribute('aria-pressed', String(!muted));
      snd.setAttribute('aria-label', label);
      snd.title = label;
      setHtml(snd, html`<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="${muted ? 'M16 9l5 6M21 9l-5 6' : 'M16 9.5a4 4 0 0 1 0 5M18.5 7a7.5 7.5 0 0 1 0 10'}"/></svg>`);
    }
  }

  // ---- the stats rail: a trend line per stat over the last minute, and the scoreboard
  const HISTORY_TICKS = 60;
  const history = [];   // { tick, grubs, workers, fighters, buildings }, oldest first
  function remember() {
    const s = status?.stats;
    if (!s || (history.length && history[history.length - 1].tick >= status.tick)) return;
    history.push({ tick: status.tick, grubs: s.grubs, workers: s.workers, fighters: s.fighters, buildings: s.buildings });
    while (history.length > 2 && history[0].tick < status.tick - HISTORY_TICKS) history.shift();
  }
  const fmtN = (n) => Number(n ?? 0).toLocaleString();
  function trendTile(key, label, color) {
    const now = history[history.length - 1]?.[key] ?? status?.stats?.[key] ?? 0;
    const first = history[0], last = history[history.length - 1];
    const span = first && last ? last.tick - first.tick : 0;
    const perMin = span >= 5 ? Math.round(((last[key] - first[key]) * 60) / span) : null;
    const W = 112, H = 20;
    let line = '', area = '', mark = null;
    if (history.length >= 2) {
      const vals = history.map((h) => h[key]);
      const lo = Math.min(...vals), hi = Math.max(...vals), range = Math.max(1, hi - lo);
      const x = (t) => ((t - first.tick) / Math.max(1, span)) * W;
      line = history.map((h) => `${x(h.tick).toFixed(1)},${(H - 2 - ((h[key] - lo) / range) * (H - 4)).toFixed(1)}`).join(' ');
      area = `0,${H} ${line} ${W},${H}`;
      // Where the newest version went live, when that was within the last minute.
      const v = status?.versions?.[status.versions.length - 1];
      if (v && v.version > 1 && v.tick > first.tick) mark = x(v.tick).toFixed(1);
    }
    const rate = perMin == null ? '' : perMin === 0 ? 'steady' : `${perMin > 0 ? '▲ +' : '▼ −'}${fmtN(Math.abs(perMin))}/min`;
    const rateClass = perMin == null || perMin === 0 ? '' : perMin > 0 ? ' up' : ' down';
    return html`<div class="bb-tile">
      <span class="bb-tile-label">${label}</span>
      <span class="bb-tile-row"><span class="bb-tile-value" style="color:${color}">${fmtN(now)}</span><span class="bb-tile-rate${rateClass}">${rate}</span></span>
      <svg width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${label} over the last minute, now ${fmtN(now)}">
        ${line ? html`<polyline points="${area}" fill="${color}" fill-opacity="0.14" stroke="none"/><polyline points="${line}" fill="none" stroke="${color}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>` : ''}
        ${mark ? html`<line x1="${mark}" y1="0" x2="${mark}" y2="${H}" stroke="#f2c14e" stroke-width="1" stroke-dasharray="2 2"><title>v${status.versions[status.versions.length - 1].version} went live</title></line>` : ''}
      </svg>
    </div>`;
  }
  /** Everyone when it fits; with lots of players, the top four, then you. */
  function scoreboard() {
    const players = [...(lastState?.players || [])]
      .sort((a, b) => (a.eliminated - b.eliminated) || ((b.score?.total ?? 0) - (a.score?.total ?? 0)))
      .map((p, i) => ({ ...p, rank: i + 1, me: p.slot === status?.slot }));
    if (!players.length) return '';
    let rows = players;
    if (players.length > 5) {
      const top = players.slice(0, 4);
      rows = top.some((p) => p.me) ? players.slice(0, 5) : [...top, null, players.find((p) => p.me)].filter((p) => p !== undefined);
    }
    return html`<div class="bb-tile bb-board">
      <span class="bb-tile-label">Scoreboard <span class="bb-board-count">${players.length} players</span></span>
      <ol>${rows.map((p) => p == null
        ? html`<li class="bb-board-gap" aria-hidden="true">⋯</li>`
        : html`<li class="${p.me ? 'me' : ''}${p.eliminated ? ' out' : ''}" style="--pc:${slotVar(p.slot)}"><span class="bb-board-rank">${p.rank}</span><span class="bb-board-name">${p.me ? 'You' : displayName(p.name)}</span><span class="bb-board-score">${fmtN(p.score?.total)}</span></li>`)}</ol>
    </div>`;
  }
  /** The tick, the tick limit and roughly how long until it (a match can also end sooner, by elimination). */
  function clockTile() {
    const tick = Math.max(lastState?.tick ?? 0, status?.tick ?? 0);
    const max = lastState?.maxTicks ?? summary?.maxTicks ?? 0;
    if (!max) return '';
    const done = summary?.status === 'Completed' || status?.matchStatus === 'Completed';
    const secs = Math.round((Math.max(0, max - tick) * (lastState?.tickIntervalMs ?? summary?.tickIntervalMs ?? 1000)) / 1000);
    const left = done ? 'final' : `${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')} left`;
    const pct = Math.min(100, (tick / max) * 100);
    return html`<div class="bb-tile bb-clock" title="The match ends at tick ${fmtN(max)} if nobody is knocked out first. Then the highest score wins.">
      <span class="bb-tile-label">Tick <span>${left}</span></span>
      <span class="bb-tile-value">${fmtN(tick)}<small> / ${fmtN(max)}</small></span>
      <span class="bb-clock-bar" role="progressbar" aria-label="Match progress" aria-valuemin="0" aria-valuemax="${max}" aria-valuenow="${tick}"><span style="width:${pct.toFixed(1)}%"></span></span>
    </div>`;
  }

  function renderHud() {
    const box = $('bb-hud');
    if (!box || !status) return;
    setHtml(box, html`
      ${clockTile()}
      ${trendTile('grubs', ORE[0].toUpperCase() + ORE.slice(1), '#f2c14e')}
      ${trendTile('workers', 'Workers', '#7cc7f0')}
      ${trendTile('fighters', 'Fighters', '#ff8a7a')}
      ${trendTile('buildings', 'Buildings', '#c7b2f0')}
      ${scoreboard()}`);
  }
  const timers = [];
  const later = (fn, ms) => timers.push(setTimeout(() => { if (alive) fn(); }, ms));

  if (!player()) {
    setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1><p>This browser doesn’t have your ${say('bot', 'badger')}’s key. <a href="#/blocks">Go to the locker</a> to set it up, or <a href="#/match/${matchId}">just watch the match</a>.</p></section>`);
    return () => {};
  }

  main.classList.add('wide', 'bb-full');
  setHtml(main, html`
    <section class="bb-cockpit" id="bb-cockpit">
      <aside class="bb-left" aria-label="Your rules">
        <div class="bb-left-head">
          <div class="bb-bar">
            <a href="#/blocks" class="bb-back">← My strategies</a>
            <span class="bb-live-pill" id="bb-pill">LIVE</span>
            <span class="bb-spacer"></span>
            <a class="btn ghost small-btn" href="#/match/${matchId}">Spectator view</a>
            <button type="button" class="btn ghost small-btn" id="bb-surrender">Give up</button>
          </div>
          <h1 class="bb-match-title" id="bb-title">Loading the match…</h1>
          <p class="small muted bb-running" id="bb-running"></p>
          <div class="bb-waiting" id="bb-waiting" aria-live="polite" hidden></div>
        </div>
        <div class="bb-controls">
          <section class="bb-switches-row" aria-labelledby="bb-sw-h">
            <h2 id="bb-sw-h" class="bb-label">Switches · instant</h2>
            <div class="bb-switches" id="bb-switches"></div>
          </section>
          <section class="bb-apply" id="bb-apply" aria-live="polite"></section>
        </div>
        <div class="bb-bar bb-edit-bar">
          <h2 class="bb-label" title="Point at a rule to make the ${say('units', 'badgers')} it’s moving pulse.">Your rules</h2>
          <span class="bb-spacer"></span>
          <button type="button" class="btn ghost small-btn" id="bb-code" aria-pressed="${!!prefs().code}">${raw(ICON.code)} Python</button>
        </div>
        <div id="bb-editor"></div>
        <section aria-labelledby="bb-ver-h" class="bb-ver">
          <h2 id="bb-ver-h" class="bb-label">Your versions this match</h2>
          <ol class="bb-versions" id="bb-versions"></ol>
        </section>
      </aside>
      <div class="bb-right">
        <div class="bb-3d-wrap">
          <div class="map3d bb-3d" id="bb-3d"></div>
          <div class="bb-view-left" role="group" aria-label="Camera">
            <button type="button" class="bb-sq" data-zoom="in" aria-label="Zoom in" title="Zoom in (+)">+</button>
            <button type="button" class="bb-sq" data-zoom="out" aria-label="Zoom out" title="Zoom out (−)">−</button>
            <button type="button" class="bb-sq" data-home aria-label="Home: center on my base" title="Home: center on my base (H)">${raw('<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 11l9-7 9 7"/><path d="M5 10v10h14V10"/><path d="M10 20v-5h4v5"/></svg>')}</button>
          </div>
          <div class="bb-view-right">
            <button type="button" class="bb-sq" id="bb-sound" aria-pressed="true" aria-label="Sound"></button>
            <button type="button" class="bb-sq" id="bb-quality" aria-label="Picture quality"></button>
          </div>
          <aside class="bb-hud" id="bb-hud" aria-label="Your stats over the last minute, and the scoreboard"></aside>
          <aside class="bb-bcard" id="bb-bcard" aria-label="The building you clicked" aria-live="polite" hidden></aside>
          <p class="bb-3d-msg" id="bb-3d-msg">Building the 3D valley…</p>
          <div class="bb-result" id="bb-result" hidden></div>
        </div>
      </div>
    </section>`);

  // The cockpit fills the window under the site's top bar: the rules scroll on the left, the view stays put.
  const fitHeight = () => {
    const el = main.querySelector('#bb-cockpit');
    if (el) el.style.setProperty('--bb-top', `${Math.max(0, el.getBoundingClientRect().top + window.scrollY)}px`);
  };
  fitHeight();
  window.addEventListener('resize', fitHeight);

  const $ = (id) => main.querySelector(`#${id}`);
  let editor = null;

  function ensureStrategy(program) {
    if (strategy) return;
    const rec = matchRecords()[matchId];
    strategy = rec ? getStrategy(rec.strategyId) : null;
    if (!strategy) {
      strategy = newStrategy({ ...program, name: `${program.name || 'Strategy'} (from a match)`.slice(0, 40) });
      recordMatch(matchId, strategy.id);
    }
    editor = new RuleEditor($('bb-editor'), {
      compact: true,
      program: strategy.program,
      // Each rule's Save (and each on/off, move or delete) sends the whole program in as a new version.
      onSave: async (p) => {
        if (status?.matchStatus === 'Completed') throw new Error('The match is over, so there’s nothing to send it to.');
        try {
          status = await authed(`/api/v1/blocks/matches/${matchId}/program`, { method: 'PUT', body: p });
        } catch (e) {
          throw new Error(e.code === 'INVALID_PROGRAM' ? `That doesn’t work yet: ${e.message}` : e.message);
        }
        strategy = { ...strategy, program: { ...p, name: strategy.program.name } };
        putStrategy(strategy);
        renderSide();
        renderApply();
      },
      onHover: (i) => { hovered = i; highlight(); },
    });
  }

  // The saved strategy can differ from what's playing when it was changed in the full editor meanwhile.
  const pending = () => strategy && status?.program && !editor?.busy && !sameProgram(strategy.program, status.program);

  function renderApply() {
    const box = $('bb-apply');
    if (!box || !status) return;
    if (status.matchStatus !== 'Completed' && pending()) {
      setHtml(box, html`<div class="bb-pending"><p><b>This strategy changed in the editor</b> since this version started playing.</p>
        <div class="bb-row-btns"><button type="button" class="btn start small-btn" id="bb-apply-btn" ${applying ? 'disabled' : ''}>Send the changes in</button>
        <button type="button" class="btn ghost small-btn" id="bb-undo">Keep what’s playing</button></div><p class="bb-error" id="bb-apply-err" role="alert"></p></div>`);
    } else {
      setHtml(box, '');
    }
    editor?.setStatus(status.rules, pending() ? '(running version)' : '');
  }

  function renderSide() {
    if (!status) return;
    const sw = Object.entries(status.switches || {});
    setHtml($('bb-switches'), sw.length
      ? html`${sw.map(([name, on]) => html`<button type="button" class="bb-switch" data-switch="${name}" aria-pressed="${on}">${name}<span>${on ? 'ON' : 'off'}</span></button>`)}`
      : html`<p class="small muted">None yet: add one from a check’s ▾, like “Attack!”.</p>`);
    setHtml($('bb-versions'), html`${(status.versions || []).map((v) => html`<li${v.version === status.version ? ' aria-current="true"' : ''}><b>v${v.version}</b> <span class="muted">from tick ${v.tick}</span></li>`)}`);
    remember();
    renderHud();
    $('bb-running').textContent = `${status.programName || 'Strategy'} · running v${status.version}`;
    if (status.error) $('bb-running').textContent += ` · problem: ${status.error}`;
  }

  function renderSummary() {
    if (!summary) return;
    const names = summary.players.map((p) => displayName(p.name));
    $('bb-title').textContent = summary.players.length ? names.join(' vs ') : 'Match';
    const pill = $('bb-pill');
    pill.textContent = summary.status === 'Active' ? 'LIVE' : summary.status === 'Waiting' ? 'WAITING' : 'FINISHED';
    pill.dataset.state = summary.status;
    $('bb-surrender').hidden = summary.status !== 'Active';
    const result = $('bb-result');
    if (summary.status === 'Completed') {
      const won = summary.outcome?.winnerId === player()?.playerId;
      result.hidden = false;
      setHtml(result, html`<h2>${won ? 'You won!' : summary.outcome?.winnerId ? 'Good game' : 'A draw'}</h2>
        <p>${outcomeText(summary.outcome, summary.players)}</p>
        <div class="bb-row-btns"><a class="btn start" href="#/blocks">Play again</a><a class="btn ghost" href="#/blocks/edit/${strategy?.id ?? ''}">Edit this strategy</a><a class="btn ghost" href="#/match/${matchId}">Watch the replay</a></div>`);
    }
    renderWaiting();
    if (summary.status === 'Waiting') {
      setMsg(`Waiting for players: ${summary.players.length} of ${summary.maxPlayers} seats taken.`);
      waitingMsg = true;
    } else if (waitingMsg) {
      waitingMsg = false;
      setMsg('');
    }
  }
  let waitingMsg = false;

  /** A match waiting for people: who's in, how others find it, and (for whoever created it) Start now. */
  function renderWaiting() {
    const box = $('bb-waiting');
    if (!box) return;
    if (summary?.status !== 'Waiting') { box.hidden = true; return; }
    const me = player()?.playerId;
    const creator = !summary.hostedBy && summary.players.find((p) => p.slot === 0)?.playerId === me;
    const n = summary.players.length;
    box.hidden = false;
    setHtml(box, html`
      <p><b>Waiting for players: ${n} of ${summary.maxPlayers}.</b>
        ${summary.players.map((p) => html`<span class="bb-waiting-name">${displayName(p.name)}</span>`)}</p>
      <p class="small">Others join from Block Bot → Real match → Join a match${summary.name ? html`, or by typing <b>${summary.name}</b>` : ''}.
        It starts when every seat is taken${creator ? ', or when you start it' : summary.hostedBy ? ' or when the host starts it' : ' or when its creator starts it'}.</p>
      ${creator ? html`<button type="button" class="btn start small-btn" id="bb-start-now" ${n >= 2 ? '' : 'disabled'}>${n >= 2 ? `Start now with ${n} players` : 'Start now (needs 2 players)'}</button>` : ''}
      <p class="bb-error" id="bb-start-err" role="alert"></p>`);
  }

  async function startNow(btn) {
    btn.disabled = true;
    try {
      summary = await authed(`/api/v1/blocks/matches/${matchId}/start`, { method: 'POST' });
      renderSummary();
      if (summary?.status === 'Active' && !es) startStream();
    } catch (e) {
      const err = $('bb-start-err');
      if (err) err.textContent = e.message;
      btn.disabled = false;
    }
  }

  function setMsg(text) {
    const m = $('bb-3d-msg');
    if (!m) return;
    m.hidden = !text;
    m.textContent = text || '';
  }

  // ---- data
  async function pollSummary() {
    try { summary = await api(`/api/v1/matches/${matchId}`); } catch (e) {
      if (e.status === 404) { gone(); return; }
      later(pollSummary, 10000);
      return;
    }
    renderSummary();
    if (summary?.status === 'Active' && !es) startStream();
    if (summary?.status === 'Completed' && !lastState) showFinalBoard();
    if (summary?.status !== 'Completed') later(pollSummary, summary?.status === 'Waiting' ? 2000 : 4000);
  }

  async function pollStatus() {
    try {
      status = await authed(`/api/v1/blocks/matches/${matchId}/status?theme=${THEME}`);
      ensureStrategy(status.program);
      renderSide();
      renderApply();
      if (view3d && status.slot >= 0 && view3d.vision !== status.slot) view3d.setVision(status.slot);
      highlight();
    } catch (e) {
      if (e.code === 'NO_BLOCK_BOT') { setMsg('You don’t have a Block Bot in this match.'); return; }
      if (e.status === 404) { gone(); return; }
      later(pollStatus, 10000);
      return;
    }
    if (status?.matchStatus !== 'Completed') later(pollStatus, Math.max(400, Math.min(1000, summary?.tickIntervalMs ?? 1000)));
  }

  async function start3d() {
    const box = $('bb-3d');
    try {
      const [{ View3D }, { GameAudio }] = await Promise.all([import('./3d/view.js'), import('./3d/audio.js')]);
      if (!alive) return;
      const names = () => new Map((lastState?.players || summary?.players || []).map((p) => [p.slot, displayName(p.name)]));
      audio = new GameAudio();
      audio.setActive(true);
      // The same viewer as the spectator page: its quality adapts to the computer (and remembers a choice).
      view3d = new View3D(box, { badger: SNOW, names, audio });
      box.view3d = view3d;   // handy from the dev tools console, as on the spectator page
      view3d.onQualityChange = renderViewTools;
      view3d.onPickBuilding = (b) => { pickedId = b?.id ?? null; pickedLast = b; renderBuildingCard(); };
      renderViewTools();
      view3d.onReady = () => {
        if (!alive) return;
        if (summary?.status !== 'Waiting') setMsg('');
        goHome();
      };
    } catch (e) {
      setMsg(`The 3D view couldn’t start here (${e?.message || 'no WebGL'}). Your bot is still playing.`);
    }
  }

  /** A finished match: the last picture, while the server still has it. */
  async function showFinalBoard() {
    if (!view3d) return;
    try {
      mapData = await api(`/api/v1/matches/${matchId}/map`);
      view3d.setMap(mapData);
      lastState = await api(`/api/v1/matches/${matchId}/spectate?fog=true`);
      view3d.setState(lastState);
      goHome();
      renderHud();
    } catch { /* archived: only the result is left */ }
  }

  let retryMs = 2000;
  const gone = () => setMsg('This match is over and gone from the server. Your strategy is still saved here.');
  async function startStream() {
    if (!view3d || !alive) return;
    if (!lastState) {
      try {
        const map = await api(`/api/v1/matches/${matchId}/map`);
        if (!alive) return;
        mapData = map;
        view3d.setMap(map);
        if (status?.slot >= 0) view3d.setVision(status.slot);
      } catch (e) {
        // Gone (the server restarted, say): stop. Unreachable: try again, less and less often.
        if (e.status === 404) { gone(); return; }
        later(startStream, retryMs);
        retryMs = Math.min(retryMs * 2, 30000);
        return;
      }
    }
    es = new EventSource(`/api/v1/matches/${matchId}/spectate/stream?fog=true`);
    es.onmessage = (msg) => {
      let s;
      try { s = JSON.parse(msg.data); } catch { return; }
      retryMs = 2000;   // connected again: the next drop starts the backoff over
      lastState = s;
      view3d?.setState(s);
      if (pickedId != null) renderBuildingCard();
      if (!homed) goHome();
    };
    es.onerror = () => {
      es?.close();
      es = null;
      if (alive && summary?.status !== 'Completed') { later(startStream, retryMs); retryMs = Math.min(retryMs * 2, 30000); }
    };
  }

  // ---- the building you clicked in the 3D view: what it is, whose, its health and what it's doing
  let pickedId = null;
  let pickedLast = null;   // the last picture of it, for when it's gone
  function renderBuildingCard() {
    const box = $('bb-bcard');
    if (!box) return;
    if (pickedId == null) { box.hidden = true; return; }
    const live = lastState?.buildings?.find((x) => x.id === pickedId);
    const b = live ?? pickedLast;
    if (!b) { box.hidden = true; return; }
    if (live) pickedLast = live;
    const mine = b.owner === status?.slot;
    const ownerName = displayName((lastState?.players || summary?.players || []).find((p) => p.slot === b.owner)?.name ?? `Player ${b.owner + 1}`);
    const r = RULES?.buildings?.find((x) => x.type === b.type);
    const pct = b.maxHp ? Math.max(0, Math.min(100, (b.hp / b.maxHp) * 100)) : 0;
    const stats = [
      r ? ['Armor', r.armor] : null,
      r?.damage ? ['Attack', `${r.damage} damage, range ${r.range}`] : null,
      r?.storageBonus ? ['Stores', `${fmtN(r.storageBonus)} ${ORE}`] : null,
      r?.produces?.length ? ['Trains', r.produces.map((t) => unitName(t)).join(', ')] : null,
      r?.canResearch ? ['Classes', say('researches upgrades', 'teaches the classes')] : null,
    ].filter(Boolean);
    box.hidden = false;
    setHtml(box, html`
      <div class="bb-bcard-head">
        <h2>${buildingName(b.type, b.owner)}</h2>
        <button type="button" class="bb-sq small" data-bclose aria-label="Close">×</button>
      </div>
      <p class="bb-bcard-owner"><span class="bb-bcard-dot" style="--pc:${slotVar(b.owner)}" aria-hidden="true"></span>${mine ? 'Yours' : ownerName}</p>
      ${live ? html`
        <div class="bb-bcard-hp"><span>HP</span><b>${fmtN(b.hp)} / ${fmtN(b.maxHp)}</b></div>
        <span class="bb-clock-bar bb-hpbar${pct < 34 ? ' low' : ''}" aria-hidden="true"><span style="width:${pct.toFixed(1)}%"></span></span>
        ${b.completed ? '' : html`<p>Under construction: ${b.constructionPercent}%</p>`}
        ${b.production?.length ? html`<p>${say('Producing', 'Training')}: ${unitName(b.production[0].unitType)} ${b.production[0].percent}%${b.production.length > 1 ? html` <span class="bb-bcard-q">(+${b.production.length - 1} queued)</span>` : ''}</p>` : ''}
        ${b.research ? html`<p>${say('Researching', 'Studying')}: ${upgradeName(b.research.upgrade)} ${b.research.percent}%</p>` : ''}`
        : html`<p class="bb-bcard-gone">Destroyed.</p>`}
      <p class="bb-bcard-role">${buildingRole(b.type)}</p>
      ${stats.length ? html`<dl class="bb-bcard-stats">${stats.map(([k, v]) => html`<div><dt>${k}</dt><dd>${v}</dd></div>`)}</dl>` : ''}
      ${live ? html`<button type="button" class="bb-bcard-zoom" data-bzoom>Zoom to it</button>` : ''}`);
  }

  /** Your base from above: your HQ (any of them), else where you started. */
  function goHome() {
    if (!view3d?.ready) return;
    const slot = status?.slot;
    const hq = (lastState?.buildings || []).find((b) => b.owner === slot && b.type === 'CommandCenter')
      ?? mapData?.startPositions?.[slot];
    if (!hq) return;
    homed = true;
    view3d.lookAtTile(hq.x, hq.y, HOME_VIEW);
  }
  function highlight() {
    if (!view3d) return;
    const ids = hovered != null ? status?.rules?.[hovered]?.unitIds : null;
    view3d.highlight = ids?.length ? new Set(ids) : null;
  }

  // ---- actions
  async function apply() {
    if (!strategy || applying) return;
    applying = true;
    renderApply();
    try {
      status = await authed(`/api/v1/blocks/matches/${matchId}/program`, { method: 'PUT', body: strategy.program });
      renderSide();
    } catch (e) {
      applying = false;
      renderApply();
      const err = $('bb-apply-err');
      if (err) err.textContent = e.code === 'INVALID_PROGRAM' ? `Your strategy has a problem: ${e.message}` : e.message;
      return;
    }
    applying = false;
    renderApply();
  }

  async function flip(name) {
    const on = !status?.switches?.[name];
    try {
      status = await authed(`/api/v1/blocks/matches/${matchId}/switches`, { method: 'PUT', body: { name, on } });
      renderSide();
    } catch (e) { setMsg(e.message); later(() => setMsg(''), 3000); }
  }

  function onClick(e) {
    const b = e.target.closest('button');
    if (!b) return;
    if (b.id === 'bb-apply-btn') apply();
    else if (b.id === 'bb-undo' && status?.program) {
      strategy = { ...strategy, program: { ...clone(status.program), name: strategy.program.name } };
      putStrategy(strategy);
      editor.setProgram(strategy.program);
      renderApply();
    } else if (b.dataset.switch) flip(b.dataset.switch);
    else if ('home' in b.dataset) goHome();
    else if (b.id === 'bb-start-now') startNow(b);
    else if ('bclose' in b.dataset) { pickedId = null; renderBuildingCard(); }
    else if ('bzoom' in b.dataset && pickedLast) view3d?.lookAtTile(pickedLast.x, pickedLast.y, { dist: 5 });
    else if (b.dataset.zoom) view3d?.zoom(b.dataset.zoom === 'in' ? 0.8 : 1.25);
    else if (b.id === 'bb-quality' && view3d) {
      const order = ['low', 'medium', 'high'];
      view3d.setQuality(order[(order.indexOf(view3d.quality) + 1) % order.length]);
      renderViewTools();
    } else if (b.id === 'bb-sound' && audio) {
      audio.unlock();
      audio.set('muted', !audio.settings.muted);
      renderViewTools();
    }
    else if (b.id === 'bb-code') {
      const on = b.getAttribute('aria-pressed') !== 'true';
      b.setAttribute('aria-pressed', String(on));
      editor?.setCode(on);
    } else if (b.id === 'bb-surrender') {
      if (b.dataset.confirm !== 'yes') { b.dataset.confirm = 'yes'; b.textContent = 'Really give up?'; b.classList.add('danger'); return; }
      authed(`/api/v1/matches/${matchId}/surrender`, { method: 'POST' }).catch((ex) => setMsg(ex.message));
    }
  }
  main.addEventListener('click', onClick);

  // + and - zoom from anywhere on the page, except while typing a number or picking from a list. (With
  // the 3D view focused, the view's own keys do it, smoothly while held.)
  function onKey(e) {
    if (e.ctrlKey || e.metaKey || e.altKey || !view3d) return;
    if (e.target.closest?.('input, select, textarea, [contenteditable]')) return;
    if (e.key === 'Escape' && pickedId != null && !e.target.closest?.('.bb-editor')) { pickedId = null; renderBuildingCard(); return; }
    if ((e.key === 'h' || e.key === 'H' || e.key === 'Home') && !e.target.closest?.('.map3d')) { e.preventDefault(); goHome(); return; }
    const zoom = { '+': 0.8, '=': 0.8, '-': 1.25, '_': 1.25 }[e.key];
    if (!zoom || e.target.closest?.('.map3d')) return;
    e.preventDefault();
    view3d.zoom(zoom);
  }
  document.addEventListener('keydown', onKey);

  (async () => {
    await start3d();
    await pollSummary();
    await pollStatus();
  })();

  return () => {
    alive = false;
    timers.forEach(clearTimeout);
    es?.close();
    view3d?.destroy();
    audio?.setActive(false);
    audio?.dispose?.();
    window.removeEventListener('resize', fitHeight);
    main.removeEventListener('click', onClick);
    document.removeEventListener('keydown', onKey);
    main.classList.remove('wide', 'bb-full');
  };
}

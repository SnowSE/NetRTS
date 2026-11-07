// #/blocks: Block Bot. Build a bot from rule cards, play it against the house bots or in a real match,
// and change it while the match runs. The server plays the rules (BlockBot in NetRts.Bots), so a match
// keeps going when the tab closes. Strategies live in this browser (localStorage, saved as you go) and
// download as .json files.
//
//   #/blocks                 the locker: your strategies, and where to play
//   #/blocks/edit/<id>       the rule-card editor
//   #/blocks/match/<id>      the cockpit: the match in a simple 3D view, the rules live, switches, Apply
import { api, html, raw, setHtml, displayName, timeAgo, outcomeText } from './util.js';
import { GAME, SNOW, say, unitName, upgradeName, buildingName, nickname, ORE } from './lore.js';

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
  scout: () => ({ kind: 'scout' }),
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
    case 'scout': return 'bot.scout()';
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
  constructor(root, { program, onChange, onHover }) {
    this.root = root;
    this.program = clone(program);
    this.onChange = onChange;
    this.onHover = onHover;
    this.picker = null;    // { type: 'cond' | 'action', path, mode: 'replace' | 'add' }
    this.status = null;    // the running bot's rule statuses (cockpit)
    this.statusNote = '';
    this.code = !!prefs().code;
    this.dragFrom = null;
    root.addEventListener('click', (e) => this.click(e));
    root.addEventListener('change', (e) => this.change(e));
    root.addEventListener('dragstart', (e) => {
      const grip = e.target.closest('[data-drag]');
      if (!grip) return;
      this.dragFrom = Number(grip.dataset.drag);
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', grip.dataset.drag);
    });
    root.addEventListener('dragover', (e) => { if (this.dragFrom != null && e.target.closest('.bb-rule')) e.preventDefault(); });
    root.addEventListener('drop', (e) => {
      const card = e.target.closest('.bb-rule');
      if (this.dragFrom == null || !card) return;
      e.preventDefault();
      this.move(this.dragFrom, Number(card.dataset.rule));
      this.dragFrom = null;
    });
    root.addEventListener('dragend', () => { this.dragFrom = null; });
    const hover = (e) => {
      const card = e.target.closest?.('.bb-rule');
      this.onHover?.(card ? Number(card.dataset.rule) : null);
    };
    root.addEventListener('mouseover', hover);
    root.addEventListener('focusin', hover);
    root.addEventListener('mouseleave', () => this.onHover?.(null));
    this.render();
  }

  setProgram(program) { this.program = clone(program); this.render(); }
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
    this.onChange(clone(p));
  }

  move(from, to) {
    if (from === to || from < 0 || to < 0) return;
    this.commit((p) => {
      const [r] = p.rules.splice(from, 1);
      p.rules.splice(to, 0, r);
    });
  }

  click(e) {
    const b = e.target.closest('button[data-act]');
    if (!b || !this.root.contains(b)) return;
    const { act, path } = b.dataset;
    const i = Number(b.dataset.i);
    switch (act) {
      case 'pick-cond': this.picker = { type: 'cond', path }; this.render(); this.focusPicker(); return;
      case 'pick-action': this.picker = { type: 'action', path, mode: 'replace' }; this.render(); this.focusPicker(); return;
      case 'add-action': this.picker = { type: 'action', path, mode: 'add' }; this.render(); this.focusPicker(); return;
      case 'close-picker': this.picker = null; this.render(); return;
      case 'choose': return this.choose(b.dataset);
      case 'new-switch': {
        const input = this.root.querySelector('#bb-new-switch');
        const name = (input?.value || '').trim().slice(0, 24);
        if (!name) { input?.focus(); return; }
        const path0 = this.picker?.path;
        this.commit((p) => {
          if (!p.switches.some((s) => s.toLowerCase() === name.toLowerCase())) p.switches.push(name);
          if (path0) setAt(p, path0, C.switch(name));
        });
        return;
      }
      case 'del-switch': return this.commit((p) => {
        p.switches = p.switches.filter((s) => s !== b.dataset.name);
      }, { keepPicker: true });
      case 'del-action': return this.commit((p) => { at(p, path.replace(/\.\d+$/, '')).splice(Number(path.split('.').pop()), 1); });
      case 'unwrap': return this.commit((p) => setAt(p, path, at(p, path).a));
      case 'mix-add': return this.commit((p) => at(p, path).units.push('Soldier'));
      case 'mix-del': return this.commit((p) => { const u = at(p, path).units; if (u.length > 1) u.splice(i, 1); });
      case 'rule-up': return this.move(i, i - 1);
      case 'rule-down': return this.move(i, Math.min(i + 1, this.program.rules.length - 1));
      case 'rule-toggle': return this.commit((p) => { p.rules[i].enabled = p.rules[i].enabled === false; });
      case 'rule-del': return this.commit((p) => { p.rules.splice(i, 1); });
      case 'add-rule': return this.commit((p) => { p.rules.push(R(C.count('fighters', '>=', 8), A.attack())); });
      default:
    }
  }

  choose(d) {
    const pk = this.picker;
    if (!pk) return;
    this.commit((p) => {
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
          : d.kind === 'build' ? A.build(d.building, d.building === 'ResourceDepot' ? 'NearOre' : d.building === 'GuardTower' ? 'TowardEnemy' : 'TowardCenter')
            : A[d.kind]();
        if (pk.mode === 'add') at(p, pk.path).do.push(node);
        else setAt(p, pk.path, node);
      }
    });
  }

  change(e) {
    const el = e.target;
    const { path, field } = el.dataset;
    if (!path || !field) return;
    this.commit((p) => {
      const node = at(p, path);
      if (field === 'units') node.units[Number(el.dataset.i)] = el.value;
      else if (field === 'value') node.value = Math.max(0, Math.min(100000, Math.round(Number(el.value) || 0)));
      else node[field] = el.value;
    }, { keepPicker: true });
  }

  focusPicker() { this.root.querySelector('.bb-picker [data-act="choose"]')?.focus(); }

  // ---------------------------------------------------------------- rendering

  render() {
    const focused = document.activeElement?.dataset?.k;
    const p = this.program;
    setHtml(this.root, html`
      <div class="bb-editor${this.code ? ' with-code' : ''}">
        <div class="bb-rules">
          ${p.rules.length ? '' : html`<p class="bb-empty">No rules yet. Add one, or start from one of the starter strategies in the locker.</p>`}
          ${p.rules.map((r, i) => this.ruleHtml(r, i))}
          <button type="button" class="bb-add-rule" data-act="add-rule" data-k="add-rule">+ Add a rule</button>
          <p class="small muted">Rules are checked top to bottom, every tick. Higher rules get first pick of ${ORE} and fighters.
            Two things happen on their own: idle workers dig ${ORE}, and half-built buildings get finished.</p>
        </div>
        <aside class="bb-picker" aria-label="${this.picker ? 'Pick a block' : 'How rules work'}">${this.pickerHtml()}</aside>
      </div>`);
    if (focused) this.root.querySelector(`[data-k="${CSS.escape(focused)}"]`)?.focus();
    this.renderStatus();
  }

  renderStatus() {
    this.root.querySelectorAll('.bb-rule').forEach((card) => {
      const i = Number(card.dataset.rule);
      const s = this.status?.[i];
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
    return html`
      <div class="bb-row">
        <article class="bb-rule${off ? ' off' : ''}" data-rule="${i}">
          <div class="bb-rule-main">
            <button type="button" class="bb-grip" draggable="true" data-drag="${i}" aria-label="Drag to move rule ${i + 1}" title="Drag to move">${raw('<svg width="12" height="20" viewBox="0 0 12 20" fill="currentColor" aria-hidden="true"><circle cx="3" cy="4" r="1.8"/><circle cx="9" cy="4" r="1.8"/><circle cx="3" cy="10" r="1.8"/><circle cx="9" cy="10" r="1.8"/><circle cx="3" cy="16" r="1.8"/><circle cx="9" cy="16" r="1.8"/></svg>')}</button>
            <span class="bb-num" aria-hidden="true">${i + 1}</span>
            <div class="bb-sentence">
              ${r.when?.kind === 'always' ? '' : html`<span class="bb-word">When</span>`}
              ${this.condHtml(r.when, `rules.${i}.when`)}
              <span class="bb-word">${r.when?.kind === 'always' ? '' : 'do'}</span>
              ${(r.do || []).map((a, j) => this.actionHtml(a, `rules.${i}.do.${j}`))}
              <button type="button" class="bb-plus" data-act="add-action" data-path="rules.${i}" data-k="add-action|${i}" aria-label="Add something for rule ${i + 1} to do">+ do</button>
            </div>
            <div class="bb-tools">
              <button type="button" class="bb-toggle" data-act="rule-toggle" data-i="${i}" data-k="toggle|${i}" aria-pressed="${!off}" aria-label="Rule ${i + 1} on or off">${off ? 'Off' : 'On'}</button>
              <button type="button" class="bb-icon" data-act="rule-up" data-i="${i}" data-k="up|${i}" aria-label="Move rule ${i + 1} up" ${i === 0 ? 'disabled' : ''}>↑</button>
              <button type="button" class="bb-icon" data-act="rule-down" data-i="${i}" data-k="down|${i}" aria-label="Move rule ${i + 1} down" ${i === n - 1 ? 'disabled' : ''}>↓</button>
              <button type="button" class="bb-icon" data-act="rule-del" data-i="${i}" data-k="del|${i}" aria-label="Delete rule ${i + 1}">×</button>
            </div>
          </div>
          <p class="bb-live" hidden></p>
        </article>
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
        return html`<span class="${cls} build">${kindBtn(say('research', 'take the class'))}${this.select(path, 'upgrade', a.upgrade, UPGRADES.map((u) => [u, upgradeName(u)]), 'Which upgrade')}${del}</span>`;
      case 'attack':
        return html`<span class="${cls}">${kindBtn('attack')}${this.select(path, 'target', a.target ?? 'enemyBase', TARGETS, 'Attack what')}<span>with</span>${this.select(path, 'who', a.who ?? 'fighters', WHO(), 'Who attacks')}${del}</span>`;
      case 'defend':
        return html`<span class="${cls}">${kindBtn('defend with')}${this.select(path, 'who', a.who ?? 'fighters', WHO(), 'Who defends')}${del}</span>`;
      case 'retreat':
        return html`<span class="${cls}">${kindBtn('bring home')}${this.select(path, 'who', a.who ?? 'fighters', WHO(), 'Who comes home')}${del}</span>`;
      case 'scout':
        return html`<span class="${cls}">${kindBtn('send a scout to find the enemy')}${del}</span>`;
      default:
        return html`<span class="${cls}">${kindBtn(a.kind)}${del}</span>`;
    }
  }

  pickerHtml() {
    const pk = this.picker;
    if (!pk) {
      return html`
        <h2>How rules work</h2>
        <p>Each rule says <b>when</b> and <b>what to do</b>. Every tick (about once a second) your bot checks the rules from the top.</p>
        <p>Click a <b>▾</b> to swap a piece for a different one. Change numbers and choices right in the sentence.</p>
        <p>Add a <b>switch</b> to flip during a match: a rule like "When the <i>Attack!</i> switch is on, attack" lets you send your army in whenever you want.</p>
        ${this.program.switches.length ? html`<h3>Your switches</h3><ul class="bb-switch-list">${this.program.switches.map((s) => html`<li>${s} <button type="button" class="bb-icon small" data-act="del-switch" data-name="${s}" aria-label="Delete the ${s} switch">×</button></li>`)}</ul>` : ''}`;
    }
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
        ])}
        ${group('The enemy', [opt('an enemy is near my base', { kind: 'enemyNearBase' }), opt('I can see an enemy', { kind: 'seeEnemy' })])}
        ${group('Time', [opt('minute ≥ 3', { kind: 'minute' }), opt('every tick', { kind: 'always' })])}
        ${group('Switches', html`
          ${this.program.switches.map((s) => opt(`${s} is on`, { kind: 'switch', name: s }))}
          <span class="bb-new-switch"><label class="visually-hidden" for="bb-new-switch">New switch name</label><input id="bb-new-switch" type="text" maxlength="24" placeholder="New switch, e.g. Attack!"><button type="button" class="bb-opt" data-act="new-switch">Add</button></span>`)}
        ${current?.kind !== 'always' ? group('Combine', [opt('both this and…', { kind: 'and' }), opt('either this or…', { kind: 'or' }), opt('not this', { kind: 'not' })]) : ''}`;
    }
    return html`${head(pk.mode === 'add' ? 'Add something to do' : 'Pick an action')}
      ${group('Train', UNITS.map((t) => opt(unitWord(t), { kind: 'train', unit: t })).concat([opt('a mix, in turn', { kind: 'trainMix' })]))}
      ${group('Build', BUILDINGS.slice(0, 5).map((b) => opt(bWord(b), { kind: 'build', building: b })).concat([opt(say('research an upgrade', 'take a class'), { kind: 'research' })]))}
      ${group('Army', [opt('attack', { kind: 'attack' }), opt('defend', { kind: 'defend' }), opt('bring home', { kind: 'retreat' }), opt('send a scout', { kind: 'scout' })])}`;
  }
}

// ------------------------------------------------------------------ shared bits

const ICON = {
  download: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v12"/><polyline points="7 10 12 15 17 10"/><path d="M4 20h16"/></svg>',
  code: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>',
  check: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"/></svg>',
};

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
    busy: false,
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
      st.waiting = waiting.filter((m) => m.category === 'Practice' && !m.heats);
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
      Real: st.joinMatch ? 'Join the match' : 'Pick a match',
    }[st.mode];
    const canStart = me && picked && !st.busy && !(multi && (st.many.length < 2 || st.many.length > 3)) && !(st.mode === 'Real' && !st.joinMatch);

    setHtml(main, html`
      <section class="bb-page">
        <header class="bb-hero">
          <p class="eyebrow">${GAME}</p>
          <h1 class="headline">Block Bot</h1>
          <p class="lede">Build a bot out of rules, no typing code. The server plays it for you, and you can change the rules while it plays.</p>
        </header>

        ${me ? '' : html`
          <section class="bb-card bb-signup" aria-labelledby="bb-signup-h">
            <h2 id="bb-signup-h">First, name your ${say('bot', 'badger')}</h2>
            <p class="muted">This is the name other players see on the scoreboard. Letters, numbers, - and _, 3 to 32 of them.</p>
            <form id="bb-signup" class="bb-inline-form">
              <label class="visually-hidden" for="bb-name">Name</label>
              <input id="bb-name" name="name" required minlength="3" maxlength="32" pattern="[A-Za-z0-9_\\-]{3,32}" placeholder="e.g. rowan-b" autocomplete="off">
              <button class="btn" type="submit">Make my ${say('bot', 'badger')}</button>
            </form>
            <details class="small"><summary>I already have an API key</summary>
              <form id="bb-key" class="bb-inline-form">
                <label class="visually-hidden" for="bb-key-in">API key</label>
                <input id="bb-key-in" name="key" required placeholder="Paste your key" autocomplete="off">
                <button class="btn ghost" type="submit">Use this key</button>
              </form>
            </details>
            <p class="bb-error" id="bb-signup-err" role="alert"></p>
          </section>`}

        ${st.live.length ? html`
          <section class="bb-card" aria-labelledby="bb-live-h">
            <h2 id="bb-live-h">Playing now</h2>
            <ul class="bb-live-list">
              ${st.live.map((m) => html`<li><a class="btn" href="#/blocks/match/${m.matchId}">Open the cockpit</a> <b>${m.programName || 'Strategy'}</b> <span class="muted">· ${m.matchStatus === 'Waiting' ? 'waiting for players' : `tick ${m.tick}`} · version ${m.version}</span></li>`)}
            </ul>
          </section>` : ''}

        <div class="bb-locker">
          <section class="bb-mine" aria-labelledby="bb-mine-h">
            <div class="bb-mine-head">
              <div>
                <h2 id="bb-mine-h">My strategies</h2>
                <p class="small muted">Saved in this browser as you work. Download one to keep it safe or move it to another computer.</p>
              </div>
              <label class="btn ghost bb-file">Open a strategy file<input type="file" accept=".json,application/json" id="bb-open"></label>
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
          </section>

          <aside class="bb-card bb-play" aria-labelledby="bb-play-h">
            <h2 id="bb-play-h">Play</h2>
            <fieldset>
              <legend class="bb-step">1. Where do you want to play?</legend>
              <div class="bb-modes">
                ${[['Sandbox', 'Sandbox', `Try a build against the ${botLabel('sitter')}, who only digs.`],
                   ['HouseBot', 'Vs a house bot', 'One opponent, any time.'],
                   ['FreeForAll', 'Free-for-all', 'You vs 2 or 3 house bots.'],
                   ['Real', 'Real match', 'Join a match other players are in.']]
                  .map(([id, name, desc]) => html`<button type="button" class="bb-mode" data-mode="${id}" aria-pressed="${st.mode === id}"><b>${name}</b><span class="small muted">${desc}</span></button>`)}
              </div>
            </fieldset>

            ${st.mode === 'HouseBot' || st.mode === 'FreeForAll' ? html`
              <fieldset>
                <legend class="bb-step">2. ${multi ? 'Pick 2 or 3 opponents' : 'Pick an opponent'}</legend>
                <div class="bb-bots">
                  ${st.bots.map((b) => {
                    const on = multi ? st.many.includes(b.name) : st.one === b.name;
                    return html`<button type="button" class="bb-bot${multi ? ' multi' : ''}" data-bot="${b.name}" aria-pressed="${on}"><span class="bb-mark" aria-hidden="true">${on ? '✓' : ''}</span><span><b>${botLabel(b.name)}</b><span class="small muted">${shortBlurb(b.description)}</span></span></button>`;
                  })}
                </div>
              </fieldset>` : ''}

            ${st.mode === 'Real' ? html`
              <fieldset>
                <legend class="bb-step">2. Pick a match</legend>
                ${st.waiting.length ? html`<div class="bb-bots">${st.waiting.map((m) => html`
                  <button type="button" class="bb-bot" data-match="${m.matchId}" aria-pressed="${st.joinMatch === m.matchId}">
                    <span class="bb-mark" aria-hidden="true">${st.joinMatch === m.matchId ? '✓' : ''}</span>
                    <span><b>${m.name || `Match ${m.matchId.slice(0, 8)}`}</b><span class="small muted">${m.players.length} of ${m.maxPlayers} seats${m.hostedBy ? ` · hosted by ${m.hostedBy}` : ''} · starts when ${m.hostedBy ? 'the host starts it' : 'full'}</span></span>
                  </button>`)}</div>`
                  : html`<p class="small muted">No matches are waiting for players right now.</p>`}
                <form id="bb-code" class="bb-inline-form">
                  <label for="bb-code-in" class="small muted">Or type the match name your teacher gave you</label>
                  <input id="bb-code-in" maxlength="40" placeholder="e.g. cs1400-lab" autocomplete="off">
                  <button class="btn ghost" type="submit">Find it</button>
                </form>
                <p class="small muted">League matches don't take Block Bots yet.</p>
              </fieldset>` : ''}

            ${st.mode !== 'Real' ? html`
              <fieldset>
                <legend class="bb-step">${st.mode === 'Sandbox' ? '2' : '3'}. Speed</legend>
                <div class="bb-seg">
                  <button type="button" data-fast="0" aria-pressed="${!st.fast}">Normal</button>
                  <button type="button" data-fast="1" aria-pressed="${st.fast}">Fast (4×)</button>
                </div>
              </fieldset>` : ''}

            <div>
              <p class="bb-step">Bring</p>
              <p class="bb-bring">${picked ? picked.program.name : 'Pick a strategy on the left'}</p>
            </div>
            <button type="button" class="btn start bb-start" id="bb-start" ${canStart ? '' : 'disabled'}>${me ? startLabel : `Name your ${say('bot', 'badger')} first`}</button>
            <p class="bb-error" role="alert">${st.error}</p>
            <p class="small muted">Your bot plays on the game server. Close this tab and it keeps playing. Come back any time to watch or change it.</p>
            ${me ? html`<p class="small muted">Playing as <b>${me.name}</b>.</p>` : ''}
          </aside>
        </div>
      </section>`);
  }

  main.addEventListener('click', onClick);
  main.addEventListener('submit', onSubmit);
  main.addEventListener('change', onChange);

  function onClick(e) {
    const t = e.target.closest('button');
    if (!t) return;
    const d = t.dataset;
    if (d.pick) { st.selected = d.pick; setPref('selected', d.pick); render(); }
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
        refresh();
      } catch (ex) { if (err) err.textContent = ex.message; }
    } else if (e.target.id === 'bb-key') {
      const key = e.target.key.value.trim();
      try {
        const me = await api('/api/v1/players/me', { headers: { authorization: `Bearer ${key}` } });
        store(KEYS.player, { playerId: me.playerId, name: me.name, apiKey: key });
        refresh();
      } catch (ex) { if (err) err.textContent = ex.status === 401 ? 'That key didn’t work.' : ex.message; }
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

  async function onChange(e) {
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
      if (st.mode === 'Real') {
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
  refresh();
  return () => {
    alive = false;
    main.removeEventListener('click', onClick);
    main.removeEventListener('submit', onSubmit);
    main.removeEventListener('change', onChange);
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

  setHtml(main, html`
    <section class="bb-page">
      <div class="bb-bar">
        <a href="#/blocks" class="bb-back">← My strategies</a>
        <label class="visually-hidden" for="bb-sname">Strategy name</label>
        <input id="bb-sname" class="bb-title-in" maxlength="40" value="${strategy.program.name}">
        <span class="bb-saved small muted" id="bb-saved">${raw(ICON.check)} Saved in this browser</span>
        <span class="bb-spacer"></span>
        <button type="button" class="btn ghost" id="bb-code" aria-pressed="${!!prefs().code}">${raw(ICON.code)} Python</button>
        <button type="button" class="btn ghost" id="bb-dl">${raw(ICON.download)} Download</button>
        <a class="btn start" href="#/blocks" id="bb-play">Play it</a>
      </div>
      <p class="bb-banner" id="bb-livebanner" hidden></p>
      <div id="bb-editor"></div>
    </section>`);

  const savedEl = main.querySelector('#bb-saved');
  const editor = new RuleEditor(main.querySelector('#bb-editor'), {
    program: strategy.program,
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
  const codeBtn = main.querySelector('#bb-code');
  codeBtn.addEventListener('click', () => {
    const on = codeBtn.getAttribute('aria-pressed') !== 'true';
    codeBtn.setAttribute('aria-pressed', String(on));
    editor.setCode(on);
  });
  main.querySelector('#bb-dl').addEventListener('click', () => download(strategy));
  main.querySelector('#bb-play').addEventListener('click', () => setPref('selected', strategy.id));

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

  return () => { alive = false; };
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
  let camMode = 'director';
  const timers = [];
  const later = (fn, ms) => timers.push(setTimeout(() => { if (alive) fn(); }, ms));

  if (!player()) {
    setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1><p>This browser doesn’t have your ${say('bot', 'badger')}’s key. <a href="#/blocks">Go to the locker</a> to set it up, or <a href="#/match/${matchId}">just watch the match</a>.</p></section>`);
    return () => {};
  }

  main.classList.add('wide');
  setHtml(main, html`
    <section class="bb-cockpit">
      <div class="bb-bar">
        <a href="#/blocks" class="bb-back">← My strategies</a>
        <span class="bb-live-pill" id="bb-pill">LIVE</span>
        <h1 class="bb-match-title" id="bb-title">Loading the match…</h1>
        <span class="bb-spacer"></span>
        <span class="small muted" id="bb-running"></span>
        <a class="btn ghost small-btn" href="#/match/${matchId}">Full spectator view</a>
        <button type="button" class="btn ghost small-btn" id="bb-surrender">Give up</button>
      </div>
      <div class="bb-cockpit-grid">
        <div class="bb-stage">
          <div class="bb-stats" id="bb-stats"></div>
          <div class="bb-3d-wrap">
            <div class="map3d bb-3d" id="bb-3d"></div>
            <div class="bb-cams" role="group" aria-label="Camera">
              <button type="button" data-cam="director" aria-pressed="true">Auto camera</button>
              <button type="button" data-cam="army" aria-pressed="false">Follow my army</button>
              <button type="button" data-cam="overview" aria-pressed="false">Whole map</button>
            </div>
            <span class="bb-3d-note">Simple 3D · sound off</span>
            <p class="bb-3d-msg" id="bb-3d-msg">Building the 3D valley…</p>
            <div class="bb-result" id="bb-result" hidden></div>
          </div>
          <p class="small muted">Point at a rule to make the ${say('units', 'badgers')} it's moving pulse. Click a ${say('unit', 'badger')} to ride along with it.</p>
        </div>
        <aside class="bb-side">
          <section aria-labelledby="bb-sw-h">
            <h2 id="bb-sw-h" class="bb-label">Switches · instant</h2>
            <div class="bb-switches" id="bb-switches"></div>
          </section>
          <section class="bb-apply" id="bb-apply" aria-live="polite"></section>
          <section aria-labelledby="bb-ver-h">
            <h2 id="bb-ver-h" class="bb-label">Your versions this match</h2>
            <ol class="bb-versions" id="bb-versions"></ol>
          </section>
        </aside>
      </div>
      <div class="bb-bar bb-edit-bar">
        <h2 class="bb-label">Your rules · edit them, then Apply</h2>
        <span class="bb-spacer"></span>
        <button type="button" class="btn ghost small-btn" id="bb-code" aria-pressed="${!!prefs().code}">${raw(ICON.code)} Python</button>
      </div>
      <div id="bb-editor"></div>
    </section>`);

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
      program: strategy.program,
      onChange: (p) => {
        strategy = { ...strategy, program: { ...p, name: strategy.program.name } };
        putStrategy(strategy);
        renderApply();
      },
      onHover: (i) => { hovered = i; highlight(); },
    });
  }

  const pending = () => strategy && status?.program && !sameProgram(strategy.program, status.program);

  function renderApply() {
    const box = $('bb-apply');
    if (!box || !status) return;
    if (status.matchStatus === 'Completed') { setHtml(box, ''); return; }
    if (pending()) {
      setHtml(box, html`<div class="bb-pending"><p><b>You have changes that aren’t playing yet.</b> They go live on the next tick after you apply them.</p>
        <div class="bb-row-btns"><button type="button" class="btn start" id="bb-apply-btn" ${applying ? 'disabled' : ''}>Apply now</button>
        <button type="button" class="btn ghost" id="bb-undo">Undo my changes</button></div><p class="bb-error" id="bb-apply-err" role="alert"></p></div>`);
    } else {
      setHtml(box, html`<div class="bb-good">${raw(ICON.check)} <span><b>Version ${status.version} is playing.</b> Change a rule below and Apply to send in a new version.</span></div>`);
    }
    editor?.setStatus(status.rules, pending() ? '(running version)' : '');
  }

  function renderSide() {
    if (!status) return;
    const sw = Object.entries(status.switches || {});
    setHtml($('bb-switches'), sw.length
      ? html`${sw.map(([name, on]) => html`<button type="button" class="bb-switch" data-switch="${name}" aria-pressed="${on}">${name}<span>${on ? 'ON' : 'off'}</span></button>`)}`
      : html`<p class="small muted">No switches yet. In a rule, pick a check and add a switch, like “Attack!”, then Apply.</p>`);
    setHtml($('bb-versions'), html`${(status.versions || []).map((v) => html`<li${v.version === status.version ? ' aria-current="true"' : ''}><b>v${v.version}</b> <span class="muted">from tick ${v.tick}</span></li>`)}`);
    const s = status.stats || {};
    setHtml($('bb-stats'), html`
      <span><span class="muted">${ORE[0].toUpperCase() + ORE.slice(1)}</span> <b>${s.grubs ?? 0}</b></span>
      <span><span class="muted">Workers</span> <b>${s.workers ?? 0}</b></span>
      <span><span class="muted">Fighters</span> <b>${s.fighters ?? 0}</b></span>
      <span><span class="muted">Buildings</span> <b>${s.buildings ?? 0}</b></span>
      <span><span class="muted">Score</span> <b>${s.score ?? 0}</b></span>
      <span><span class="muted">Tick</span> <b>${status.tick}</b></span>`);
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
    } else if (summary.status === 'Waiting') {
      setMsg(`Waiting for players: ${summary.players.length} of ${summary.maxPlayers} seats taken. It starts when ${summary.players.length >= summary.maxPlayers ? 'it’s ready' : 'the match fills or its host starts it'}.`);
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
      if (e.status === 404) { setMsg('This match is over and gone from the server.'); return; }
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
      if (e.status === 404) return;
    }
    if (status?.matchStatus !== 'Completed') later(pollStatus, Math.max(400, Math.min(1000, summary?.tickIntervalMs ?? 1000)));
  }

  async function start3d() {
    const box = $('bb-3d');
    try {
      const { View3D } = await import('./3d/view.js');
      if (!alive) return;
      const names = () => new Map((lastState?.players || summary?.players || []).map((p) => [p.slot, displayName(p.name)]));
      view3d = new View3D(box, { badger: SNOW, names, quality: 'low', audio: null });
      view3d.onReady = () => {
        if (!alive) return;
        if (summary?.status !== 'Waiting') setMsg('');
        if (camMode === 'director') view3d.setMode('director');
      };
      view3d.onModeChange = (m) => { if (m !== 'director' && camMode === 'director') setCam(m === 'follow' ? 'army' : 'overview', false); };
    } catch (e) {
      setMsg(`The 3D view couldn’t start here (${e?.message || 'no WebGL'}). Your bot is still playing.`);
    }
  }

  /** A finished match: the last picture, while the server still has it. */
  async function showFinalBoard() {
    if (!view3d) return;
    try {
      view3d.setMap(await api(`/api/v1/matches/${matchId}/map`));
      lastState = await api(`/api/v1/matches/${matchId}/spectate?fog=true`);
      view3d.setState(lastState);
    } catch { /* archived: only the result is left */ }
  }

  async function startStream() {
    if (!view3d) return;
    try {
      const map = await api(`/api/v1/matches/${matchId}/map`);
      if (!alive) return;
      view3d.setMap(map);
      if (status?.slot >= 0) view3d.setVision(status.slot);
    } catch { later(startStream, 2000); return; }
    es = new EventSource(`/api/v1/matches/${matchId}/spectate/stream?fog=true`);
    es.onmessage = (msg) => {
      let s;
      try { s = JSON.parse(msg.data); } catch { return; }
      lastState = s;
      view3d?.setState(s);
      if (camMode === 'army') followArmy(false);
    };
    es.onerror = () => {
      es?.close();
      es = null;
      if (alive && summary?.status !== 'Completed') later(startStream, 2000);
    };
  }

  function myFighters() {
    const slot = status?.slot;
    return (lastState?.units || []).filter((u) => u.owner === slot && u.type !== 'Worker');
  }
  function followArmy(force) {
    if (!view3d) return;
    if (!force && view3d.followed != null && myFighters().some((u) => u.id === view3d.followed)) return;
    const f = myFighters()[0] ?? (lastState?.units || []).find((u) => u.owner === status?.slot);
    if (f) view3d.follow(f.id);
  }
  function setCam(mode, apply = true) {
    camMode = mode;
    main.querySelectorAll('[data-cam]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.cam === mode)));
    if (!apply || !view3d) return;
    if (mode === 'director') view3d.setMode('director');
    else if (mode === 'overview') { view3d.setMode('orbit'); view3d.overview(); }
    else followArmy(true);
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
    else if (b.dataset.cam) setCam(b.dataset.cam);
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
    main.removeEventListener('click', onClick);
    main.classList.remove('wide');
  };
}

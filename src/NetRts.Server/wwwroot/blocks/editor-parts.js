// What both rule editors (the cards in rule-editor.js and the blocks in block-editor.js) show for a piece of
// a program: its words and inputs, the choices for adding one, the limits, and the bits around a rule
// (on/off, Save and Cancel, notes). Each editor wraps these in its own look.
import { html, raw } from '../util.js';
import { say, upgradeName, ORE } from '../lore.js';
import { ICON } from './icons.js';
import {
  C, A, LIMITS, knows, canCombine, condDepth, condHeight, byDefault,
  OPS, UNITS, FIGHTERS, BUILDINGS, UPGRADES, WHAT, WHO, SITES, TARGETS, SCOUT_TARGETS, EXPAND_BUILDINGS, VOCAB,
  ENEMY_WHAT, ATTACKABLE, ON_OFF, unitWord, bWord, upgradeInfo,
} from './program.js';

// ------------------------------------------------------------------ limits

// Where a new step or rule would go past the server's limits (GET /api/v1/blocks/vocabulary), the editor
// refuses it and says why.
export const fullRules = (p) => p.rules.length >= LIMITS().maxRules;
export const fullRule = (rule) => (rule?.do?.length ?? 0) >= LIMITS().maxActionsPerRule;
export const fullSwitches = (p) => p.switches.length >= LIMITS().maxSwitches;
export const why = {
  rules: () => `A strategy has at most ${LIMITS().maxRules} rules.`,
  steps: () => `A rule does at most ${LIMITS().maxActionsPerRule} things.`,
  switches: () => `A strategy has at most ${LIMITS().maxSwitches} switches.`,
  depth: () => `Checks go at most ${LIMITS().maxDepth} deep, so this one can't be combined further.`,
};
/** Whether check could sit at path without nesting deeper than the server allows. */
export const fitsAt = (check, path) => condDepth(path) + condHeight(check) <= LIMITS().maxDepth;

/** The rule a path is in: 'rules.3.do.1' -> 3. */
export const ruleOf = (path) => Number(String(path).split('.')[1]);

// ------------------------------------------------------------------ inputs

/** A dropdown for one field of the block at path. A value the list doesn't offer (from an older file) still shows as itself. */
export function select(path, field, value, options, label, extra = {}) {
  const k = `${path}|${field}|${extra.i ?? ''}`;
  if (value != null && !options.some(([v]) => String(v) === String(value))) options = [...options, [value, String(value)]];
  return html`<select class="bb-select" data-path="${path}" data-field="${field}" data-k="${k}" ${extra.i != null ? html`data-i="${extra.i}"` : ''} aria-label="${label}">
    ${options.map(([v, t]) => html`<option value="${v}" ${String(v) === String(value) ? 'selected' : ''}>${t}</option>`)}
  </select>`;
}

const number = (path, value, max, label, field = 'value', min = 0) =>
  html`<input class="bb-num-in" type="number" min="${min}" max="${max}" inputmode="numeric" value="${value}" data-path="${path}" data-field="${field}" data-k="${path}|${field}|" aria-label="${label}">`;

/** A program's switches, for a dropdown (one it names that's gone still shows as itself). */
const switchChoices = (program, name) => (program?.switches?.length ? program.switches : [name]).map((s) => [s, s]);

/**
 * How a block's inputs are drawn: LIVE as the dropdowns and number boxes of a placed block; SHOWN, for a
 * choice not placed yet (the palette, the picker), as {its value}, so it's clear that part can be changed.
 */
const LIVE = { select, number, live: true };
const slot = (text) => html`<span class="bb-slot"><span aria-hidden="true">{</span>${text}<span aria-hidden="true">}</span></span> `;
const SHOWN = {
  // Just the name: a class's dropdown also says what it does ("Intro to Chemistry: +2 damage").
  select: (path, field, value, options) => slot(String(options.find(([v]) => String(v) === String(value))?.[1] ?? value).split(': ')[0]),
  number: (path, value) => slot(value),
  live: false,
};

/**
 * A single check's words and inputs ("minute", [≥], [3]). And, or and not are made of other checks, so
 * each editor draws those itself.
 */
export function condParts(program, c, path, { select, number } = LIVE) {
  switch (c?.kind) {
    case 'count': return { label: '', fields: html`${select(path, 'what', c.what, WHAT(), 'What to count')}${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, LIMITS().maxNumber, 'Number')}` };
    case 'grubsLeft': return { label: `${ORE} left near my base`, fields: html`${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, LIMITS().maxNumber, 'Number')}` };
    case 'minute': return { label: 'minute', fields: html`${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, 999, 'Minute')}` };
    case 'switch': return {
      label: 'switch',
      kind: 'switch',
      fields: html`${select(path, 'name', c.name, switchChoices(program, c.name), 'Which switch')} <span>is on</span>`,
    };
    case 'enemies': return { label: 'enemy', fields: html`${select(path, 'what', c.what, ENEMY_WHAT(), 'Which enemies')} <span>I can see</span> ${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, LIMITS().maxNumber, 'Number')}` };
    case 'underAttack': return { label: '', fields: html`${select(path, 'what', c.what, ATTACKABLE(), 'What is under attack')} <span>under attack</span>` };
    case 'hqHealth': return { label: `my ${bWord('CommandCenter')}'s health`, fields: html`${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, 100, 'Percent')} <span>%</span>` };
    case 'enemyNearBase': return { label: 'an enemy is near my base', fields: '' };
    case 'seeEnemy': return { label: 'I can see an enemy', fields: '' };
    case 'always': return { label: 'Every tick', fields: '' };
    default: return { label: String(c?.kind ?? '?'), fields: '' };
  }
}

/** Which colour family an action belongs to: train, build, army or switch (as the choices group them). */
export const actionFamily = (kind) => (['train', 'trainMix'].includes(kind) ? 'train'
  : ['build', 'research', 'expand'].includes(kind) ? 'build' : kind === 'setSwitch' ? 'switch' : 'army');

/** An action's words and inputs ("attack", [the enemy base], "with", [all fighters]). */
export function actionParts(program, a, path, { select, number, live } = LIVE) {
  const parts = (label, fields) => ({ label, fields, family: actionFamily(a.kind) });
  switch (a.kind) {
    case 'train': return parts('train a', select(path, 'unit', a.unit, UNITS().map((t) => [t, unitWord(t)]), 'Which unit'));
    case 'trainMix': return parts('train in turn', html`
      ${(a.units || []).map((u, i) => html`${select(path, 'units', u, FIGHTERS().map((t) => [t, unitWord(t)]), `Unit ${i + 1} of the mix`, { i })}${live && a.units.length > 1 ? html`<button type="button" class="bb-icon small" data-act="mix-del" data-path="${path}" data-i="${i}" data-k="mixdel|${path}|${i}" aria-label="Remove unit ${i + 1} from the mix">−</button>` : ''}`)}
      ${live && a.units.length < LIMITS().maxMixUnits ? html`<button type="button" class="bb-icon small" data-act="mix-add" data-path="${path}" data-k="mixadd|${path}" aria-label="Add a unit to the mix">+</button>` : ''}`);
    case 'build': return parts('build a', html`${select(path, 'building', a.building, BUILDINGS().map((b) => [b, bWord(b)]), 'Which building')}${select(path, 'site', a.site ?? byDefault('site'), SITES(), 'Where')}`);
    case 'research': return parts(say('research', 'take the class'), select(path, 'upgrade', a.upgrade, UPGRADES().map((u) => { const info = upgradeInfo(u); return [u, info ? `${upgradeName(u)}: ${info.short}` : upgradeName(u)]; }), 'Which upgrade'));
    case 'attack': return parts('attack', html`${select(path, 'target', a.target ?? byDefault('target'), TARGETS(), 'Attack what')}<span>with</span> ${number(path, a.percent ?? 100, 100, 'Percent of them to send', 'percent', 1)} <span>% of</span> ${select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who attacks')}`);
    case 'setSwitch': return parts('turn', html`${select(path, 'name', a.name, switchChoices(program, a.name), 'Which switch')}${select(path, 'on', String(a.on !== false), ON_OFF(), 'On or off')}`);
    case 'defend': return parts('defend with', select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who defends'));
    case 'retreat': return parts('bring home', select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who comes home'));
    case 'scout': return parts('send a scout to', select(path, 'target', a.target ?? byDefault('scoutTarget'), SCOUT_TARGETS(), 'Where the scout goes'));
    case 'expand': return parts(`expand to new ${ORE} with a`, select(path, 'building', a.building ?? byDefault('expandBuilding'), EXPAND_BUILDINGS(), 'What to build there'));
    default: return parts(a.kind, '');
  }
}

// ------------------------------------------------------------------ choices

/**
 * The blocks there are to pick from, in groups: the cards' picker and the blocks' palette both show these.
 * Each item is { label, data } where data is what choiceNode turns into a block. Only kinds the server
 * knows are included, and a group with none left is dropped.
 */
export function choiceGroups(type, program) {
  // Most choices are labelled as the block they make, with {braces} on what can be changed once it's placed.
  const shown = (data) => {
    const node = choiceNode(type, data);
    const { label, fields } = type === 'cond' ? condParts(program, node, '', SHOWN) : actionParts(program, node, '', SHOWN);
    return html`${label ? `${label} ` : ''}${fields}`;
  };
  const item = (label, data) => (knows(type, data.kind) ? { label: label ?? shown(data), data } : null);
  const group = (name, items, extra = {}) => ({ name, items: items.filter(Boolean), ...extra });
  const groups = type === 'cond'
    ? [
      group('My stuff', [
        item(null, { kind: 'count', what: 'fighters', op: '>=', value: 8 }),
        item(null, { kind: 'count', what: 'workers', op: '<', value: 12 }),
        item(null, { kind: 'count', what: 'grubs', op: '>=', value: 150 }),
        item(null, { kind: 'count', what: 'Barracks', op: '<', value: 2 }),
        item(null, { kind: 'grubsLeft' }),
      ]),
      group('The enemy', [item(null, { kind: 'enemyNearBase' }), item(null, { kind: 'seeEnemy' }),
        item(null, { kind: 'enemies', what: 'fighters', op: '>=', value: 5 }),
        item(null, { kind: 'underAttack', what: 'units' }), item(null, { kind: 'underAttack', what: 'workers' }),
        item(null, { kind: 'underAttack', what: 'CommandCenter' }), item(null, { kind: 'hqHealth' })]),
      group('Time', [item(null, { kind: 'minute' }), item('every tick', { kind: 'always' })]),
      knows('cond', 'switch') ? group('Switches', program.switches.map((s) => item(null, { kind: 'switch', name: s })), { switches: true }) : null,
      group('Combine', [item('both this and…', { kind: 'and' }), item('either this or…', { kind: 'or' }), item('not this', { kind: 'not' })], { combine: true }),
    ]
    : [
      group('Train', UNITS().map((t) => item(null, { kind: 'train', unit: t })).concat([item(null, { kind: 'trainMix' })])),
      group('Build', BUILDINGS().filter((b) => b !== 'CommandCenter')   // a new HQ is an expansion
        .map((b) => item(null, { kind: 'build', building: b }))
        .concat([item(null, { kind: 'research' }), item(null, { kind: 'expand' })])),
      group('Army', [item(null, { kind: 'attack' }), item(null, { kind: 'attack', percent: 50 }), item(null, { kind: 'defend' }), item(null, { kind: 'retreat' }),
        item(null, { kind: 'scout', target: 'enemyBase' }), item(null, { kind: 'scout', target: 'explore' })]),
      // Turning switches on and off goes with the army orders in the palette (tab: which tab shows a group).
      knows('action', 'setSwitch') ? group('Switches', program.switches.flatMap((s) => [
        item(null, { kind: 'setSwitch', name: s, on: true }), item(null, { kind: 'setSwitch', name: s, on: false })]), { switches: true, tab: 'Army' }) : null,
    ];
  return groups.filter((g) => g && (g.items.length || g.switches));
}

/** A choice's data as a block. And, or and not wrap current, the check they replace. */
export function choiceNode(type, d, current) {
  if (type === 'cond') {
    switch (d.kind) {
      case 'and': case 'or': return C[d.kind](current ?? C.always(), C.count('fighters', '>=', 8));
      case 'not': return C.not(current ?? C.always());
      case 'count': return C.count(d.what, d.op, Number(d.value));
      case 'switch': return C.switch(d.name);
      case 'enemies': return C.enemies(d.what, d.op, Number(d.value));
      case 'underAttack': return C.underAttack(d.what);
      default: return C[d.kind]();
    }
  }
  if (d.kind === 'setSwitch') return A.setSwitch(d.name, String(d.on) !== 'false');
  if (d.kind === 'attack') return A.attack(undefined, undefined, d.percent ? Number(d.percent) : undefined);
  // A depot goes near the grubs and a tower toward the enemy; anything else where the server puts it.
  const site = { ResourceDepot: 'NearOre', GuardTower: 'TowardEnemy' }[d.building];
  return d.kind === 'train' ? A.train(d.unit)
    : d.kind === 'scout' ? A.scout(d.target)
      : d.kind === 'build' ? A.build(d.building, VOCAB.sites.includes(site) ? site : undefined)
        : A[d.kind]();
}

/** Whether a combining choice (and, or, not) may wrap the check at path. */
export const canWrap = (program, path, kind) => !['and', 'or', 'not'].includes(kind) || canCombine(program, path);

/** A choice's data as data-* attributes, for a button that picks it. */
export const dataAttrs = (data) => html`${Object.entries(data).map(([k, v]) => html`data-${k}="${v}" `)}`;

// ------------------------------------------------------------------ around a rule

/** Waiting buttons: while a rule is being edited, the one-click changes wait for its Save or Cancel. */
export const waitAttr = (ed) => raw(ed.session || ed.saving ? 'disabled title="Save or cancel your change first"' : '');

/** A rule's on/off switch and delete button. */
export function ruleTools(ed, i, off) {
  return html`
    <button type="button" class="bb-switch-toggle" role="switch" data-act="rule-toggle" data-i="${i}" data-k="toggle|${i}" aria-checked="${String(!off)}" aria-label="Rule ${i + 1}" title="${off ? 'Off: this rule is skipped. Click to turn it on.' : 'On: click to turn this rule off.'}" ${waitAttr(ed)}><span class="bb-switch-track" aria-hidden="true"><span class="bb-switch-knob"></span></span><span class="bb-switch-text">${off ? 'Off' : 'On'}</span></button>
    <button type="button" class="bb-icon" data-act="rule-del" data-i="${i}" data-k="del|${i}" aria-label="Delete rule ${i + 1}" ${waitAttr(ed)}>×</button>`;
}

/**
 * The controls that float over the top of the rules, in both styles: undo and redo (EditorCore keeps the
 * history; Ctrl+Z, Ctrl+Shift+Z) on the left; on the right how the rules look: the style (editors.js passes
 * its Cards | Blocks buttons in as ed.styleToggle) and the Python beside each rule.
 */
export const canvasControlsHtml = (ed) => html`
  <div class="bb-float-row">
    <div class="bb-pill" role="group" aria-label="Undo and redo">
      <button type="button" class="bb-pill-opt icon" data-act="undo" data-k="undo" aria-label="Undo" title="Undo (Ctrl+Z)" ${ed.canUndo ? '' : 'disabled'}>${raw(ICON.undo)}</button>
      <button type="button" class="bb-pill-opt icon" data-act="redo" data-k="redo" aria-label="Redo" title="Redo (Ctrl+Shift+Z)" ${ed.canRedo ? '' : 'disabled'}>${raw(ICON.redo)}</button>
    </div>
    <div class="bb-pill" role="group" aria-label="How your rules look">
      ${ed.styleToggle?.() ?? ''}
      <span class="bb-pill-sep" aria-hidden="true"></span>
      <button type="button" class="bb-pill-opt" data-act="code" data-k="code" aria-pressed="${String(ed.code)}" title="Show each rule as Python too">${raw(ICON.code)} Python</button>
    </div>
  </div>`;

/** How often rule i may run: [every tick | only once | at most every [30] s] (EditorCore's change() sets it). */
export function repeatHtml(r, i) {
  const how = r.once ? 'once' : r.everySeconds ? 'every' : 'tick';
  return html`<span class="bb-repeat">
    ${select(`rules.${i}`, 'repeat', how, [['tick', 'every tick'], ['once', 'only once'], ['every', 'at most every']], `How often rule ${i + 1} may run`)}
    ${how === 'every' ? html`${number(`rules.${i}`, r.everySeconds, LIMITS().maxEverySeconds, `Seconds between runs of rule ${i + 1}`, 'everySeconds', 1)}<span>s</span>` : ''}
  </span>`;
}

/** The "+ rule" line above rule i (above none once the rules are full): a new rule goes in there. */
export const insertRuleHtml = (program, i) => (fullRules(program) ? '' : html`
  <button type="button" class="bb-insert" data-act="insert-rule" data-i="${i}" data-k="insert|${i}" aria-label="Add a rule before rule ${i + 1}" title="Add a rule here"><span>+ rule</span></button>`);

/** Under a rule: what its classes do, what it's doing in the match, and Save and Cancel while it's being edited. */
export function ruleFooter(ed, r, i) {
  return html`
    ${(r.do || []).filter((a) => a.kind === 'research').map((a) => {
      const info = upgradeInfo(a.upgrade);
      return info ? html`<p class="bb-note"><b>${upgradeName(a.upgrade)}</b>: ${info.detail}. <span class="muted">${info.meta}</span></p>` : '';
    })}
    <p class="bb-live" hidden></p>
    ${ed.session?.index === i ? html`
      <div class="bb-save-row">
        <button type="button" class="btn start small-btn" data-act="save-rule" data-k="save|${i}" ${ed.saving ? 'disabled' : ''}>${ed.saving ? 'Saving…' : 'Save'}</button>
        <button type="button" class="btn ghost small-btn" data-act="cancel-rule" data-k="cancel|${i}" ${ed.saving ? 'disabled' : ''}>Cancel</button>
        <span class="small muted">${ed.session.inserted ? 'New rule: it plays once you save it.' : 'Your change plays once you save it.'}</span>
        ${ed.saveError ? html`<span class="bb-error" role="alert">${ed.saveError}</span>` : ''}
      </div>` : ''}`;
}

/** The program's switches, each with a delete button. */
export const switchListHtml = (program) => (program.switches.length
  ? html`<ul class="bb-switch-list">${program.switches.map((s) => html`<li>${s} <button type="button" class="bb-icon small" data-act="del-switch" data-name="${s}" aria-label="Delete the ${s} switch">×</button></li>`)}</ul>` : '');

/** The box for a new switch (or why there can't be one). */
export const newSwitchHtml = (program) => (fullSwitches(program) ? html`<p class="small muted">${why.switches()}</p>` : html`
  <span class="bb-new-switch"><label class="visually-hidden" for="bb-new-switch">New switch name</label><input id="bb-new-switch" type="text" maxlength="${LIMITS().maxSwitchNameLength}" placeholder="New switch, e.g. Attack!"><button type="button" class="bb-opt" data-act="new-switch">Add</button></span>`);

/** The note under the rules on how they run. */
export const howRulesRun = () => html`<p class="small muted">Rules are checked top to bottom, every tick. Higher rules get first pick of ${ORE} and fighters.
  Two things happen on their own: idle workers dig ${ORE}, and half-built buildings get finished.</p>`;


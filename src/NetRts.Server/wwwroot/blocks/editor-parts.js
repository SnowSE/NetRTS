// What both rule editors (the cards in rule-editor.js and the blocks in block-editor.js) show for a piece of
// a program: its words and inputs, the choices for adding one, the limits, and the bits around a rule
// (on/off, Save and Cancel, notes). Each editor wraps these in its own look.
import { html, raw } from '../util.js';
import { say, upgradeName, ORE } from '../lore.js';
import {
  C, A, LIMITS, knows, canCombine, condDepth, condHeight, byDefault,
  OPS, UNITS, FIGHTERS, BUILDINGS, UPGRADES, WHAT, WHO, SITES, TARGETS, SCOUT_TARGETS, EXPAND_BUILDINGS, VOCAB,
  unitWord, bWord, upgradeInfo,
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

const number = (path, value, max, label) =>
  html`<input class="bb-num-in" type="number" min="0" max="${max}" inputmode="numeric" value="${value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="${label}">`;

/**
 * A single check's words and inputs ("minute", [≥], [3]). And, or and not are made of other checks, so
 * each editor draws those itself.
 */
export function condParts(program, c, path) {
  switch (c?.kind) {
    case 'count': return { label: '', fields: html`${select(path, 'what', c.what, WHAT(), 'What to count')}${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, LIMITS().maxNumber, 'Number')}` };
    case 'grubsLeft': return { label: `${ORE} left near my base`, fields: html`${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, LIMITS().maxNumber, 'Number')}` };
    case 'minute': return { label: 'minute', fields: html`${select(path, 'op', c.op, OPS(), 'Comparison')}${number(path, c.value, 999, 'Minute')}` };
    case 'switch': return {
      label: 'switch',
      kind: 'switch',
      fields: html`${select(path, 'name', c.name, (program.switches.length ? program.switches : [c.name]).map((s) => [s, s]), 'Which switch')}<span>is on</span>`,
    };
    case 'enemyNearBase': return { label: 'an enemy is near my base', fields: '' };
    case 'seeEnemy': return { label: 'I can see an enemy', fields: '' };
    case 'always': return { label: 'Every tick', fields: '' };
    default: return { label: String(c?.kind ?? '?'), fields: '' };
  }
}

/** Which colour family an action belongs to: train, build or army (as the choices group them). */
export const actionFamily = (kind) => (['train', 'trainMix'].includes(kind) ? 'train'
  : ['build', 'research', 'expand'].includes(kind) ? 'build' : 'army');

/** An action's words and inputs ("attack", [the enemy base], "with", [all fighters]). */
export function actionParts(a, path) {
  const parts = (label, fields) => ({ label, fields, family: actionFamily(a.kind) });
  switch (a.kind) {
    case 'train': return parts('train a', select(path, 'unit', a.unit, UNITS().map((t) => [t, unitWord(t)]), 'Which unit'));
    case 'trainMix': return parts('train in turn', html`
      ${(a.units || []).map((u, i) => html`${select(path, 'units', u, FIGHTERS().map((t) => [t, unitWord(t)]), `Unit ${i + 1} of the mix`, { i })}${a.units.length > 1 ? html`<button type="button" class="bb-icon small" data-act="mix-del" data-path="${path}" data-i="${i}" data-k="mixdel|${path}|${i}" aria-label="Remove unit ${i + 1} from the mix">−</button>` : ''}`)}
      ${a.units.length < LIMITS().maxMixUnits ? html`<button type="button" class="bb-icon small" data-act="mix-add" data-path="${path}" data-k="mixadd|${path}" aria-label="Add a unit to the mix">+</button>` : ''}`);
    case 'build': return parts('build a', html`${select(path, 'building', a.building, BUILDINGS().map((b) => [b, bWord(b)]), 'Which building')}${select(path, 'site', a.site ?? byDefault('site'), SITES(), 'Where')}`);
    case 'research': return parts(say('research', 'take the class'), select(path, 'upgrade', a.upgrade, UPGRADES().map((u) => { const info = upgradeInfo(u); return [u, info ? `${upgradeName(u)}: ${info.short}` : upgradeName(u)]; }), 'Which upgrade'));
    case 'attack': return parts('attack', html`${select(path, 'target', a.target ?? byDefault('target'), TARGETS(), 'Attack what')}<span>with</span>${select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who attacks')}`);
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
  const item = (label, data) => (knows(type, data.kind) ? { label, data } : null);
  const group = (name, items, extra = {}) => ({ name, items: items.filter(Boolean), ...extra });
  const groups = type === 'cond'
    ? [
      group('My stuff', [
        item('fighters ≥ 8', { kind: 'count', what: 'fighters', op: '>=', value: 8 }),
        item('workers < 12', { kind: 'count', what: 'workers', op: '<', value: 12 }),
        item(`${ORE} ≥ 150`, { kind: 'count', what: 'grubs', op: '>=', value: 150 }),
        item(`fewer than 2 ${bWord('Barracks')}`, { kind: 'count', what: 'Barracks', op: '<', value: 2 }),
        item(`${ORE} left near my base ≤ 300`, { kind: 'grubsLeft' }),
      ]),
      group('The enemy', [item('an enemy is near my base', { kind: 'enemyNearBase' }), item('I can see an enemy', { kind: 'seeEnemy' })]),
      group('Time', [item('minute ≥ 3', { kind: 'minute' }), item('every tick', { kind: 'always' })]),
      knows('cond', 'switch') ? group('Switches', program.switches.map((s) => item(`${s} is on`, { kind: 'switch', name: s })), { switches: true }) : null,
      group('Combine', [item('both this and…', { kind: 'and' }), item('either this or…', { kind: 'or' }), item('not this', { kind: 'not' })], { combine: true }),
    ]
    : [
      group('Train', UNITS().map((t) => item(unitWord(t), { kind: 'train', unit: t })).concat([item('a mix, in turn', { kind: 'trainMix' })])),
      group('Build', BUILDINGS().filter((b) => b !== 'CommandCenter')   // a new HQ is an expansion
        .map((b) => item(bWord(b), { kind: 'build', building: b }))
        .concat([item(say('research an upgrade', 'take a class'), { kind: 'research' }), item(`expand to new ${ORE}`, { kind: 'expand' })])),
      group('Army', [item('attack', { kind: 'attack' }), item('defend', { kind: 'defend' }), item('bring home', { kind: 'retreat' }),
        item('send a scout to the enemy', { kind: 'scout', target: 'enemyBase' }), item(`explore for more ${ORE}`, { kind: 'scout', target: 'explore' })]),
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
      default: return C[d.kind]();
    }
  }
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


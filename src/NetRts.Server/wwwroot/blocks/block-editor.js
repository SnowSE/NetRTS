import { html, raw } from '../util.js';
import { at, R, C } from './program.js';
import { pyRule } from './python.js';
import { ICON } from './icons.js';
import { EditorCore } from './editor-core.js';
import { pointerDrag, dropSpot, slotAmong } from './drag.js';
import {
  fullRule, fullRules, why, ruleOf, select, condParts, actionParts, choiceGroups, choiceNode, canWrap, fitsAt,
  dataAttrs, waitAttr, ruleTools, ruleFooter, insertRuleHtml, switchListHtml, newSwitchHtml, howRulesRun,
} from './editor-parts.js';

/** The palette's tabs: the checks, then the action groups by name (as choiceGroups names them). */
const TABS = [
  { id: 'cond', label: 'Checks' },
  { id: 'Train', label: 'Train' },
  { id: 'Build', label: 'Build' },
  { id: 'Army', label: 'Army' },
];
const CHOICE_KEYS = ['kind', 'what', 'op', 'value', 'name', 'unit', 'building', 'target'];

/**
 * Snap-together blocks: a palette of coloured blocks to drag into rules, each rule a "when ⟨check⟩ do"
 * block holding a stack of actions. Blocks drag by mouse, pen or finger; a block dropped back on the
 * palette is removed. For keyboards (and quick taps), a palette block clicked goes into the selected
 * rule, a focused step moves with ↑ ↓ and goes with Delete. The editing itself is EditorCore's.
 */
export class BlockEditor extends EditorCore {
  constructor(root, opts) {
    super(root, opts);
    this.tab = 'cond';
    this.palOpen = true;    // the palette's chevron folds it down to its title (it stays a place to drop blocks to remove)
    this.selected = null;   // the rule a tapped palette block goes into (null: the last one)
    this.dragging = null;
    this.listen(root, 'keydown', (e) => this.key(e));
    this.listen(root, 'pointerdown', (e) => this.selectFrom(e.target));
    this.listen(root, 'focusin', (e) => this.selectFrom(e.target));
    this.listeners.push(pointerDrag(root, {
      pick: (el) => (this.saving ? null : this.payload(el)),
      over: (payload, x, y) => this.over(payload, x, y),
      drop: (payload, target) => this.drop(payload, target),
      onStart: (payload) => { this.dragging = payload; root.querySelector('.bb-blocks')?.classList.add('is-dragging', `drag-${payload.type}`); },
      onEnd: () => { this.dragging = null; root.querySelector('.bb-blocks')?.classList.remove('is-dragging', 'drag-rule', 'drag-step', 'drag-cond', 'drag-new-rule', 'drag-new-cond', 'drag-new-action'); },
      covered: () => this.paletteCover(),
      layer: () => root.querySelector('.bb-blocks'),   // the block colours are set there
    }));
    this.render();
  }

  get target() {
    const n = this.program.rules.length;
    return n ? Math.min(this.selected ?? n - 1, n - 1) : null;
  }

  selectFrom(el) {
    const script = el.closest?.('.bb-script');
    if (!script || this.dragging) return;
    const i = Number(script.dataset.rule);
    if (i === this.selected) return;
    this.selected = i;
    this.root.querySelectorAll('.bb-script').forEach((s) => s.classList.toggle('selected', Number(s.dataset.rule) === i));
    const hint = this.root.querySelector('#bb-pal-hint');
    if (hint) hint.textContent = this.hint();
  }

  /** In one column the pinned palette sits over the top of the rules: where they show from (else 0). */
  paletteCover() {
    const pal = this.root.querySelector('.bb-palette')?.getBoundingClientRect();
    const canvas = this.root.querySelector('.bb-canvas')?.getBoundingClientRect();
    return pal && canvas && pal.right > canvas.left && pal.left < canvas.right ? pal.bottom : 0;
  }

  hint() { return this.target == null ? 'Drag a rule block in to start.' : `Drag blocks into a rule, or tap one to add it to rule ${this.target + 1}.`; }

  // ---------------------------------------------------------------- dragging

  /** What dragging el carries. */
  payload(el) {
    const d = el.dataset;
    switch (d.drag) {
      case 'palette': {
        const data = Object.fromEntries(CHOICE_KEYS.filter((k) => d[k] != null).map((k) => [k, d[k]]));
        return { type: `new-${d.ptype}`, data };
      }
      case 'step': return { type: 'step', path: d.path };
      case 'cond': return { type: 'cond', path: d.path };
      case 'rule': return { type: 'rule', index: Number(d.i) };
      default: return null;
    }
  }

  /** Where a drop at x, y would go, and how to mark it. */
  over(payload, x, y) {
    const old = !payload.type.startsWith('new-');
    const trash = old && dropSpot(x, y, '[data-drop-trash]');
    if (trash) return { el: trash, cls: 'drop-trash', trash: true };
    if (payload.type === 'rule' || payload.type === 'new-rule') {
      const list = dropSpot(x, y, '[data-drop-rules]');
      if (!list) return null;
      const rows = [...list.querySelectorAll(':scope > .bb-row')];
      const j = slotAmong(rows, y);
      if (payload.type === 'rule' && (j === payload.index || j === payload.index + 1)) return null;
      return { el: rows[j] ?? list.querySelector('.bb-rules-end'), cls: 'drop-above', j };
    }
    if (payload.type === 'step' || payload.type === 'new-action') {
      // Anywhere on a rule (or in the gap next to one) goes into its stack, at the step nearest the pointer.
      const stack = this.ruleAt(x, y)?.querySelector('[data-drop-stack]');
      if (!stack) return null;
      const rulePath = stack.dataset.dropStack;
      const steps = [...stack.querySelectorAll(':scope > [data-step]')];
      const j = slotAmong(steps, y);
      const sameList = payload.path?.startsWith(`${rulePath}.do.`);
      if (sameList) {
        const from = Number(payload.path.split('.').pop());
        if (j === from || j === from + 1) return null;
      } else if (fullRule(at(this.program, rulePath))) return { el: stack, cls: 'drop-no' };
      return { el: steps[j] ?? stack.querySelector('.bb-mouth-end'), cls: 'drop-above', rulePath, j };
    }
    // A check: onto a check's spot, which it replaces (and/or/not wrap it). Anywhere else on a rule's top
    // means its whole "when", unless that's and/or/not, which a single check shouldn't wipe out by a near miss.
    const hatWhen = dropSpot(x, y, '.bb-hat')?.querySelector(':scope > [data-drop-cond]');
    const wraps = ['and', 'or', 'not'].includes(payload.data?.kind);
    const slot = dropSpot(x, y, '[data-drop-cond]')
      ?? (hatWhen && (wraps || !['and', 'or', 'not'].includes(at(this.program, hatWhen.dataset.dropCond)?.kind)) ? hatWhen : null);
    if (!slot) return null;
    const path = slot.dataset.dropCond;
    if (payload.type === 'cond') {
      if (path === payload.path || path.startsWith(`${payload.path}.`) || payload.path.startsWith(`${path}.`)) return null;
      return fitsAt(at(this.program, payload.path), path) ? { el: slot, cls: 'drop-into', path } : { el: slot, cls: 'drop-no' };
    }
    return canWrap(this.program, path, payload.data.kind) ? { el: slot, cls: 'drop-into', path } : { el: slot, cls: 'drop-no' };
  }

  /** The rule under x, y, or in the rules' column, the one nearest it (the gaps between them count). */
  ruleAt(x, y) {
    const hit = dropSpot(x, y, '.bb-script');
    if (hit) return hit;
    if (!dropSpot(x, y, '[data-drop-rules]')) return null;
    let best = null;
    let gap = Infinity;
    for (const s of this.root.querySelectorAll('.bb-script')) {
      const r = s.getBoundingClientRect();
      const d = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0;
      if (d < gap) { gap = d; best = s; }
    }
    return gap <= 40 ? best : null;
  }

  drop(payload, t) {
    if (t.cls === 'drop-no') return;
    if (t.trash) {
      if (payload.type === 'step') this.removeAction(payload.path);
      else if (payload.type === 'cond') this.removeCond(payload.path);
      else if (payload.type === 'rule') this.structural((p) => { p.rules.splice(payload.index, 1); });
      return;
    }
    switch (payload.type) {
      case 'new-rule': this.addRule(t.j); return;
      case 'rule': this.moveRule(payload.index, t.j > payload.index ? t.j - 1 : t.j); this.selected = null; return;
      case 'new-action': this.insertAction(t.rulePath, t.j, choiceNode('action', payload.data)); this.selected = ruleOf(t.rulePath); return;
      case 'step': this.moveAction(payload.path, t.rulePath, t.j); this.selected = ruleOf(t.rulePath); return;
      case 'new-cond': this.placeCond(t.path, choiceNode('cond', payload.data, at(this.program, t.path))); this.selected = ruleOf(t.path); return;
      case 'cond': this.moveCond(payload.path, t.path); return;
      default:
    }
  }

  /** A new, empty rule ("every tick", nothing to do yet) at index, selected so taps fill it. */
  addRule(index = this.program.rules.length) {
    if (!this.insertRule(index, R(C.always()))) return;
    this.selected = index;
    this.render();
    this.root.querySelector(`.bb-script[data-rule="${index}"]`)?.scrollIntoView({ block: 'nearest' });
  }

  // ---------------------------------------------------------------- clicks and keys

  act(act, b) {
    switch (act) {
      case 'pal-tab': this.tab = b.dataset.tab; this.render(); this.root.querySelector(`[data-tab="${this.tab}"]`)?.focus(); return;
      case 'pal-toggle': this.palOpen = !this.palOpen; this.render(); this.root.querySelector('[data-act="pal-toggle"]')?.focus(); return;
      case 'add-rule': this.addRule(); return;
      case 'insert-rule': this.addRule(Number(b.dataset.i)); return;
      case 'palette': this.tap(this.payload(b)); return;
      default: super.act(act, b);
    }
  }

  /** A palette block tapped (or pressed with Enter): into the selected rule. */
  tap(payload) {
    if (payload.type === 'new-rule') { this.addRule(); return; }
    const i = this.target;
    if (i == null) { this.addRule(); return; }
    if (payload.type === 'new-action') this.insertAction(`rules.${i}`, this.program.rules[i].do.length, choiceNode('action', payload.data));
    else {
      const path = `rules.${i}.when`;
      if (canWrap(this.program, path, payload.data.kind)) this.placeCond(path, choiceNode('cond', payload.data, at(this.program, path)));
    }
  }

  key(e) {
    const el = e.target;
    if (el !== e.target.closest?.('[data-drag]')) return;   // not while typing in a block's box
    const { path } = el.dataset;
    if (el.dataset.drag === 'step' && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) {
      e.preventDefault();
      const j = Number(path.split('.').pop());
      const rulePath = path.replace(/\.do\.\d+$/, '');
      const to = e.key === 'ArrowUp' ? j - 1 : j + 2;
      if (to < 0 || to > at(this.program, rulePath).do.length) return;
      if (this.moveAction(path, rulePath, to)) this.root.querySelector(`[data-k="${CSS.escape(`step|${rulePath}.do.${e.key === 'ArrowUp' ? j - 1 : j + 1}`)}"]`)?.focus();
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      if (el.dataset.drag === 'step') { e.preventDefault(); this.removeAction(path); } else if (el.dataset.drag === 'cond') { e.preventDefault(); this.removeCond(path); }
    }
  }

  // ---------------------------------------------------------------- drawing

  html() {
    const p = this.program;
    return html`
      <div class="bb-blocks${this.code ? ' with-code' : ''}${this.compact ? ' compact' : ''}${this.session ? ' locked' : ''}${this.palOpen ? '' : ' pal-closed'}">
        ${this.paletteHtml()}
        <div class="bb-canvas">
          ${this.saveError && !this.session ? html`<p class="bb-error" role="alert">${this.saveError}</p>` : ''}
          <div class="bb-scripts" data-drop-rules>
            ${p.rules.length ? '' : html`<p class="bb-empty">No rules yet. Drag a <b>when … do</b> block here to start one.</p>`}
            ${p.rules.map((r, i) => html`${insertRuleHtml(p, i)}${this.ruleHtml(r, i)}`)}
            <div class="bb-rules-end" aria-hidden="true"></div>
          </div>
          <button type="button" class="bb-add-rule" data-act="add-rule" data-k="add-rule" ${fullRules(p) ? html`disabled aria-describedby="bb-full-rules"` : ''}>+ Add a rule</button>
          ${fullRules(p) ? html`<p class="small muted" id="bb-full-rules">${why.rules()}</p>` : ''}
          ${howRulesRun()}
        </div>
        ${this.compact ? '' : html`<aside class="bb-picker bb-help" aria-label="How blocks work">${this.sideTop ? this.sideTop(this) : ''}${this.helpHtml()}</aside>`}
      </div>`;
  }

  paletteHtml() {
    const groups = this.tab === 'cond' ? choiceGroups('cond', this.program) : choiceGroups('action', this.program).filter((g) => g.name === this.tab);
    const family = this.tab === 'cond' ? 'cond' : `act fam-${this.tab.toLowerCase()}`;
    const block = (it, ptype, cls) => html`<button type="button" class="bb-b pal ${cls}" data-drag="palette" data-act="palette" data-ptype="${ptype}" ${dataAttrs(it.data)}>${it.label}</button>`;
    return html`
      <aside class="bb-palette" data-drop-trash aria-label="Blocks to add">
        <div class="bb-pal-head">
          <h2>Blocks</h2>
          <button type="button" class="bb-icon bb-pal-toggle" data-act="pal-toggle" aria-expanded="${String(this.palOpen)}" aria-controls="bb-pal-main" title="${this.palOpen ? 'Fold the blocks away' : 'Show the blocks'}">${raw(ICON.chevron)}<span class="visually-hidden">${this.palOpen ? 'Hide the blocks' : 'Show the blocks'}</span></button>
        </div>
        <div class="bb-pal-main" id="bb-pal-main" ${this.palOpen ? '' : 'hidden'}>
          <p class="small muted bb-pal-hint" id="bb-pal-hint">${this.hint()}</p>
          <button type="button" class="bb-b pal hat" data-drag="palette" data-act="palette" data-ptype="rule" ${fullRules(this.program) ? html`disabled title="${why.rules()}"` : ''}><span>when</span><span class="bb-b-gap"></span><span>do</span></button>
          <div class="bb-pal-tabs" role="tablist" aria-label="Kinds of block">
            ${TABS.map((t) => html`<button type="button" role="tab" class="bb-pal-tab tab-${t.id.toLowerCase()}" data-act="pal-tab" data-tab="${t.id}" aria-selected="${String(this.tab === t.id)}">${t.label}</button>`)}
          </div>
          <div class="bb-pal-body" role="tabpanel">
            ${groups.map((g) => html`<div class="bb-pal-group"><h3>${g.name}</h3><div class="bb-pal-items">
              ${g.items.map((it) => block(it, this.tab === 'cond' ? 'cond' : 'action', g.switches ? 'cond switch' : family))}
              ${g.switches ? newSwitchHtml(this.program) : ''}</div></div>`)}
          </div>
        </div>
        <p class="bb-trash" aria-hidden="true">Drop here to remove it</p>
      </aside>`;
  }

  ruleHtml(r, i) {
    const off = r.enabled === false;
    const n = this.program.rules.length;
    const steps = r.do || [];
    return html`
      <div class="bb-row">
        <article class="bb-rule bb-script${off ? ' off' : ''}${this.session?.index === i ? ' editing-rule' : ''}${this.target === i ? ' selected' : ''}" data-rule="${i}" aria-label="Rule ${i + 1}">
          <div class="bb-shape">
          <div class="bb-hat" data-drag="rule" data-i="${i}" title="Drag to move this rule">
            <span class="bb-num" aria-hidden="true">${i + 1}</span>
            <span class="bb-word">when</span>
            ${this.condHtml(r.when, `rules.${i}.when`)}
            <span class="bb-word">do</span>
            <span class="bb-tools">
              <button type="button" class="bb-icon small" data-act="rule-up" data-i="${i}" data-k="up|${i}" aria-label="Move rule ${i + 1} up" ${i === 0 ? 'disabled' : waitAttr(this)}>↑</button>
              <button type="button" class="bb-icon small" data-act="rule-down" data-i="${i}" data-k="down|${i}" aria-label="Move rule ${i + 1} down" ${i === n - 1 ? 'disabled' : waitAttr(this)}>↓</button>
              ${ruleTools(this, i, off)}
            </span>
          </div>
          <div class="bb-mouth" data-drop-stack="rules.${i}">
            ${steps.map((a, j) => this.stepHtml(a, `rules.${i}.do.${j}`, j))}
            <div class="bb-mouth-end">${steps.length ? '' : html`<span class="muted small">Drag actions here</span>`}${fullRule(r) ? html`<span class="muted small">${why.steps()}</span>` : ''}</div>
          </div>
          <div class="bb-cap" aria-hidden="true"></div>
          </div>
          ${ruleFooter(this, r, i)}
        </article>
        ${this.code ? html`<pre class="bb-py" aria-label="Rule ${i + 1} in Python">${pyRule(r, i)}</pre>` : ''}
      </div>`;
  }

  stepHtml(a, path, j) {
    const { label, fields, family } = actionParts(a, path);
    return html`<div class="bb-b act fam-${family}" data-drag="step" data-path="${path}" data-step="${path}" data-k="step|${path}" tabindex="0" aria-label="Step ${j + 1}: ${label}. Arrow keys move it, Delete removes it.">
      <span class="bb-grip" aria-hidden="true">${raw(ICON.grip)}</span><span class="bb-b-label">${label}</span>${fields}
      <button type="button" class="bb-icon small" data-act="del-action" data-path="${path}" data-k="del|${path}" aria-label="Remove this step">×</button></div>`;
  }

  condHtml(c, path) {
    // Every check has a grip to grab it by: some (a count) are nothing but dropdowns otherwise.
    const spot = (cls, inner, label) => html`<span class="bb-b cond ${cls}" data-drag="cond" data-path="${path}" data-drop-cond="${path}" data-k="cond|${path}" tabindex="0" aria-label="${label}. Delete removes it."><span class="bb-grip" aria-hidden="true">${raw(ICON.grip)}</span>${inner}</span>`;
    if (c?.kind === 'and' || c?.kind === 'or') {
      return spot('combo', html`${this.condHtml(c.a, `${path}.a`)}${select(path, 'kind', c.kind, [['and', 'and'], ['or', 'or']], 'and or or')}${this.condHtml(c.b, `${path}.b`)}`, `Check: ${c.kind}`);
    }
    if (c?.kind === 'not') return spot('combo', html`<span class="bb-b-label">not</span>${this.condHtml(c.a, `${path}.a`)}`, 'Check: not');
    const { label, fields, kind } = condParts(this.program, c, path);
    return spot(kind ?? '', html`${label ? html`<span class="bb-b-label">${label}</span>` : ''}${fields}`, `Check: ${label || c?.what || c?.kind}`);
  }

  helpHtml() {
    return html`
      <h2>How blocks work</h2>
      <p>Each rule is a <b>when … do</b> block. Every tick (about once a second) your bot checks the rules from the top.</p>
      <p>Drag a <b>check</b> (green) onto a rule's <i>when</i> to swap it. <b>and</b>, <b>or</b> and <b>not</b> wrap the check you drop them on.</p>
      <p>To start a rule somewhere other than the bottom, use a <b>+ rule</b> line, or drag the <b>when … do</b> block to that spot.</p>
      <p>Drag <b>actions</b> onto a rule, where you want them in its stack. Drag any block back onto the palette to remove it.</p>
      <p>No mouse? Tap a palette block to add it to the selected rule. A focused step moves with <b>↑ ↓</b> and goes with <b>Delete</b>.</p>
      ${this.program.switches.length ? html`<h3>Your switches</h3>${switchListHtml(this.program)}` : ''}`;
  }
}

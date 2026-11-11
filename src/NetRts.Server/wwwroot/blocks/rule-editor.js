import { html, raw } from '../util.js';
import { at, setAt, R, C, A, canCombine } from './program.js';
import { pyRule } from './python.js';
import { ICON } from './icons.js';
import { EditorCore } from './editor-core.js';
import {
  fullRule, fullRules, why, ruleOf, select, condParts, actionParts, choiceGroups, choiceNode, canWrap, dataAttrs,
  waitAttr, ruleTools, ruleFooter, insertRuleHtml, canvasControlsHtml, switchListHtml, newSwitchHtml, howRulesRun,
} from './editor-parts.js';

/**
 * Rule cards: each rule is a sentence ("When [fighters ≥ 8] do [attack the enemy base with all fighters]")
 * whose pieces are dropdowns. Clicking a piece's ▾ opens the picker beside the cards to swap it for
 * another kind. The editing itself (sessions, saving, limits) is EditorCore's.
 */
export class RuleEditor extends EditorCore {
  constructor(root, opts) {
    super(root, opts);
    this.picker = null;    // { type: 'cond' | 'action', path, mode: 'replace' | 'add' | 'insert', at }
    this.listen(root, 'keydown', (e) => {
      // A step's handle moves it with the arrow keys too, for keyboards (and where dragging doesn't work).
      const grip = e.target.closest?.('[data-drag-step]');
      if (grip && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) {
        e.preventDefault();
        const path = grip.dataset.dragStep;
        const j = Number(path.split('.').pop());
        this.moveStep(path, j + (e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 1), { focus: true });
      }
    });
    this.dragSteps();
    this.render();
  }

  /**
   * Dragging a step by its handle, within its rule (rules move with their Move Up and Move Down buttons):
   * the step follows the pointer, the original fades, and a yellow line shows where it will land.
   */
  dragSteps() {
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
    this.listen(this.root, 'dragstart', (e) => {
      const stepGrip = e.target.closest('[data-drag-step]');
      if (!stepGrip) return;
      const rule = ruleOf(stepGrip.dataset.dragStep);
      if (this.saving || (this.session && this.session.index !== rule)) { e.preventDefault(); return; }
      this.dragStep = stepGrip.dataset.dragStep;
      const el = stepGrip.closest('.bb-step') ?? stepGrip;
      const r = el.getBoundingClientRect();
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', this.dragStep);
      e.dataTransfer.setDragImage(el, Math.max(0, e.clientX - r.left), Math.max(0, e.clientY - r.top));
      requestAnimationFrame(() => el.classList.add('dragging'));   // after the browser has taken its picture
    });
    this.listen(this.root, 'dragover', (e) => {
      if (!this.dragStep) return;
      e.preventDefault();   // anywhere in the editor; a drop where nothing would move does nothing
      const d = stepDrop(e);
      if (d?.to != null) markDrop(d.el, d.before);
      else markDrop(null);
    });
    this.listen(this.root, 'drop', (e) => {
      if (!this.dragStep) return;
      e.preventDefault();
      const d = stepDrop(e);
      const from = this.dragStep;
      this.dragStep = null;
      clearDrop();
      if (d?.to != null) this.moveStep(from, d.to);
    });
    this.listen(this.root, 'dragend', () => { this.dragStep = null; clearDrop(); });
  }

  setProgram(program) { this.picker = null; super.setProgram(program); }
  afterSession() { this.picker = null; }
  afterReplace() { this.picker = null; }
  commit(mutate, { keepPicker = false } = {}) {
    if (!keepPicker) this.picker = null;
    return super.commit(mutate);
  }

  /** A step to a new place in its rule (arrow keys or a drop). */
  moveStep(path, to, { focus = false } = {}) {
    const list = path.replace(/\.\d+$/, '');
    const from = Number(path.split('.').pop());
    const n = at(this.program, list)?.length ?? 0;
    if (to < 0 || to >= n || to === from) return;
    this.moveAction(path, list.replace(/\.do$/, ''), to > from ? to + 1 : to);
    if (focus) this.root.querySelector(`[data-k="${CSS.escape(`stepgrip|${list}.${to}`)}"]`)?.focus();
  }

  /** The switch the picker's check should test, when a new one is added from the picker. */
  switchTarget() { return this.picker?.type === 'cond' ? this.picker.path : null; }

  act(act, b) {
    const { path } = b.dataset;
    switch (act) {
      case 'pick-cond': this.picker = { type: 'cond', path }; this.render(); this.focusPicker(); return;
      case 'pick-action': this.picker = { type: 'action', path, mode: 'replace' }; this.render(); this.focusPicker(); return;
      case 'add-action': this.picker = { type: 'action', path, mode: 'add' }; this.render(); this.focusPicker(); return;
      case 'insert-action': this.picker = { type: 'action', path, mode: 'insert', at: Number(b.dataset.j) }; this.render(); this.focusPicker(); return;
      case 'close-picker': this.picker = null; this.render(); return;
      case 'choose': this.choose(b.dataset); return;
      case 'add-rule':
      case 'insert-rule': {
        // The new rule goes where it was asked for, and its check's picker opens so it's ready to fill in.
        const i = act === 'insert-rule' ? Number(b.dataset.i) : this.program.rules.length;
        if (!this.insertRule(i, R(C.count('fighters', '>=', 8), A.attack()))) return;
        this.picker = { type: 'cond', path: `rules.${i}.when` };
        this.render();
        this.root.querySelector(`.bb-rule[data-rule="${i}"]`)?.scrollIntoView({ block: 'nearest' });
        this.focusPicker();
        return;
      }
      default: super.act(act, b);
    }
  }

  choose(d) {
    const pk = this.picker;
    if (!pk || !canWrap(this.program, pk.path, d.kind)) return;
    if (pk.type === 'cond') {
      this.placeCond(pk.path, choiceNode('cond', d, at(this.program, pk.path)));
      return;
    }
    const node = choiceNode('action', d);
    if (pk.mode === 'replace') this.edit(ruleOf(pk.path), (p) => setAt(p, pk.path, node));
    else this.insertAction(pk.path, pk.mode === 'add' ? at(this.program, pk.path).do.length : pk.at, node);
  }

  focusPicker() { this.root.querySelector('.bb-picker [data-act="choose"]')?.focus(); }

  // ---------------------------------------------------------------- drawing

  html() {
    const p = this.program;
    const full = fullRules(p);
    const pickerUnder = (i) => this.picker
      && (this.picker.path === `rules.${i}` || this.picker.path.startsWith(`rules.${i}.`))
      ? html`<aside class="bb-picker inline" aria-label="Pick a block">${this.pickerHtml()}</aside>` : '';
    return html`
      <div class="bb-editor${this.code ? ' with-code' : ''}${this.compact ? ' compact' : ''}${this.session ? ' locked' : ''}">
        <div class="bb-rules bb-surface">
          ${canvasControlsHtml(this)}
          ${this.saveError && !this.session ? html`<p class="bb-error" role="alert">${this.saveError}</p>` : ''}
          ${p.rules.length ? '' : html`<p class="bb-empty">No rules yet. Add one, or start from one of the starter strategies in the locker.</p>`}
          ${p.rules.map((r, i) => html`${insertRuleHtml(p, i)}${this.ruleHtml(r, i)}${pickerUnder(i)}`)}
          <button type="button" class="bb-add-rule" data-act="add-rule" data-k="add-rule" ${full ? html`disabled aria-describedby="bb-full-rules"` : ''}>+ Add a rule</button>
          ${full ? html`<p class="small muted" id="bb-full-rules">${why.rules()}</p>` : ''}
          ${howRulesRun()}
        </div>
        ${this.compact ? '' : html`<aside class="bb-picker bb-help" aria-label="How rules work">${this.sideTop ? this.sideTop(this) : ''}${this.helpHtml()}</aside>`}
      </div>`;
  }

  ruleHtml(r, i) {
    const n = this.program.rules.length;
    const off = r.enabled === false;
    const fullNote = fullRule(r) ? raw(`disabled title="${why.steps()}"`) : '';
    return html`
      <div class="bb-row">
        <div class="bb-rule-line">
        <div class="bb-move" role="group" aria-label="Move rule ${i + 1}">
          <button type="button" class="bb-move-btn" data-act="rule-up" data-i="${i}" data-k="up|${i}" aria-label="Move rule ${i + 1} up" ${i === 0 ? 'disabled' : waitAttr(this)}><span aria-hidden="true">↑</span> Move Up</button>
          <button type="button" class="bb-move-btn" data-act="rule-down" data-i="${i}" data-k="down|${i}" aria-label="Move rule ${i + 1} down" ${i === n - 1 ? 'disabled' : waitAttr(this)}>Move Down <span aria-hidden="true">↓</span></button>
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
                const insert = j > 0 && !fullRule(r) ? html`<button type="button" class="bb-step-insert" data-act="insert-action" data-path="rules.${i}" data-j="${j}" data-k="ins-step|${i}|${j}" aria-label="Add a step between steps ${j} and ${j + 1} of rule ${i + 1}" title="Add a step here">+</button>` : '';
                const grip = r.do.length > 1 ? html`<button type="button" class="bb-step-grip" draggable="true" data-drag-step="${path}" data-k="stepgrip|${path}" aria-label="Move step ${j + 1} of rule ${i + 1}: drag it, or press the left or right arrow" title="Drag to move (or ← →)">${raw(ICON.grip)}</button>` : '';
                return html`${insert}<span class="bb-step" data-step="${path}">${grip}${this.actionHtml(a, path)}</span>`;
              })}
              <button type="button" class="bb-plus" data-act="add-action" data-path="rules.${i}" data-k="add-action|${i}" aria-label="Add something for rule ${i + 1} to do" ${fullNote}>+ do</button>
            </div>
            <div class="bb-tools">${ruleTools(this, i, off)}</div>
          </div>
          ${ruleFooter(this, r, i)}
        </article>
        </div>
        ${this.code ? html`<pre class="bb-py" aria-label="Rule ${i + 1} in Python">${pyRule(r, i)}</pre>` : ''}
      </div>`;
  }

  editing(path) { return this.picker?.path === path ? ' editing' : ''; }

  condHtml(c, path) {
    if (c?.kind === 'and' || c?.kind === 'or') {
      return html`<span class="bb-group${this.editing(path)}">
        ${this.condHtml(c.a, `${path}.a`)}
        ${select(path, 'kind', c.kind, [['and', 'and'], ['or', 'or']], 'and or or')}
        ${this.condHtml(c.b, `${path}.b`)}
        <button type="button" class="bb-icon small" data-act="unwrap" data-path="${path}" data-k="unwrap|${path}" aria-label="Remove the second check">×</button>
      </span>`;
    }
    if (c?.kind === 'not') {
      return html`<span class="bb-group${this.editing(path)}"><button type="button" class="bb-icon small" data-act="unwrap" data-path="${path}" data-k="unwrap|${path}" aria-label="Remove not">not ×</button>${this.condHtml(c.a, `${path}.a`)}</span>`;
    }
    const { label, fields, kind } = condParts(this.program, c, path);
    return html`<span class="bb-chip ${kind ?? 'cond'}${this.editing(path)}"><button type="button" class="bb-kind" data-act="pick-cond" data-path="${path}" data-k="pick|${path}" aria-label="Change this check">${label}<span aria-hidden="true"> ▾</span></button>${fields}</span>`;
  }

  actionHtml(a, path) {
    const { label, fields, family } = actionParts(a, path);
    return html`<span class="bb-chip act${family === 'build' ? ' build' : ''}${this.editing(path)}"><button type="button" class="bb-kind" data-act="pick-action" data-path="${path}" data-k="pick|${path}" aria-label="Change this action: ${label}">${label}<span aria-hidden="true"> ▾</span></button>${fields}<button type="button" class="bb-icon small" data-act="del-action" data-path="${path}" data-k="del|${path}" aria-label="Remove this action">×</button></span>`;
  }

  helpHtml() {
    return html`
        <h2>How rules work</h2>
        <p>Each rule says <b>when</b> and <b>what to do</b>. Every tick (about once a second) your bot checks the rules from the top.</p>
        <p>Click a <b>▾</b> to swap a piece for a different one. Change numbers and choices right in the sentence.</p>
        <p>Add a <b>switch</b> to flip during a match: a rule like "When the <i>Attack!</i> switch is on, attack" lets you send your army in whenever you want.</p>
        <p><b>Undo</b> and <b>redo</b> float at the top left of your rules (or press Ctrl+Z and Ctrl+Shift+Z).</p>
        <p>To add a rule above another one, use the <b>+ rule</b> line above it.</p>
        <p>To add a step between two others, use the <b>+</b> between them. Drag a step by its <b>⠿</b> handle to change the order (or focus the handle and press ← or →).</p>
        ${this.program.switches.length ? html`<h3>Your switches</h3>${switchListHtml(this.program)}` : ''}`;
  }

  pickerHtml() {
    const pk = this.picker;
    if (!pk) return '';
    const head = (title) => html`<div class="bb-picker-head"><h2>${title}</h2><button type="button" class="bb-icon" data-act="close-picker" aria-label="Close the picker">×</button></div>`;
    const groups = choiceGroups(pk.type, this.program);
    const body = groups.map((g) => {
      // Combining: not around "every tick", and not past the depth limit.
      if (g.combine && at(this.program, pk.path)?.kind === 'always') return '';
      if (g.combine && !canCombine(this.program, pk.path)) return html`<div class="bb-opt-group"><h3>${g.name}</h3><p class="small muted">${why.depth()}</p></div>`;
      return html`<div class="bb-opt-group"><h3>${g.name}</h3><div>
        ${g.items.map((it) => html`<button type="button" class="bb-opt" data-act="choose" ${dataAttrs(it.data)}>${it.label}</button>`)}
        ${g.switches ? newSwitchHtml(this.program) : ''}</div></div>`;
    });
    if (pk.type === 'cond') return html`${head('Pick a check')}${body}`;
    if (pk.mode !== 'replace' && fullRule(at(this.program, pk.path))) return html`${head('Add something to do')}<p class="small muted">${why.steps()}</p>`;
    return html`${head(pk.mode === 'add' ? 'Add something to do' : pk.mode === 'insert' ? `Add a step before step ${pk.at + 1}` : 'Pick an action')}${body}`;
  }
}

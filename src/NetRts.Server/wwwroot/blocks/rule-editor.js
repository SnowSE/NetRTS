import { html, raw, setHtml } from '../util.js';
import { say, upgradeName, ORE } from '../lore.js';
import { clone, prefs, setPref } from './store.js';
import {
  C, A, R, at, setAt, LIMITS, knows, canCombine, byDefault,
  OPS, UNITS, FIGHTERS, BUILDINGS, UPGRADES, WHAT, WHO, SITES, TARGETS, SCOUT_TARGETS, EXPAND_BUILDINGS, VOCAB,
  unitWord, bWord, upgradeInfo,
} from './program.js';
import { pyRule } from './python.js';
import { ICON } from './icons.js';

// Where a new step or rule would go past the server's limits (GET /api/v1/blocks/vocabulary), its
// button is disabled and says why.
const fullRules = (p) => p.rules.length >= LIMITS().maxRules;
const fullRule = (rule) => (rule?.do?.length ?? 0) >= LIMITS().maxActionsPerRule;
const fullSwitches = (p) => p.switches.length >= LIMITS().maxSwitches;
const why = {
  rules: () => `A strategy has at most ${LIMITS().maxRules} rules.`,
  steps: () => `A rule does at most ${LIMITS().maxActionsPerRule} things.`,
  switches: () => `A strategy has at most ${LIMITS().maxSwitches} switches.`,
  depth: () => `Checks go at most ${LIMITS().maxDepth} deep, so this one can't be combined further.`,
};

/**
 * Rule cards: each rule is a sentence ("When [fighters ≥ 8] do [attack the enemy base with all fighters]")
 * whose pieces are dropdowns. Clicking a piece's ▾ opens the picker beside the cards to swap it for
 * another kind. Every change calls onChange with a fresh copy of the program.
 */
export class RuleEditor {
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
        const name = (input?.value || '').trim().slice(0, LIMITS().maxSwitchNameLength);
        if (!name) { input?.focus(); return; }
        const known = this.program.switches.some((s) => s.toLowerCase() === name.toLowerCase());
        if (!known && fullSwitches(this.program)) return;
        const path0 = this.picker?.path;
        const change = (p) => {
          if (!known) p.switches.push(name);
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
      case 'mix-add': return this.edit(ruleOf(path), (p) => { const u = at(p, path).units; if (u.length < LIMITS().maxMixUnits) u.push(FIGHTERS()[0]); });
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
        if (this.session || fullRules(this.program)) return;
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
    if (!knows(pk.type, d.kind)) return;
    if (pk.type === 'cond' && ['and', 'or', 'not'].includes(d.kind) && !canCombine(this.program, pk.path)) return;
    if (pk.type === 'action' && pk.mode !== 'replace' && fullRule(at(this.program, pk.path))) return;
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
        // A depot goes near the grubs and a tower toward the enemy; anything else where the server puts it.
        const site = { ResourceDepot: 'NearOre', GuardTower: 'TowardEnemy' }[d.building];
        const node = d.kind === 'train' ? A.train(d.unit)
          : d.kind === 'scout' ? A.scout(d.target)
          : d.kind === 'build' ? A.build(d.building, VOCAB.sites.includes(site) ? site : undefined)
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
      else if (field === 'value') node.value = Math.max(0, Math.min(LIMITS().maxNumber, Math.round(Number(el.value) || 0)));
      else node[field] = el.value;
    }, { keepPicker: true });
  }

  focusPicker() { this.root.querySelector('.bb-picker [data-act="choose"]')?.focus(); }

  // ---------------------------------------------------------------- rendering

  render() {
    const focused = document.activeElement?.dataset?.k;
    const p = this.program;
    const full = fullRules(p);
    const pickerUnder = (i) => this.picker
      && (this.picker.path === `rules.${i}` || this.picker.path.startsWith(`rules.${i}.`))
      ? html`<aside class="bb-picker inline" aria-label="Pick a block">${this.pickerHtml()}</aside>` : '';
    setHtml(this.root, html`
      <div class="bb-editor${this.code ? ' with-code' : ''}${this.compact ? ' compact' : ''}${this.session ? ' locked' : ''}">
        <div class="bb-rules">
          ${this.saveError && !this.session ? html`<p class="bb-error" role="alert">${this.saveError}</p>` : ''}
          ${p.rules.length ? '' : html`<p class="bb-empty">No rules yet. Add one, or start from one of the starter strategies in the locker.</p>`}
          ${p.rules.map((r, i) => html`${full ? '' : html`<button type="button" class="bb-insert" data-act="insert-rule" data-i="${i}" data-k="insert|${i}" aria-label="Add a rule before rule ${i + 1}"><span>+ rule</span></button>`}${this.ruleHtml(r, i)}${pickerUnder(i)}`)}
          <button type="button" class="bb-add-rule" data-act="add-rule" data-k="add-rule" ${full ? html`disabled aria-describedby="bb-full-rules"` : ''}>+ Add a rule</button>
          ${full ? html`<p class="small muted" id="bb-full-rules">${why.rules()}</p>` : ''}
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
    const full = fullRule(r);
    const fullNote = full ? raw(`disabled title="${why.steps()}"`) : '';
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
                const insert = j > 0 && !full ? html`<button type="button" class="bb-step-insert" data-act="insert-action" data-path="rules.${i}" data-j="${j}" data-k="ins-step|${i}|${j}" aria-label="Add a step between steps ${j} and ${j + 1} of rule ${i + 1}" title="Add a step here">+</button>` : '';
                const grip = r.do.length > 1 ? html`<button type="button" class="bb-step-grip" draggable="true" data-drag-step="${path}" data-k="stepgrip|${path}" aria-label="Move step ${j + 1} of rule ${i + 1}: drag it, or press the left or right arrow" title="Drag to move (or ← →)">${raw(ICON.grip)}</button>` : '';
                return html`${insert}<span class="bb-step" data-step="${path}">${grip}${this.actionHtml(a, path)}</span>`;
              })}
              <button type="button" class="bb-plus" data-act="add-action" data-path="rules.${i}" data-k="add-action|${i}" aria-label="Add something for rule ${i + 1} to do" ${fullNote}>+ do</button>
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
    // A value this list doesn't offer (from an older file, say) still shows as itself rather than as the first choice.
    if (value != null && !options.some(([v]) => String(v) === String(value))) options = [...options, [value, String(value)]];
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
          ${this.select(path, 'op', c.op, OPS(), 'Comparison')}
          <input class="bb-num-in" type="number" min="0" max="${LIMITS().maxNumber}" inputmode="numeric" value="${c.value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="Number">
        </span>`;
      case 'grubsLeft':
        return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn(`${ORE} left near my base`)}
          ${this.select(path, 'op', c.op, OPS(), 'Comparison')}
          <input class="bb-num-in" type="number" min="0" max="${LIMITS().maxNumber}" inputmode="numeric" value="${c.value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="Number">
        </span>`;
      case 'minute':
        return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('minute')}
          ${this.select(path, 'op', c.op, OPS(), 'Comparison')}
          <input class="bb-num-in" type="number" min="0" max="999" inputmode="numeric" value="${c.value}" data-path="${path}" data-field="value" data-k="${path}|value|" aria-label="Minute">
        </span>`;
      case 'switch':
        return html`<span class="bb-chip switch${this.editing(path)}">${kindBtn('switch')}
          ${this.select(path, 'name', c.name, (this.program.switches.length ? this.program.switches : [c.name]).map((s) => [s, s]), 'Which switch')}
          <span>is on</span></span>`;
      case 'enemyNearBase': return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('an enemy is near my base')}</span>`;
      case 'seeEnemy': return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('I can see an enemy')}</span>`;
      case 'always': return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn('Every tick')}</span>`;
      default: return html`<span class="bb-chip cond${this.editing(path)}">${kindBtn(String(c?.kind ?? '?'))}</span>`;
    }
  }

  actionHtml(a, path) {
    const kindBtn = (text) => html`<button type="button" class="bb-kind" data-act="pick-action" data-path="${path}" data-k="pick|${path}" aria-label="Change this action: ${text}">${text}<span aria-hidden="true"> ▾</span></button>`;
    const del = html`<button type="button" class="bb-icon small" data-act="del-action" data-path="${path}" data-k="del|${path}" aria-label="Remove this action">×</button>`;
    const cls = `bb-chip act${this.editing(path)}`;
    switch (a.kind) {
      case 'train':
        return html`<span class="${cls}">${kindBtn('train a')}${this.select(path, 'unit', a.unit, UNITS().map((t) => [t, unitWord(t)]), 'Which unit')}${del}</span>`;
      case 'trainMix':
        return html`<span class="${cls}">${kindBtn('train in turn')}
          ${(a.units || []).map((u, i) => html`${this.select(path, 'units', u, FIGHTERS().map((t) => [t, unitWord(t)]), `Unit ${i + 1} of the mix`, { i })}${a.units.length > 1 ? html`<button type="button" class="bb-icon small" data-act="mix-del" data-path="${path}" data-i="${i}" data-k="mixdel|${path}|${i}" aria-label="Remove unit ${i + 1} from the mix">−</button>` : ''}`)}
          ${a.units.length < LIMITS().maxMixUnits ? html`<button type="button" class="bb-icon small" data-act="mix-add" data-path="${path}" data-k="mixadd|${path}" aria-label="Add a unit to the mix">+</button>` : ''}${del}</span>`;
      case 'build':
        return html`<span class="${cls} build">${kindBtn('build a')}${this.select(path, 'building', a.building, BUILDINGS().map((b) => [b, bWord(b)]), 'Which building')}${this.select(path, 'site', a.site ?? byDefault('site'), SITES(), 'Where')}${del}</span>`;
      case 'research':
        return html`<span class="${cls} build">${kindBtn(say('research', 'take the class'))}${this.select(path, 'upgrade', a.upgrade, UPGRADES().map((u) => { const info = upgradeInfo(u); return [u, info ? `${upgradeName(u)}: ${info.short}` : upgradeName(u)]; }), 'Which upgrade')}${del}</span>`;
      case 'attack':
        return html`<span class="${cls}">${kindBtn('attack')}${this.select(path, 'target', a.target ?? byDefault('target'), TARGETS(), 'Attack what')}<span>with</span>${this.select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who attacks')}${del}</span>`;
      case 'defend':
        return html`<span class="${cls}">${kindBtn('defend with')}${this.select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who defends')}${del}</span>`;
      case 'retreat':
        return html`<span class="${cls}">${kindBtn('bring home')}${this.select(path, 'who', a.who ?? byDefault('who'), WHO(), 'Who comes home')}${del}</span>`;
      case 'scout':
        return html`<span class="${cls}">${kindBtn('send a scout to')}${this.select(path, 'target', a.target ?? byDefault('scoutTarget'), SCOUT_TARGETS(), 'Where the scout goes')}${del}</span>`;
      case 'expand':
        return html`<span class="${cls} build">${kindBtn(`expand to new ${ORE} with a`)}${this.select(path, 'building', a.building ?? byDefault('expandBuilding'), EXPAND_BUILDINGS(), 'What to build there')}${del}</span>`;
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
    // Only kinds the server knows are offered; a group with none left isn't shown.
    const opt = (label, data) => (knows(pk.type, data.kind)
      ? html`<button type="button" class="bb-opt" data-act="choose" ${Object.entries(data).map(([k, v]) => html`data-${k}="${v}" `)}>${label}</button>` : '');
    const group = (name, items) => ([].concat(items).some(Boolean) ? html`<div class="bb-opt-group"><h3>${name}</h3><div>${items}</div></div>` : '');
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
        ${knows('cond', 'switch') ? html`<div class="bb-opt-group"><h3>Switches</h3><div>
          ${this.program.switches.map((s) => opt(`${s} is on`, { kind: 'switch', name: s }))}
          ${fullSwitches(this.program) ? html`<p class="small muted">${why.switches()}</p>` : html`
          <span class="bb-new-switch"><label class="visually-hidden" for="bb-new-switch">New switch name</label><input id="bb-new-switch" type="text" maxlength="${LIMITS().maxSwitchNameLength}" placeholder="New switch, e.g. Attack!"><button type="button" class="bb-opt" data-act="new-switch">Add</button></span>`}</div></div>` : ''}
        ${current?.kind === 'always' ? ''
          : canCombine(this.program, pk.path) ? group('Combine', [opt('both this and…', { kind: 'and' }), opt('either this or…', { kind: 'or' }), opt('not this', { kind: 'not' })])
            : html`<div class="bb-opt-group"><h3>Combine</h3><p class="small muted">${why.depth()}</p></div>`}`;
    }
    if (pk.mode !== 'replace' && fullRule(at(this.program, pk.path))) return html`${head('Add something to do')}<p class="small muted">${why.steps()}</p>`;
    return html`${head(pk.mode === 'add' ? 'Add something to do' : pk.mode === 'insert' ? `Add a step before step ${pk.at + 1}` : 'Pick an action')}
      ${group('Train', UNITS().map((t) => opt(unitWord(t), { kind: 'train', unit: t })).concat([opt('a mix, in turn', { kind: 'trainMix' })]))}
      ${group('Build', BUILDINGS().filter((b) => b !== 'CommandCenter')   // a new HQ is an expansion
        .map((b) => opt(bWord(b), { kind: 'build', building: b })).concat([opt(say('research an upgrade', 'take a class'), { kind: 'research' }), opt(`expand to new ${ORE}`, { kind: 'expand' })]))}
      ${group('Army', [opt('attack', { kind: 'attack' }), opt('defend', { kind: 'defend' }), opt('bring home', { kind: 'retreat' }), opt('send a scout to the enemy', { kind: 'scout', target: 'enemyBase' }), opt(`explore for more ${ORE}`, { kind: 'scout', target: 'explore' })])}`;
  }
}

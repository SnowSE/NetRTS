// What a Block Bot rule editor does, whatever it looks like: holds the program, makes each change on a
// fresh copy, and in the cockpit saves changes into the running match one rule at a time. The cards
// (rule-editor.js) and the blocks (block-editor.js) extend it and draw the program their own way.
import { html, setHtml } from '../util.js';
import { clone, prefs, setPref } from './store.js';
import { C, at, setAt, LIMITS, FIGHTERS } from './program.js';
import { fullRule, fullRules, fullSwitches, fitsAt, ruleOf } from './editor-parts.js';

export class EditorCore {
  /**
   * @param root      the element to draw in
   * @param program   the program to edit (copied)
   * @param onChange  without onSave: every change, as a fresh copy of the program
   * @param onSave    the cockpit: sends a program into the running match (async; throws to refuse it)
   * @param onHover   the rule under the pointer or focus (its index, or null)
   * @param compact   a narrow column (the cockpit)
   * @param sideTop   the edit page's buttons, for the top of the side panel
   */
  constructor(root, { program, onChange, onHover, onSave, compact = false, sideTop = null }) {
    this.root = root;
    this.sideTop = sideTop;
    this.compact = compact;
    // With onSave, edits go into the running match one rule at a time: changing a rule starts a session
    // on it (a yellow border, Save and Cancel) and locks the others until it ends. One-click changes
    // (on/off, moving, deleting) are saved straight away. Without it, every change goes to onChange.
    this.onSave = onSave;
    this.session = null;   // { index, snapshot (the program before the edit), inserted }
    this.saving = false;
    this.saveError = '';
    this.program = clone(program);
    this.onChange = onChange;
    this.onHover = onHover;
    this.status = null;    // the running bot's rule statuses (cockpit)
    this.statusNote = '';
    this.code = !!prefs().code;
    this.listeners = [];
    this.listen(root, 'click', (e) => {
      const b = e.target.closest('button[data-act]');
      if (b && root.contains(b)) this.act(b.dataset.act, b);
    });
    this.listen(root, 'change', (e) => this.change(e));
    this.listen(root, 'keydown', (e) => {
      if (e.key === 'Escape' && this.session && !this.saving) { e.preventDefault(); this.cancel(); }
    });
    const hover = (e) => {
      const card = e.target.closest?.('.bb-rule');
      this.onHover?.(card ? Number(card.dataset.rule) : null);
    };
    this.listen(root, 'mouseover', hover);
    this.listen(root, 'focusin', hover);
    this.listen(root, 'mouseleave', () => this.onHover?.(null));
  }

  listen(target, type, fn, opts) {
    target.addEventListener(type, fn, opts);
    this.listeners.push(() => target.removeEventListener(type, fn, opts));
  }

  /** Stops listening (when the page switches to the other editor style). */
  destroy() { this.listeners.forEach((off) => off()); this.listeners = []; }

  setProgram(program) { this.program = clone(program); this.session = null; this.render(); }
  get busy() { return !!this.session; }
  setStatus(rules, note = '') {
    const changed = JSON.stringify(rules) !== JSON.stringify(this.status) || note !== this.statusNote;
    this.status = rules;
    this.statusNote = note;
    if (changed) this.renderStatus();
  }
  setCode(on) { this.code = on; setPref('code', on); this.render(); }

  // ---------------------------------------------------------------- changes

  /** Applies mutate to a copy of the program and draws it. (opts are for the editor style's own use.) */
  commit(mutate, opts) {
    const p = clone(this.program);
    mutate(p);
    this.program = p;
    this.render();
    if (!this.onSave) this.onChange?.(clone(p));
  }

  /** A change inside one rule. In save mode it opens (or continues) that rule's session; false if another rule's is open. */
  edit(index, mutate, opts) {
    if (this.onSave) {
      if (this.saving || (this.session && this.session.index !== index)) return false;
      this.session ??= { index, snapshot: clone(this.program), inserted: false };
      this.saveError = '';
    }
    this.commit(mutate, opts);
    return true;
  }

  /** A change to the list of rules, or across two rules. In save mode it's saved straight away. */
  async structural(mutate) {
    if (this.session || this.saving) return false;
    const before = clone(this.program);
    this.commit(mutate);
    if (!this.onSave) return true;
    this.saving = true;
    this.saveError = '';
    try { await this.onSave(clone(this.program)); } catch (e) {
      this.program = before;
      this.saveError = e.message;
    }
    this.saving = false;
    this.render();
    return true;
  }

  async save() {
    if (!this.session || this.saving) return;
    this.saving = true;
    this.saveError = '';
    this.render();
    try {
      await this.onSave(clone(this.program));
      this.session = null;
      this.afterSession();
    } catch (e) { this.saveError = e.message; }
    this.saving = false;
    this.render();
  }

  cancel() {
    if (!this.session) return;
    this.program = this.session.snapshot;
    this.session = null;
    this.saveError = '';
    this.afterSession();
    this.render();
  }

  /** A hook for when a rule's session ends (the cards close their picker). */
  afterSession() {}

  /** A new rule at index. In save mode it starts out being edited: Save sends it in, Cancel takes it away. */
  insertRule(index, rule) {
    if (this.session || this.saving || fullRules(this.program)) return false;
    if (this.onSave) this.session = { index, snapshot: clone(this.program), inserted: true };
    this.commit((p) => { p.rules.splice(index, 0, rule); });
    return true;
  }

  moveRule(from, to) {
    if (from === to || from < 0 || to < 0 || to >= this.program.rules.length) return;
    this.structural((p) => {
      const [r] = p.rules.splice(from, 1);
      p.rules.splice(to, 0, r);
    });
  }

  /** Inserts an action into rule (its path, 'rules.3') at index j. */
  insertAction(rulePath, j, node) {
    if (fullRule(at(this.program, rulePath))) return false;
    return this.edit(ruleOf(rulePath), (p) => { at(p, rulePath).do.splice(j, 0, node); });
  }

  /** Moves the step at path to rule (its path) at index j: within a rule it's an edit of that rule, across two it's structural. */
  moveAction(path, rulePath, j) {
    const fromList = path.replace(/\.\d+$/, '');
    const from = Number(path.split('.').pop());
    const move = (p) => {
      const [a] = at(p, fromList).splice(from, 1);
      at(p, `${rulePath}.do`).splice(j, 0, a);
    };
    if (fromList === `${rulePath}.do`) {
      if (from < j) j -= 1;
      if (j === from) return false;
      return this.edit(ruleOf(path), move);
    }
    if (fullRule(at(this.program, rulePath))) return false;
    return this.structural(move);
  }

  removeAction(path) {
    return this.edit(ruleOf(path), (p) => { at(p, path.replace(/\.\d+$/, '')).splice(Number(path.split('.').pop()), 1); });
  }

  /** Puts check at path (replacing what's there). False where it would nest too deep. */
  placeCond(path, check) {
    if (!fitsAt(check, path)) return false;
    return this.edit(ruleOf(path), (p) => setAt(p, path, check));
  }

  /**
   * Moves the check at from to to. What's left behind closes up: a rule's own check becomes "every tick",
   * an and/or keeps its other half, a not goes too.
   */
  moveCond(from, to) {
    if (from === to || to.startsWith(`${from}.`) || from.startsWith(`${to}.`)) return false;
    const check = at(this.program, from);
    if (!fitsAt(check, to)) return false;
    const move = (p) => { setAt(p, to, clone(check)); detachCond(p, from); };
    return ruleOf(from) === ruleOf(to) ? this.edit(ruleOf(to), move) : this.structural(move);
  }

  removeCond(path) { return this.edit(ruleOf(path), (p) => detachCond(p, path)); }

  /** Adds a switch (and, given path, makes the check there test it). */
  addSwitch(name, path = null) {
    name = (name || '').trim().slice(0, LIMITS().maxSwitchNameLength);
    if (!name) return false;
    const known = this.program.switches.some((s) => s.toLowerCase() === name.toLowerCase());
    if (!known && fullSwitches(this.program)) return false;
    const change = (p) => {
      if (!known) p.switches.push(name);
      if (path) setAt(p, path, C.switch(name));
    };
    if (path) return this.edit(ruleOf(path), change);
    this.commit(change);
    return true;
  }

  // ---------------------------------------------------------------- input

  /** A button with data-act: the actions both editors share. Each editor handles its own first. */
  act(act, b) {
    const { path } = b.dataset;
    const i = Number(b.dataset.i);
    switch (act) {
      case 'save-rule': return this.save();
      case 'cancel-rule': return this.cancel();
      case 'new-switch': {
        const input = this.root.querySelector('#bb-new-switch');
        if (!this.addSwitch(input?.value, this.switchTarget?.())) input?.focus();
        return undefined;
      }
      case 'del-switch': return this.commit((p) => { p.switches = p.switches.filter((s) => s !== b.dataset.name); });
      case 'del-action': return this.removeAction(path);
      case 'unwrap': return this.edit(ruleOf(path), (p) => setAt(p, path, at(p, path).a));
      case 'mix-add': return this.edit(ruleOf(path), (p) => { const u = at(p, path).units; if (u.length < LIMITS().maxMixUnits) u.push(FIGHTERS()[0]); });
      case 'mix-del': return this.edit(ruleOf(path), (p) => { const u = at(p, path).units; if (u.length > 1) u.splice(Number(b.dataset.i), 1); });
      case 'rule-toggle': return this.structural((p) => { p.rules[i].enabled = p.rules[i].enabled === false; });
      case 'rule-del': return this.structural((p) => { p.rules.splice(i, 1); });
      case 'rule-up':
      case 'rule-down': {
        // Focus follows the rule that moved, so pressing again keeps moving it.
        const to = act === 'rule-up' ? i - 1 : Math.min(i + 1, this.program.rules.length - 1);
        this.moveRule(i, to);
        const btn = this.root.querySelector(`[data-k="${act === 'rule-up' ? 'up' : 'down'}|${to}"]`);
        (btn && !btn.disabled ? btn : this.root.querySelector(`[data-k="${act === 'rule-up' ? 'down' : 'up'}|${to}"]`))?.focus();
        return undefined;
      }
      default: return undefined;
    }
  }

  /** A dropdown or number changed. */
  change(e) {
    const el = e.target;
    const { path, field } = el.dataset;
    if (!path || !field) return;
    this.edit(ruleOf(path), (p) => {
      const node = at(p, path);
      if (field === 'units') node.units[Number(el.dataset.i)] = el.value;
      else if (field === 'value') node.value = Math.max(0, Math.min(LIMITS().maxNumber, Math.round(Number(el.value) || 0)));
      else node[field] = el.value;
    }, { keepPicker: true });
  }

  // ---------------------------------------------------------------- drawing

  /** Draws the editor (each style its own), keeping focus on the control that had it. */
  render() {
    const focused = document.activeElement?.dataset?.k;
    setHtml(this.root, this.html());
    if (focused) this.root.querySelector(`[data-k="${CSS.escape(focused)}"]`)?.focus();
    this.renderStatus();
  }

  html() { return html``; }

  /** Each rule's line on what it did last tick (cockpit): drawn into its .bb-live box. */
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
      const bar = s.state === 'idle' && s.progress != null ? html`<span class="bb-progress" role="img" aria-label="${s.progress}% of the way"><span style="width:${s.progress}%"></span></span>` : '';
      setHtml(box, html`<span class="bb-dot" aria-hidden="true"></span><span class="bb-why">${s.why}${this.statusNote ? html` <span class="muted">${this.statusNote}</span>` : ''}</span>${bar}`);
    });
  }
}

/** Takes the check at path out of the program, closing up what held it. */
function detachCond(p, path) {
  const last = path.split('.').pop();
  if (last === 'when') { setAt(p, path, C.always()); return; }
  const parentPath = path.replace(/\.[ab]$/, '');
  const parent = at(p, parentPath);
  if (parent.kind === 'not') detachCond(p, parentPath);
  else setAt(p, parentPath, last === 'a' ? parent.b : parent.a);
}

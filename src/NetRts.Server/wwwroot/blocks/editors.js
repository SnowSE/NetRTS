// The two ways to edit rules, and the toggle between them: cards (sentences with dropdowns, rule-editor.js)
// and blocks (drag-and-drop, block-editor.js). The choice is remembered in this browser.
import { html } from '../util.js';
import { prefs, setPref } from './store.js';
import { RuleEditor } from './rule-editor.js';
import { BlockEditor } from './block-editor.js';

const STYLES = [
  { id: 'cards', label: 'Cards', title: 'Rules as sentences you change with dropdowns', Editor: RuleEditor },
  { id: 'blocks', label: 'Blocks', title: 'Rules as blocks you drag and drop', Editor: BlockEditor },
];

export const editorStyle = () => (STYLES.some((s) => s.id === prefs().editorStyle) ? prefs().editorStyle : 'cards');

/** An editor in the style the player last picked. Takes the options EditorCore does. */
export function createEditor(root, opts) {
  const { Editor } = STYLES.find((s) => s.id === editorStyle());
  const editor = new Editor(root, opts);
  editor.opts = opts;
  return editor;
}

/**
 * The same editor in the other style: same program, the rule being changed (if any) still mid-change,
 * the same live statuses. Returns the new editor; the old one stops listening.
 */
export function restyle(editor, style) {
  if (!STYLES.some((s) => s.id === style) || style === editorStyle()) return editor;
  setPref('editorStyle', style);
  editor.destroy();
  const next = createEditor(editor.root, { ...editor.opts, program: editor.program });
  Object.assign(next, {
    session: editor.session, saveError: editor.saveError, status: editor.status, statusNote: editor.statusNote,
    past: editor.past, future: editor.future,
  });
  next.render();
  return next;
}

/** The Cards | Blocks toggle; its buttons carry data-editor-style. */
export const styleToggleHtml = () => html`
  <div class="bb-style-toggle" role="group" aria-label="Rule editor style">
    ${STYLES.map((s) => html`<button type="button" class="btn ghost small-btn" data-editor-style="${s.id}" aria-pressed="${String(editorStyle() === s.id)}" title="${s.title}">${s.label}</button>`)}
  </div>`;

/** After a switch, every toggle on the page shows the new style. */
export const markStyle = (root) => root.querySelectorAll('[data-editor-style]')
  .forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.editorStyle === editorStyle())));

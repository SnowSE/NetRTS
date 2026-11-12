// The two ways to edit rules, and the toggle between them: cards (sentences with dropdowns, rule-editor.js)
// and blocks (drag-and-drop, block-editor.js, the default). The choice is remembered in this browser.
import { html } from '../util.js';
import { prefs, setPref } from './store.js';
import { RuleEditor } from './rule-editor.js';
import { BlockEditor } from './block-editor.js';

// The first is the default, for anyone who hasn't picked one.
const STYLES = [
  { id: 'blocks', label: 'Blocks', title: 'Rules as blocks you drag and drop', Editor: BlockEditor },
  { id: 'cards', label: 'Cards', title: 'Rules as sentences you change with dropdowns', Editor: RuleEditor },
];

export const editorStyle = () => (STYLES.some((s) => s.id === prefs().editorStyle) ? prefs().editorStyle : STYLES[0].id);

/** An editor in the style the player last picked. Takes the options EditorCore does. */
export function createEditor(root, opts) {
  const { Editor } = STYLES.find((s) => s.id === editorStyle());
  const editor = new Editor(root, { ...opts, styleToggle: styleToggleHtml });
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

/** The Cards | Blocks buttons (in the editor's floating controls); each carries data-editor-style for the page to act on. */
export const styleToggleHtml = () => html`
  ${STYLES.map((s) => html`<button type="button" class="bb-pill-opt" data-editor-style="${s.id}" aria-pressed="${String(editorStyle() === s.id)}" title="${s.title}">${s.label}</button>`)}`;

/** After a switch, every toggle on the page shows the new style. */
export const markStyle = (root) => root.querySelectorAll('[data-editor-style]')
  .forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.editorStyle === editorStyle())));

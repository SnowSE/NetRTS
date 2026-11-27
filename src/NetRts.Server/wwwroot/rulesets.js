// The rule-set picker on the forms that start a match (exhibitions, hosted matches). The rule sets come
// from GET /api/v1/rulesets; the choice goes out as the match setting `rules`.
import { api, html } from './util.js';

let loading = null;

/** A labeled <select name="rules">, filled in by fillRules once the server says which rule sets it has. */
export function rulesField(id) {
  return html`<div class="field"><label for="${id}">Rules</label>
    <select id="${id}" name="rules"><option value="">Server default</option></select></div>`;
}

/** Fills the picker; `alive` says whether the page is still there when the answer comes back. */
export async function fillRules(select, alive = () => true) {
  loading ??= api('/api/v1/rulesets').catch((e) => { loading = null; throw e; });
  let sets;
  try {
    sets = await loading;
  } catch {
    return; // the default still works without the list
  }
  if (!alive() || !select) return;
  select.replaceChildren(...sets.map((r) => new Option(r.isDefault ? `${r.title} (default)` : r.title, r.name, false, r.isDefault)));
}

/** The form's chosen rule set, or undefined for the server's. */
export const chosenRules = (form) => form.get('rules') || undefined;

// The rule sets a match can be played under (GET /api/v1/rulesets): the picker on the forms that start a
// match (exhibitions, hosted matches; the choice goes out as the match setting `rules`), and where the
// move from classic to the spring rules stands, for the home page notice and #/spring-rules.
import { api, html } from './util.js';

let loading = null;

/** The server's rule sets, fetched once per page load. */
export function ruleSets() {
  loading ??= api('/api/v1/rulesets').catch((e) => { loading = null; throw e; });
  return loading;
}

/** The spring rule set's name, and whether new matches already default to it. */
export const SPRING = 'spring-draft';
export const springIsDefault = (sets) => Boolean(sets?.find((r) => r.name === SPRING)?.isDefault);

/** One line on where the switch stands. */
export const rolloutLine = (sets) => (springIsDefault(sets)
  ? 'The spring rules are now the default for new matches. Classic is still there under Rules when you start one.'
  : 'The next regular season match is the last one played under the classic rules. After it, new matches use the spring rules.');

/** A labeled <select name="rules">, filled in by fillRules once the server says which rule sets it has. */
export function rulesField(id) {
  return html`<div class="field"><label for="${id}">Rules</label>
    <select id="${id}" name="rules"><option value="">Server default</option></select></div>`;
}

/** Fills the picker; `alive` says whether the page is still there when the answer comes back. */
export async function fillRules(select, alive = () => true) {
  let sets;
  try {
    sets = await ruleSets();
  } catch {
    return; // the default still works without the list
  }
  if (!alive() || !select) return;
  select.replaceChildren(...sets.map((r) => new Option(r.isDefault ? `${r.title} (default)` : r.title, r.name, false, r.isDefault)));
}

/** The form's chosen rule set, or undefined for the server's. */
export const chosenRules = (form) => form.get('rules') || undefined;

// #/blocks: Block Bot. Build a bot from rule cards, play it against the house bots or in a real match,
// and change it while the match runs. The server plays the rules (BlockBot in NetRts.Bots), so a match
// keeps going when the tab closes. Strategies live in this browser (localStorage, saved as you go) and
// download as .json files. app.js loads this module only when a Block Bot page opens.
//
//   #/blocks                 the locker (locker.js): your strategies, and where to play
//   #/blocks/edit/<id>       the rule-card editor (edit-page.js, rule-editor.js)
//   #/blocks/match/<id>      the cockpit (cockpit.js): the match in the 3D view, the rules live, switches,
//                            and each rule saved into the running match as you change it
import { html, setHtml } from '../util.js';
import { loadVocabulary } from './program.js';
import { mountLocker } from './locker.js';
import { mountEditor } from './edit-page.js';
import { mountCockpit } from './cockpit.js';

/** Mounts the page for route ({ page: 'edit' | 'match', id } or null for the locker); returns its teardown. */
export function mountBlocks(main, route) {
  let alive = true;
  let teardown = null;
  // The pages draw on the server's vocabulary and the game's rules: fetched once, and again on the next visit if that failed.
  setHtml(main, html`<section class="bb-page"><p class="muted">Loading Block Bot…</p></section>`);
  loadVocabulary().then(() => {
    if (!alive) return;
    teardown = route?.page === 'edit' ? mountEditor(main, route.id)
      : route?.page === 'match' ? mountCockpit(main, route.id)
        : mountLocker(main);
  }, (e) => {
    if (!alive) return;
    setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1>
      <p class="bb-error" role="alert">Couldn’t load the rule blocks from the server: ${e.message}</p>
      <p><a class="btn" href="${location.hash}" data-bb-retry>Try again</a></p></section>`);
    main.querySelector('[data-bb-retry]')?.addEventListener('click', (ev) => {
      ev.preventDefault();
      window.dispatchEvent(new HashChangeEvent('hashchange'));
    });
  });
  return () => { alive = false; teardown?.(); };
}

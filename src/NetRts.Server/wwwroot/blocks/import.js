// #/blocks/import?program=<base64url JSON>: a strategy from a link (the Pick-up Game's "Take your bot
// home"). The program is checked like a strategy file (checkProgram: the server's validator), saved as a
// new strategy in this browser, and opened in the editor. Nothing about it is stored on the server.
import { html, setHtml, setTitle } from '../util.js';
import { checkProgram } from './program.js';
import { newStrategy } from './store.js';
import { decodeProgram } from '../pickup/convert.js';

/** Longer than any program a pick-up plan turns into, by a lot. */
const MAX_LINK = 16000;

export function mountImport(main, encoded) {
  let alive = true;
  setTitle('Open a bot', 'Block Bot');
  setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1><p class="muted">Opening your bot…</p></section>`);
  const fail = (why) => {
    if (!alive) return;
    setHtml(main, html`<section class="bb-page"><h1>Block Bot</h1>
      <p class="bb-error" role="alert">This link doesn’t hold a bot Block Bot can open. ${why}</p>
      <p><a class="btn" href="#/blocks">Go to Block Bot</a></p></section>`);
  };

  (async () => {
    if (!encoded) { fail('It has no bot in it.'); return; }
    if (encoded.length > MAX_LINK) { fail('It is too long.'); return; }
    let data;
    try { data = decodeProgram(encoded); } catch { fail('Part of it is missing or garbled.'); return; }
    let program;
    try { program = await checkProgram(data, 'My pick-up bot', 'That link'); } catch (e) { fail(e.message); return; }
    if (!alive) return;
    const s = newStrategy(program);
    location.replace(`#/blocks/edit/${s.id}`);   // the back button doesn't open it twice
  })();

  return () => { alive = false; };
}

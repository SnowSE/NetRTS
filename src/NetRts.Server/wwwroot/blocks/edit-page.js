// #/blocks/edit/<id>: one strategy in the rule editor (cards or blocks, editors.js), saved in this browser as you go.
import { html, raw, setHtml, mountScope } from '../util.js';
import { getStrategy, putStrategy, setPref, matchRecords, player, authed, themed, nav, downloadStrategy } from './store.js';
import { LIMITS } from './program.js';
import { createEditor, restyle, markStyle } from './editors.js';
import { ICON } from './icons.js';

export function mountEditor(main, id) {
  const s = getStrategy(id);
  if (!s) {
    setHtml(main, html`<section class="bb-page"><h1>Strategy not found</h1><p>It isn’t saved in this browser. <a href="#/blocks">Back to my strategies</a></p></section>`);
    return () => {};
  }
  let strategy = s;
  let liveMatch = null;
  const scope = mountScope();

  // What's done with the whole strategy sits by its name: Download (the strategy, or a bot in code) and
  // Play it. How the rules look, and undo, float over the rules themselves (the editor draws those).
  const actions = () => html`
    <details class="bb-menu" id="bb-dl-menu">
      <summary class="btn ghost">${raw(ICON.download)} Download <span class="bb-menu-caret" aria-hidden="true">${raw(ICON.chevron)}</span></summary>
      <div class="bb-menu-list">
        <button type="button" data-ed="dl">Strategy file <span class="muted">.json, to open again here</span></button>
        <button type="button" data-ed="export" data-lang="python">Python bot <span class="muted">.py, runs on its own</span></button>
        <button type="button" data-ed="export" data-lang="csharp">C# bot <span class="muted">.cs, runs on its own</span></button>
      </div>
    </details>
    <a class="btn start" href="#/blocks" data-ed="play">${raw(ICON.play)} Play it</a>`;

  // The way on from blocks: this strategy as a whole bot in code (export.js, loaded when it's asked for).
  const codeCard = () => html`
    <section class="bb-code-card" aria-labelledby="bb-code-h">
      <h2 id="bb-code-h">Ready for more than blocks?</h2>
      <p>Download this strategy as a complete bot in Python or C#. It plays your rules from the first run, and from there
        you can teach it anything blocks can’t say: split your army, react to what your scouts find, time an attack to the tick.</p>
      <div class="bb-row-btns">
        <button type="button" class="btn ghost" data-ed="export" data-lang="python">${raw(ICON.download)} Python</button>
        <button type="button" class="btn ghost" data-ed="export" data-lang="csharp">${raw(ICON.download)} C#</button>
      </div>
      <p class="small muted">It plays close to your Block Bot, though not move for move. You’ll need Python 3, or the .NET 10 SDK; how to run it is at the top of the file.</p>
      <p class="bb-error small" role="alert" data-export-error hidden></p>
    </section>`;

  setHtml(main, html`
    <section class="bb-page bb-edit-page">
      <div class="bb-bar">
        <a href="#/blocks" class="bb-back">← My strategies</a>
        <label class="visually-hidden" for="bb-sname">Strategy name</label>
        <input id="bb-sname" class="bb-title-in" maxlength="${LIMITS().maxNameLength}" value="${strategy.program.name}">
        <span class="bb-saved small muted" id="bb-saved">${raw(ICON.check)} Saved in this browser</span>
        <span class="bb-spacer"></span>
        <div class="bb-edit-actions">${actions()}</div>
      </div>
      <p class="bb-banner" id="bb-livebanner" hidden></p>
      <div id="bb-editor"></div>
    </section>`);

  const savedEl = main.querySelector('#bb-saved');
  let editor = createEditor(main.querySelector('#bb-editor'), {
    program: strategy.program,
    sideTop: () => codeCard(),
    onChange: (program) => {
      strategy = { ...strategy, program: { ...program, name: strategy.program.name } };
      save();
    },
  });

  function save() {
    const ok = putStrategy(strategy);
    setHtml(savedEl, ok ? html`${raw(ICON.check)} Saved in this browser` : html`Couldn’t save here. Download a copy!`);
  }

  main.querySelector('#bb-sname').addEventListener('input', (e) => {
    strategy = { ...strategy, program: { ...strategy.program, name: e.target.value.slice(0, LIMITS().maxNameLength) } };
    editor.program.name = strategy.program.name;
    save();
  });
  const onAction = (e) => {
    const styleBtn = e.target.closest('[data-editor-style]');
    if (styleBtn) {
      editor = restyle(editor, styleBtn.dataset.editorStyle);
      markStyle(main);
      return;
    }
    const b = e.target.closest('[data-ed]');
    if (!b) return;
    if (b.closest('#bb-dl-menu')) menu.open = false;
    if (b.dataset.ed === 'dl') downloadStrategy(strategy);
    else if (b.dataset.ed === 'play') { setPref('selected', strategy.id); nav.jumpToPlay = true; }
    else if (b.dataset.ed === 'export') exportCode(b);
  };
  scope.on(main, 'click', onAction);

  // The Download menu closes on a click anywhere else, and on Escape (back to its button).
  const menu = main.querySelector('#bb-dl-menu');
  scope.on(document, 'click', (e) => { if (menu.open && !menu.contains(e.target)) menu.open = false; });
  scope.on(menu, 'keydown', (e) => {
    if (e.key !== 'Escape' || !menu.open) return;
    menu.open = false;
    menu.querySelector('summary').focus();
  });

  async function exportCode(b) {
    const error = main.querySelector('[data-export-error]');
    if (error) error.hidden = true;
    try {
      const { downloadCode } = await scope.wait(import('./export.js'));
      await scope.wait(downloadCode(strategy.program, b.dataset.lang));
    } catch (e) {
      if (!error) return;
      error.hidden = false;
      error.textContent = `Couldn’t make the file: ${e.message}`;
    }
  }

  // A match already playing this strategy: changes here go live from its cockpit.
  (async () => {
    if (!player()) return;
    const recs = matchRecords();
    try {
      const live = (await scope.wait(authed(themed('/api/v1/blocks/matches')))).filter((m) => m.matchStatus !== 'Completed' && recs[m.matchId]?.strategyId === strategy.id);
      liveMatch = live[0] ?? null;
    } catch { liveMatch = null; }
    if (!liveMatch) return;
    const banner = main.querySelector('#bb-livebanner');
    banner.hidden = false;
    setHtml(banner, html`This strategy is playing a match right now. <a href="#/blocks/match/${liveMatch.matchId}">Open the cockpit</a> to watch it and apply your changes.`);
  })();

  return () => { editor.destroy(); scope.dispose(); };
}

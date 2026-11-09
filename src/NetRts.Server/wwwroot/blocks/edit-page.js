// #/blocks/edit/<id>: one strategy in the rule-card editor, saved in this browser as you go.
import { html, raw, setHtml, mountScope } from '../util.js';
import { getStrategy, putStrategy, prefs, setPref, matchRecords, player, authed, themed, nav, downloadStrategy } from './store.js';
import { LIMITS } from './program.js';
import { RuleEditor } from './rule-editor.js';
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

  // Python, Download and Play it: in the top bar on narrow screens (which sticks), at the top of the
  // side panel (which stays in view as the rules scroll) on wide ones.
  const actions = (code) => html`
    <button type="button" class="btn ghost" data-ed="code" aria-pressed="${code}">${raw(ICON.code)} Python</button>
    <button type="button" class="btn ghost" data-ed="dl">${raw(ICON.download)} Download</button>
    <a class="btn start" href="#/blocks" data-ed="play">Play it</a>`;

  setHtml(main, html`
    <section class="bb-page bb-edit-page">
      <div class="bb-bar">
        <a href="#/blocks" class="bb-back">← My strategies</a>
        <label class="visually-hidden" for="bb-sname">Strategy name</label>
        <input id="bb-sname" class="bb-title-in" maxlength="${LIMITS().maxNameLength}" value="${strategy.program.name}">
        <span class="bb-saved small muted" id="bb-saved">${raw(ICON.check)} Saved in this browser</span>
        <span class="bb-spacer"></span>
        <div class="bb-edit-actions bb-top-actions">${actions(!!prefs().code)}</div>
      </div>
      <p class="bb-banner" id="bb-livebanner" hidden></p>
      <div id="bb-editor"></div>
    </section>`);

  const savedEl = main.querySelector('#bb-saved');
  const editor = new RuleEditor(main.querySelector('#bb-editor'), {
    program: strategy.program,
    sideTop: (ed) => html`<div class="bb-edit-actions bb-side-actions">${actions(ed.code)}</div>`,
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
    const b = e.target.closest('[data-ed]');
    if (!b) return;
    if (b.dataset.ed === 'code') {
      editor.setCode(!editor.code);
      main.querySelectorAll('[data-ed="code"]').forEach((x) => x.setAttribute('aria-pressed', String(editor.code)));
    } else if (b.dataset.ed === 'dl') downloadStrategy(strategy);
    else if (b.dataset.ed === 'play') { setPref('selected', strategy.id); nav.jumpToPlay = true; }
  };
  scope.on(main, 'click', onAction);

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

  return () => scope.dispose();
}

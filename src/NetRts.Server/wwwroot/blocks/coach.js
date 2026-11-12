// The coached first match ("Start now" in intro.js): three short steps on a card over the match while it
// plays. 1: these rules are your bot, and what their colours mean. 2: add a defend rule at the top, ticked
// off as the blocks go in, and done once the match is playing it (or "Do it for me"). 3: it's live, and two
// ideas to try next. The step is kept (store.js coaching), so a reload picks up where it was; Skip, the last
// step's button or the end of the match finishes it.
import { html, setHtml } from '../util.js';
import { nickname, upgradeName } from '../lore.js';
import { coaching, setCoaching, setPref, nav } from './store.js';
import { R, C, A, bWord, unitWord } from './program.js';
import { editorStyle } from './editors.js';
import { FIRST_OPPONENT } from './intro.js';

const hasKind = (c, kinds) => !!c && (kinds.includes(c.kind) || hasKind(c.a, kinds) || hasKind(c.b, kinds));
const NEAR = ['enemyNearBase', 'underAttack'];
/** A rule that sends fighters home when an enemy turns up. */
const defends = (r) => r && r.enabled !== false && hasKind(r.when, NEAR) && (r.do || []).some((a) => a.kind === 'defend');

/** The coach for this match, or null when it isn't the coached one. */
export function coachFor(matchId) {
  return coaching()?.matchId === matchId ? coaching() : null;
}

/**
 * Puts the coach over the cockpit (root) for matchId. live(): { editor, status, summary }, the cockpit's
 * current ones. Returns { update, ended } for the cockpit to call as its status and summary come in.
 */
export function mountCoach(scope, root, matchId, live) {
  const card = document.createElement('section');
  card.className = 'bb-coach';
  card.setAttribute('aria-labelledby', 'bb-coach-h');
  card.setAttribute('aria-live', 'polite');
  root.querySelector('.bb-3d-wrap')?.append(card);
  scope.cleanup(() => { card.remove(); spot([]); });

  let step = coaching()?.step ?? 1;
  let shown = '';

  const go = (n) => {
    step = n;
    if (n > 3) setCoaching(null); else setCoaching({ matchId, step: n });
    update();
  };

  /** Rings the elements the step is about (re-applied each update: the editor redraws its own). */
  function spot(selectors) {
    root.querySelectorAll('.bb-coach-spot').forEach((el) => el.classList.remove('bb-coach-spot'));
    for (const sel of selectors) root.querySelector(sel)?.classList.add('bb-coach-spot');
  }

  /** Step 2's checklist from the rules being edited and the ones playing. */
  function progress() {
    const { editor, status } = live();
    const editing = editor?.program?.rules ?? [];
    const playing = status?.program?.rules ?? [];
    const saved = playing.some(defends);
    const check = saved || editing.some((r) => hasKind(r.when, NEAR));
    const action = saved || editing.some(defends);
    const added = check || action || !!editor?.session?.inserted || editing.length > playing.length;
    return { added, check, action, saved };
  }

  function doItForMe() {
    const { editor } = live();
    if (!editor || editor.saving) return;
    if (editor.session) editor.cancel();
    editor.structural((p) => { p.rules.unshift(R(C.enemyNearBase(), A.defend())); });
  }

  function cardHtml() {
    const skip = html`<button type="button" class="btn ghost small-btn" data-coach-skip>Skip the tour</button>`;
    if (step === 1) {
      return html`
        <p class="bb-coach-kicker">1 of 3 · Your bot is playing</p>
        <h2 id="bb-coach-h">These rules are your bot</h2>
        <p>On the left, every second, it reads them from the top. Under each rule it says what it’s doing:</p>
        <ul class="bb-coach-keys">
          <li><span class="bb-coach-dot firing"></span><span><b>Green</b> is doing it right now.</span></li>
          <li><span class="bb-coach-dot waiting"></span><span><b>Yellow</b> wants to, and says what it’s waiting for.</span></li>
          <li><span class="bb-coach-dot"></span><span><b>Grey</b> is waiting for its “when” to come true.</span></li>
        </ul>
        <p class="small muted">Point at a rule to see which ${unitWord('Worker', true)} and ${unitWord('Soldier', true)} it’s moving.</p>
        <div class="bb-coach-row">${skip}<button type="button" class="btn start small-btn" data-coach-next>Next</button></div>`;
    }
    if (step === 2) {
      const p = progress();
      const tick = (done, text) => html`<li class="${done ? 'done' : ''}"><span class="bb-coach-mark" aria-hidden="true">${done ? '✓' : ''}</span><span>${text}${done ? html`<span class="visually-hidden"> (done)</span>` : ''}</span></li>`;
      const blocks = editorStyle() === 'blocks';
      return html`
        <p class="bb-coach-kicker">2 of 3 · Your turn</p>
        <h2 id="bb-coach-h">Protect your base</h2>
        <p>${nickname(FIRST_OPPONENT) ?? 'Your opponent'} sends soldiers over. Yours only move once there are 6 of them. Add a rule that sends them home whenever an enemy gets close:</p>
        <ol class="bb-coach-steps">
          ${blocks ? html`
            ${tick(p.added, html`Click <b>+ rule</b> above rule 1 for a new rule at the top.`)}
            ${tick(p.check, html`From <b>Checks</b>, drag (or tap) <b>an enemy is near my base</b> onto its <i>when</i>.`)}
            ${tick(p.action, html`From <b>Army</b>, drag (or tap) <b>defend with all fighters</b> into its <i>do</i>.`)}
            ${tick(p.saved, html`Press <b>Save</b>.`)}` : html`
            ${tick(p.added, html`Click <b>+ Add a rule</b>.`)}
            ${tick(p.check, html`Change its <i>when</i> to <b>an enemy is near my base</b>.`)}
            ${tick(p.action, html`Change its <i>do</i> to <b>defend with all fighters</b>.`)}
            ${tick(p.saved, html`Press <b>Save</b>, then drag it to the top by its grip.`)}`}
        </ol>
        <p class="small muted">Rules higher up go first, so defending comes before anything else.</p>
        <div class="bb-coach-row">${skip}<button type="button" class="btn ghost small-btn" data-coach-auto>Do it for me</button></div>`;
    }
    return html`
      <p class="bb-coach-kicker">3 of 3 · That’s the whole loop</p>
      <h2 id="bb-coach-h">Your change is already playing</h2>
      <p>It went into the match the moment you saved, as <b>version ${live().status?.version ?? 2}</b>, with no restart. It’s saved in this browser too, so it’s there next time. Change something, watch what it does, change it again.</p>
      <p class="bb-coach-kicker">Two things to try on your own</p>
      <div class="bb-coach-ideas">
        <div><b>Mix in archers</b><span>Swap <b>train a ${unitWord('Soldier')}</b> for <b>train a mix</b> of ${unitWord('Soldier', true)} and ${unitWord('Archer', true)}. ${unitWord('Archer', true).replace(/^./, (c) => c.toUpperCase())} shoot from 4 tiles away while ${unitWord('Soldier', true)} hold the front.</span></div>
        <div><b>Hit harder</b><span>Build a <b>${bWord('TechLab')}</b>, then research <b>${upgradeName('Weapons1')}</b>: +2 damage for every fighter.</span></div>
      </div>
      <div class="bb-coach-row"><span class="small muted">Won? Try the ${nickname('rusher') ?? 'rusher'} next.</span><button type="button" class="btn start small-btn" data-coach-done>Got it, let me play</button></div>`;
  }

  function update() {
    const { status, summary } = live();
    if (step > 3 || summary?.status === 'Completed') {
      card.hidden = true;
      spot([]);
      return;
    }
    if (step === 2 && progress().saved) { go(3); return; }
    const next = cardHtml().__html;
    if (next !== shown) {
      shown = next;
      setHtml(card, next);
    }
    card.hidden = !status;
    if (step === 1) spot(['#bb-editor']);
    else if (step === 2) {
      const p = progress();
      spot(!p.added ? ['.bb-insert[data-i="0"]', '.bb-add-rule']
        : !p.check ? ['.bb-pal-tab[data-tab="cond"][aria-selected="false"]', '.bb-palette [data-ptype="cond"][data-kind="enemyNearBase"]']
          : !p.action ? ['.bb-pal-tab[data-tab="Army"][aria-selected="false"]', '.bb-palette [data-ptype="action"][data-kind="defend"]']
            : ['[data-act="save-rule"]']);
    } else spot(['#bb-versions li[aria-current]']);
  }

  /** The match is over: the result box gets where to go next. */
  function ended(resultBox) {
    if (resultBox.querySelector('.bb-coach-next')) return;
    setCoaching(null);
    step = 4;
    update();
    const next = document.createElement('div');
    next.className = 'bb-coach-next';
    setHtml(next, html`
      <p class="bb-coach-kicker">Where next</p>
      <div class="bb-row-btns">
        <a class="btn" href="#/blocks" data-coach-play="rusher">Play the ${nickname('rusher') ?? 'rusher'}: it rushes you</a>
        <a class="btn ghost" href="#/blocks" data-coach-play="Real">Join a match against classmates</a>
      </div>`);
    resultBox.append(next);
  }

  scope.on(root, 'click', (e) => {
    const b = e.target.closest('[data-coach-next], [data-coach-skip], [data-coach-auto], [data-coach-done], [data-coach-play]');
    if (!b) return;
    const d = b.dataset;
    if ('coachNext' in d) go(2);
    else if ('coachSkip' in d || 'coachDone' in d) go(4);
    else if ('coachAuto' in d) doItForMe();
    else if (d.coachPlay) {
      // The locker opens at step 3 with the next match picked.
      if (d.coachPlay === 'Real') setPref('mode', 'Real');
      else { setPref('mode', 'HouseBot'); setPref('one', d.coachPlay); }
      nav.jumpToPlay = true;
    }
  });

  // The editor redraws on its own (each drag, each status), so the rings and ticks are checked often.
  const loop = () => { update(); scope.later(loop, 400); };
  loop();
  return { update, ended };
}

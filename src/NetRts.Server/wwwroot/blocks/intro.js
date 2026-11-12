// The first visit to Block Bot: a welcome that says what the game is and how a bot works, and "Start now":
// a random name, the My First Bot starter and a match against the Poky Badger, coached in the cockpit
// (coach.js). The locker opens it once per browser (not for anyone already playing, or whose Discord
// account has a bot already); its "How it works" opens it again. Closing it any way counts as seen.
import { api, html, setHtml } from '../util.js';
import { GAME, ORE, say, nickname } from '../lore.js';
import { player, setPlayer, strategies, newStrategy, recordMatch, authed, introSeen, markIntroSeen, setCoaching } from './store.js';
import { starters, sameProgram, bWord } from './program.js';

export const FIRST_OPPONENT = 'poky';
const opponentName = () => nickname(FIRST_OPPONENT) ?? 'the poky house bot';

// Two words for a name nobody has thought of yet: "Sneaky-Sagebrush". 24 × 24 pairs, and taken ones are skipped.
const FIRST = ['Sneaky', 'Brave', 'Fuzzy', 'Speedy', 'Mighty', 'Clever', 'Rusty', 'Jolly', 'Lucky', 'Bouncy', 'Grumpy', 'Dizzy',
  'Plucky', 'Zippy', 'Frosty', 'Muddy', 'Sunny', 'Wily', 'Noisy', 'Gentle', 'Stubborn', 'Scrappy', 'Sleepy', 'Tiny'];
const SECOND = ['Sagebrush', 'Pinecone', 'Snowdrift', 'Marmot', 'Juniper', 'Boulder', 'Canyon', 'Cricket', 'Beetle', 'Gopher', 'Burrow', 'Pebble',
  'Mesa', 'Thistle', 'Aspen', 'Ridge', 'Coyote', 'Tumbleweed', 'Chipmunk', 'Acorn', 'Magpie', 'Willow', 'Hedgehog', 'Bramble'];
const pick = (list) => list[Math.floor(Math.random() * list.length)];
export const randomName = () => `${pick(FIRST)}-${pick(SECOND)}`;

/** Whether this visit gets the welcome: never closed here, nobody playing here, no bot on the signed-in account. */
export const shouldWelcome = (account) => !introSeen() && !player() && !(account?.signedIn && account.bots?.length);

/**
 * Opens the welcome over the page. scope: the locker's (the dialog goes with it). account: GET /api/v1/account,
 * so a new badger goes on a signed-in Discord account. keep(): "Keep me signed in on this computer".
 */
export function openWelcome(scope, { account = null, keep = () => false } = {}) {
  document.querySelector('dialog.bb-intro')?.remove();
  const dialog = document.createElement('dialog');
  dialog.className = 'bb-intro';
  dialog.setAttribute('aria-labelledby', 'bb-intro-h');
  document.body.append(dialog);
  scope.cleanup(() => dialog.remove());

  const st = { view: 'welcome', name: randomName(), edited: false, busy: false, error: '' };

  function render() {
    setHtml(dialog, st.view === 'welcome' ? welcomeHtml() : startHtml());
    dialog.querySelector(st.view === 'welcome' ? '[data-intro-start]' : '#bb-intro-name, [data-intro-go]')?.focus();
  }

  function welcomeHtml() {
    return html`
      <div class="bb-intro-body">
        <p class="eyebrow">Welcome to ${GAME}</p>
        <h2 id="bb-intro-h">Build a bot. Watch it brawl.</h2>
        <p class="lede">${GAME} is a strategy game where you don’t click on anything in the match. You give your ${say('bot', 'badger')} a few rules, and the game server plays them for you.</p>
        <ol class="bb-intro-tiles">
          <li><b>Dig</b><span>Workers dig ${ORE}. ${say('Ore pays', 'Grubs pay')} for everything else.</span></li>
          <li><b>Build and train</b><span>Put up ${bWord('Barracks')} to train soldiers and archers, and take classes at the ${bWord('TechLab')} to make them stronger.</span></li>
          <li><b>Win</b><span>Knock down the other side’s ${bWord('CommandCenter')}. Lose all of yours and you’re out.</span></li>
        </ol>
        <p>Your bot is a short list of rules, each <b>when</b> something is true, <b>do</b> something. Every second it reads them from the top. Change a rule while the match plays and the change plays right away.</p>
        <div class="bb-intro-actions">
          <button type="button" class="btn start" data-intro-start>Start now: play in 10 seconds</button>
          <button type="button" class="btn ghost" data-intro-close>Look around first</button>
        </div>
        <p class="small muted">This won’t show again. <b>How it works</b> at the top of Block Bot brings it back.</p>
      </div>`;
  }

  function startHtml() {
    const me = player();
    const onAccount = account?.signedIn;
    return html`
      <form class="bb-intro-body" id="bb-intro-form">
        <p class="eyebrow">Your first match</p>
        <h2 id="bb-intro-h">${me ? `Ready, ${me.name}?` : `Meet your ${say('bot', 'badger')}`}</h2>
        ${me ? '' : html`
          <label for="bb-intro-name" class="bb-legend">Its name</label>
          <div class="bb-inline-form">
            <input id="bb-intro-name" required minlength="3" maxlength="32" pattern="[A-Za-z0-9_\\-]{3,32}" autocomplete="off" value="${st.name}" aria-describedby="bb-intro-name-help">
            <button type="button" class="btn ghost" data-intro-reroll>Another name</button>
          </div>
          <p class="small muted" id="bb-intro-name-help">Other players see it on the scoreboard. Type your own if you like (letters, numbers, - and _).${onAccount ? ' It goes on your Discord account.' : ''}</p>`}
        <ul class="bb-intro-plan">
          <li>You bring <b>${starters().first.label}</b>: four rules that dig, build ${bWord('Barracks')}, train soldiers and attack.</li>
          <li>You play <b>${opponentName()}</b>: slow, but it fights back.</li>
          <li>A coach shows you how to read your rules, then how to change one while the match plays.</li>
        </ul>
        <p class="bb-error" role="alert">${st.error}</p>
        <div class="bb-intro-actions">
          <button type="submit" class="btn start" data-intro-go ${st.busy ? 'disabled' : ''}>${st.busy ? 'Starting…' : 'Start the match'}</button>
          <button type="button" class="btn ghost" data-intro-back ${st.busy ? 'disabled' : ''}>Back</button>
        </div>
      </form>`;
  }

  function close() {
    markIntroSeen();
    if (dialog.open) dialog.close();
  }

  /** A new badger with st.name, or (when that name is taken and it wasn't typed) the next random one. */
  async function makeBadger() {
    const url = account?.signedIn ? '/api/v1/account/bots' : '/api/v1/players';
    for (let tries = 0; ; tries++) {
      try {
        const res = await scope.wait(api(url, { method: 'POST', body: { name: st.name } }));
        const me = { playerId: res.playerId, name: res.name, apiKey: res.apiKey };
        setPlayer(me, { keep: keep() });
        return me;
      } catch (e) {
        if (e.code !== 'NAME_TAKEN' || st.edited || tries >= 5) throw e;
        st.name = randomName();
      }
    }
  }

  /** The starter as it comes, if it's still here unchanged (the locker makes one on a first visit); else a new copy. */
  function firstBot() {
    const starter = starters().first.program;
    return Object.values(strategies()).find((s) => sameProgram(s.program, starter)) ?? newStrategy(starter);
  }

  async function go() {
    if (st.busy) return;
    const input = dialog.querySelector('#bb-intro-name');
    if (input && !input.reportValidity()) return;
    st.busy = true;
    st.error = '';
    render();
    try {
      if (!player()) await makeBadger();
      const s = firstBot();
      const match = await scope.wait(authed('/api/v1/blocks/matches', {
        method: 'POST',
        body: { program: s.program, mode: 'HouseBot', opponents: [FIRST_OPPONENT], tickIntervalMs: null },
      }));
      recordMatch(match.matchId, s.id);
      setCoaching({ matchId: match.matchId, step: 1 });
      close();
      location.hash = `#/blocks/match/${match.matchId}`;
    } catch (e) {
      st.busy = false;
      st.error = e.code === 'NAME_TAKEN' ? `Someone already has ${st.name}. Try another name.` : e.message;
      render();
    }
  }

  dialog.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    if ('introStart' in b.dataset) { st.view = 'start'; render(); }
    else if ('introClose' in b.dataset) close();
    else if ('introBack' in b.dataset) { st.view = 'welcome'; st.error = ''; render(); }
    else if ('introReroll' in b.dataset) {
      st.name = randomName();
      st.edited = false;
      const input = dialog.querySelector('#bb-intro-name');
      if (input) { input.value = st.name; input.focus(); }
    }
  });
  dialog.addEventListener('input', (e) => {
    if (e.target.id === 'bb-intro-name') { st.name = e.target.value.trim(); st.edited = true; }
  });
  dialog.addEventListener('submit', (e) => { e.preventDefault(); go(); });
  // Esc, or a click on the dim area around the box.
  dialog.addEventListener('cancel', () => markIntroSeen());
  dialog.addEventListener('click', (e) => { if (e.target === dialog && !st.busy) close(); });
  dialog.addEventListener('close', () => dialog.remove());

  render();
  dialog.showModal();
  dialog.querySelector('[data-intro-start]')?.focus();
}

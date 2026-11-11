// #/blocks: the locker. Name your badger (or load a key), pick a strategy, pick who to play.
import { api, html, raw, setHtml, timeAgo, botLabel, mountScope } from '../util.js';
import { GAME, say } from '../lore.js';
import {
  strategies, getStrategy, deleteStrategy, newStrategy, prefs, setPref, recordMatch,
  player, setPlayer, keptSignedIn, authed, themed, nav, downloadKey, downloadStrategy,
} from './store.js';
import { starters, LIMITS, describeRules, readStrategyFile } from './program.js';
import { ICON } from './icons.js';
import { syncing, SYNC_EVENT } from './sync.js';

export function mountLocker(main) {
  // Listeners go at teardown, and a request that answers after it never reaches the page (scope.wait).
  const scope = mountScope();
  const wait = scope.wait;
  const st = {
    mode: prefs().mode ?? 'HouseBot',
    one: prefs().one ?? 'sitter',
    many: prefs().many ?? ['rusher', 'economist'],
    fast: prefs().fast ?? false,
    selected: prefs().selected ?? null,
    bots: [],
    waiting: [],
    live: [],
    joinMatch: null,
    real: prefs().real ?? 'join',   // Real match: 'join' one that's waiting, or 'create' one for others to join
    seats: prefs().seats ?? 4,
    matchName: '',
    busy: false,
    switching: false, // "Change" on step 1: show the name / key form again without forgetting the current badger
    keep: keptSignedIn(),   // "Keep me signed in on this computer"; otherwise the key is forgotten when the tab closes
    error: '',
    fileError: '',
  };

  function ensureStarter() {
    if (Object.keys(strategies()).length === 0) {
      const s = newStrategy(starters().first.program);
      st.selected = s.id;
    }
  }
  ensureStarter();

  async function refresh() {
    try { st.bots = await wait(api('/api/v1/bots')); } catch { st.bots = []; }
    try {
      const waiting = await wait(api('/api/v1/matches?status=Waiting'));
      // Waiting practice matches, but not one I'm already in (one I created, say).
      const meId = player()?.playerId;
      st.waiting = waiting.filter((m) => m.category === 'Practice' && !m.heats && !m.players.some((p) => p.playerId === meId));
    } catch { st.waiting = []; }
    if (player()) {
      try { st.live = (await wait(authed(themed('/api/v1/blocks/matches')))).filter((m) => m.matchStatus !== 'Completed'); } catch { st.live = []; }
    }
    render();
  }

  function sorted() {
    return Object.values(strategies()).sort((a, b) => (b.updatedAt > a.updatedAt ? 1 : -1));
  }

  function render() {
    const list = sorted();
    if (!list.some((s) => s.id === st.selected)) st.selected = list[0]?.id ?? null;
    const me = player();
    const picked = list.find((s) => s.id === st.selected);
    const multi = st.mode === 'FreeForAll';
    const startLabel = {
      Sandbox: 'Start the sandbox',
      HouseBot: `Play vs ${botLabel(st.one)}`,
      FreeForAll: st.many.length >= 2 ? `Start a free-for-all (${st.many.length} bots)` : 'Pick 2 or 3 bots',
      Real: st.real === 'create' ? `Create a match for ${st.seats} players` : st.joinMatch ? 'Join the match' : 'Pick a match',
    }[st.mode];
    const canStart = me && picked && !st.busy && !(multi && (st.many.length < 2 || st.many.length > 3)) && !(st.mode === 'Real' && st.real === 'join' && !st.joinMatch);

    // One column, three steps: name your badger, pick a strategy, pick who to play.
    // Until the badger exists, steps 2 and 3 are dimmed and inert so the name is the only thing to do.
    const locked = !me;
    const lockTag = locked ? html`<span class="bb-lock">${raw(ICON.lock)} After step 1</span>` : '';
    const lockAttr = locked ? 'inert' : '';

    setHtml(main, html`
      <section class="bb-page bb-locker">
        <header class="bb-hero">
          <p class="eyebrow">${GAME}</p>
          <h1 class="headline">Block Bot</h1>
          <p class="lede">Build a bot out of rules, no typing code. The server plays it for you, and you can change the rules while it plays.</p>
        </header>

        ${st.live.length ? html`
          <section class="bb-card" aria-labelledby="bb-live-h">
            <h2 id="bb-live-h">Playing now</h2>
            <ul class="bb-live-list">
              ${st.live.map((m) => html`<li><a class="btn" href="#/blocks/match/${m.matchId}">Open the cockpit</a> <b>${m.programName || 'Strategy'}</b> <span class="muted">· ${m.matchStatus === 'Waiting' ? 'waiting for players' : `tick ${m.tick}`} · version ${m.version}</span></li>`)}
            </ul>
          </section>` : ''}

        ${me && !st.switching ? html`
          <section class="bb-stage" aria-labelledby="bb-s1-h">
            <div class="bb-stage-head">
              <span class="bb-stage-num done" aria-hidden="true">${raw(ICON.check)}</span>
              <h2 id="bb-s1-h">Playing as ${me.name}</h2>
              <button type="button" class="btn ghost small-btn" data-switch="1">Change</button>
            </div>
          </section>` : html`
          <section class="bb-card bb-stage bb-signup" aria-labelledby="bb-s1-h">
            <div class="bb-stage-head">
              <span class="bb-stage-num hot" aria-hidden="true">1</span>
              <h2 id="bb-s1-h">${me ? 'Play as someone else' : `Name your ${say('bot', 'badger')}`}</h2>
            </div>
            ${me ? html`
              <div class="bb-switching">
                <p>You’re playing as <b>${me.name}</b>. ${keptSignedIn()
                  ? 'Its key is kept in this browser on this computer'
                  : 'Its key is kept in this tab only, until you close it'}, so download it if you want to come back to it.</p>
                <div class="bb-actions">
                  <button type="button" class="btn ghost small-btn" data-dlkey="1">${raw(ICON.download)} Download ${me.name}’s key</button>
                  <button type="button" class="btn ghost small-btn" data-switch-cancel="1">Keep playing as ${me.name}</button>
                  <button type="button" class="btn ghost small-btn" data-signout="1">Sign out</button>
                </div>
              </div>
              <p class="muted">Pick a new name, or load a key you already have.</p>` : html`
              <p class="muted">This is the name other players see on the scoreboard. Letters, numbers, - and _, 3 to 32 of them.</p>`}
            <form id="bb-signup" class="bb-inline-form bb-signup-form">
              <label class="visually-hidden" for="bb-name">Name</label>
              <input id="bb-name" name="name" required minlength="3" maxlength="32" pattern="[A-Za-z0-9_\\-]{3,32}" placeholder="e.g. rowan-b" autocomplete="off">
              <button class="btn" type="submit">Make my ${say('bot', 'badger')}</button>
            </form>
            <details class="small"><summary>I already have an API key</summary>
              <form id="bb-key" class="bb-inline-form">
                <label class="visually-hidden" for="bb-key-in">API key</label>
                <input id="bb-key-in" name="key" required placeholder="Paste your key" autocomplete="off">
                <button class="btn ghost" type="submit">Use this key</button>
                <span class="muted">or</span>
                <label class="btn ghost bb-file">Browse for a key file<input type="file" accept=".txt,.json,.env,.key,text/plain,application/json" id="bb-keyfile"></label>
              </form>
            </details>
            <label class="bb-keep small"><input type="checkbox" id="bb-keep" ${st.keep ? 'checked' : ''}> Keep me signed in on this computer</label>
            <p class="bb-error" id="bb-signup-err" role="alert">${st.error}</p>
          </section>`}

        <section class="bb-stage${locked ? ' locked' : ''}" aria-labelledby="bb-s2-h" ${lockAttr}>
          <div class="bb-stage-head">
            <span class="bb-stage-num" aria-hidden="true">2</span>
            <h2 id="bb-s2-h">Pick a strategy to bring</h2>
            ${lockTag}
          </div>
          <p class="bb-error" role="alert">${st.fileError}</p>
          <div class="bb-grid">
            ${list.map((s) => html`
              <article class="bb-card bb-strategy${s.id === st.selected ? ' picked' : ''}">
                <button type="button" class="bb-pick" data-pick="${s.id}" aria-pressed="${s.id === st.selected}">
                  <span class="bb-sname">${s.program.name || 'Untitled'}</span>
                  <span class="small muted">${describeRules(s.program)} · changed ${timeAgo(s.updatedAt)}</span>
                  ${s.id === st.selected ? html`<span class="bb-bringing">Bringing this one</span>` : ''}
                </button>
                <div class="bb-actions">
                  <a class="btn small-btn" href="#/blocks/edit/${s.id}">Edit</a>
                  <button type="button" class="btn ghost small-btn" data-download="${s.id}">Download</button>
                  <button type="button" class="btn ghost small-btn" data-copy="${s.id}">Copy</button>
                  <button type="button" class="btn ghost small-btn" data-delete="${s.id}" aria-label="Delete ${s.program.name}">Delete</button>
                </div>
              </article>`)}
            <article class="bb-card bb-new">
              <h3>New strategy</h3>
              <p class="small muted">Start blank, or from a starter and change it.</p>
              <div class="bb-actions">
                ${Object.entries(starters()).map(([k, v]) => html`<button type="button" class="btn ${k === 'blank' ? '' : 'ghost'} small-btn" data-new="${k}">${v.label}</button>`)}
              </div>
            </article>
          </div>
          <div class="bb-stage-foot">
            <p class="small muted">${syncing()
              ? 'Saved to your account as you work, so they’re here on any computer you sign in on.'
              : 'Saved in this browser as you work. Download one to keep it safe or move it to another computer.'}</p>
            <label class="btn ghost small-btn bb-file">Open a strategy file<input type="file" accept=".json,application/json" id="bb-open"></label>
          </div>
        </section>

        <section class="bb-stage${locked ? ' locked' : ''}" id="bb-s3" aria-labelledby="bb-s3-h" ${lockAttr}>
          <div class="bb-stage-head">
            <span class="bb-stage-num" aria-hidden="true">3</span>
            <h2 id="bb-s3-h">Pick who to play</h2>
            ${lockTag}
          </div>
          <fieldset>
            <legend class="bb-legend">Where do you want to play?</legend>
            <div class="bb-modes">
              ${[['Sandbox', 'Sandbox', `Try a build against the ${botLabel('sitter')}, who only digs.`],
                 ['HouseBot', 'Vs a house bot', 'One opponent, any time.'],
                 ['FreeForAll', 'Free-for-all', 'You vs 2 or 3 house bots.'],
                 ['Real', 'Real match', 'Join or create a match with other players.']]
                .map(([id, name, desc]) => html`<button type="button" class="bb-mode" data-mode="${id}" aria-pressed="${st.mode === id}"><b>${name}</b><span class="small muted">${desc}</span></button>`)}
            </div>
          </fieldset>

          ${st.mode === 'HouseBot' || st.mode === 'FreeForAll' ? html`
            <fieldset>
              <legend class="bb-legend">${multi ? 'Pick 2 or 3 opponents' : 'Pick an opponent'}</legend>
              <div class="bb-bots">
                ${st.bots.map((b) => {
                  const on = multi ? st.many.includes(b.name) : st.one === b.name;
                  return html`<button type="button" class="bb-bot${multi ? ' multi' : ''}" data-bot="${b.name}" aria-pressed="${on}"><span class="bb-mark" aria-hidden="true">${on ? '✓' : ''}</span><span><b>${botLabel(b.name)}</b><span class="small muted">${shortBlurb(b.description)}</span></span></button>`;
                })}
              </div>
            </fieldset>` : ''}

          ${st.mode === 'Real' ? html`
            <div class="bb-seg bb-real-seg" role="group" aria-label="Join or create">
              <button type="button" data-real="join" aria-pressed="${st.real === 'join'}">Join a match</button>
              <button type="button" data-real="create" aria-pressed="${st.real === 'create'}">Create a match</button>
            </div>` : ''}

          ${st.mode === 'Real' && st.real === 'create' ? html`
            <fieldset>
              <legend class="bb-legend">How many players?</legend>
              <div class="bb-seg">
                ${[2, 3, 4].map((n) => html`<button type="button" data-seats="${n}" aria-pressed="${st.seats === n}">${n}</button>`)}
              </div>
              <p class="small muted">Everyone brings their own Block Bot (or a bot they wrote). It starts when every seat is taken, or when you start it from the cockpit.</p>
            </fieldset>
            <form id="bb-create" class="bb-inline-form">
              <label for="bb-match-name" class="small muted">A name others can type to find it (optional)</label>
              <input id="bb-match-name" maxlength="32" pattern="[A-Za-z0-9_\-]{3,32}" placeholder="e.g. period-3" autocomplete="off" value="${st.matchName}">
            </form>` : ''}

          ${st.mode === 'Real' && st.real === 'join' ? html`
            <fieldset>
              <legend class="bb-legend">Pick a match</legend>
              ${st.waiting.length ? html`<div class="bb-bots">${st.waiting.map((m) => html`
                <button type="button" class="bb-bot" data-match="${m.matchId}" aria-pressed="${st.joinMatch === m.matchId}">
                  <span class="bb-mark" aria-hidden="true">${st.joinMatch === m.matchId ? '✓' : ''}</span>
                  <span><b>${m.name || `Match ${m.matchId.slice(0, 8)}`}</b><span class="small muted">${m.players.length} of ${m.maxPlayers} seats${m.hostedBy ? ` · hosted by ${m.hostedBy}` : ''} · starts when ${m.hostedBy ? 'the host starts it' : 'full, or when its creator starts it'}</span></span>
                </button>`)}</div>`
                : html`<p class="small muted">No matches are waiting for players right now.</p>`}
              <form id="bb-code" class="bb-inline-form">
                <label for="bb-code-in" class="small muted">Or type the match name your teacher gave you</label>
                <input id="bb-code-in" maxlength="40" placeholder="e.g. cs1400-lab" autocomplete="off">
                <button class="btn ghost" type="submit">Find it</button>
              </form>
              <p class="small muted">League matches don't take Block Bots yet.</p>
            </fieldset>` : ''}

          ${st.mode !== 'Real' || st.real === 'create' ? html`
            <fieldset class="bb-speed">
              <legend class="bb-legend">Speed</legend>
              <div class="bb-seg">
                <button type="button" data-fast="0" aria-pressed="${!st.fast}">Normal</button>
                <button type="button" data-fast="1" aria-pressed="${st.fast}">Fast (4×)</button>
              </div>
            </fieldset>` : ''}

          <p class="bb-bring">${picked ? html`Bringing <b>${picked.program.name}</b>` : 'Pick a strategy in step 2'}</p>
          <button type="button" class="btn start bb-start" id="bb-start" ${canStart ? '' : 'disabled'}>${me ? startLabel : `Name your ${say('bot', 'badger')} first`}</button>
          <p class="bb-error" role="alert">${me ? st.error : ''}</p>
          <p class="small muted">Your bot plays on the game server. Close this tab and it keeps playing. Come back any time to watch or change it.</p>
        </section>
      </section>`);
  }

  scope.on(main, 'click', onClick);
  scope.on(main, 'submit', onSubmit);
  scope.on(main, 'change', onChange);
  scope.on(main, 'input', onInput);
  // Strategies that came down from (or went away on) the website account.
  scope.on(window, SYNC_EVENT, (e) => { if (e.detail?.strategies) render(); });

  function onClick(e) {
    const t = e.target.closest('button');
    if (!t) return;
    const d = t.dataset;
    if (d.switch) {
      st.switching = true;
      render();
      main.querySelector('#bb-name')?.focus();
    } else if (d.switchCancel) { st.switching = false; render(); }
    else if (d.signout) { setPlayer(null); st.switching = false; st.live = []; render(); main.querySelector('#bb-name')?.focus(); }
    else if (d.dlkey) downloadKey(player());
    else if (d.pick) { st.selected = d.pick; setPref('selected', d.pick); render(); }
    else if (d.download) downloadStrategy(getStrategy(d.download));
    else if (d.copy) {
      const s = getStrategy(d.copy);
      const c = newStrategy({ ...s.program, name: `${s.program.name} copy`.slice(0, LIMITS().maxNameLength) });
      st.selected = c.id;
      render();
    } else if (d.delete) {
      const s = getStrategy(d.delete);
      if (t.dataset.confirm === 'yes') { deleteStrategy(d.delete); render(); }
      else { t.dataset.confirm = 'yes'; t.textContent = `Delete ${s.program.name}?`; t.classList.add('danger'); }
    } else if (d.new) {
      const s = newStrategy(starters()[d.new].program);
      location.hash = `#/blocks/edit/${s.id}`;
    } else if (d.mode) { st.mode = d.mode; setPref('mode', d.mode); st.error = ''; render(); }
    else if (d.bot) {
      if (st.mode === 'FreeForAll') {
        st.many = st.many.includes(d.bot) ? st.many.filter((x) => x !== d.bot) : [...st.many, d.bot].slice(-3);
        setPref('many', st.many);
      } else { st.one = d.bot; setPref('one', d.bot); }
      render();
    } else if (d.match) { st.joinMatch = d.match; render(); }
    else if (d.real) { st.real = d.real; setPref('real', d.real); st.error = ''; render(); }
    else if (d.seats) { st.seats = Number(d.seats); setPref('seats', st.seats); render(); }
    else if (d.fast) { st.fast = d.fast === '1'; setPref('fast', st.fast); render(); }
    else if (t.id === 'bb-start') start();
  }

  async function onSubmit(e) {
    e.preventDefault();
    const err = main.querySelector('#bb-signup-err');
    if (e.target.id === 'bb-signup') {
      const name = e.target.name.value.trim();
      try {
        const res = await wait(api('/api/v1/players', { method: 'POST', body: { name } }));
        setPlayer({ playerId: res.playerId, name: res.name, apiKey: res.apiKey }, { keep: st.keep });
        signedIn();
      } catch (ex) { if (err) err.textContent = ex.message; }
    } else if (e.target.id === 'bb-create') {
      start();
    } else if (e.target.id === 'bb-key') {
      useKey(e.target.key.value.trim());
    } else if (e.target.id === 'bb-code') {
      const name = main.querySelector('#bb-code-in').value.trim();
      if (!name) return;
      try {
        const m = await wait(api(`/api/v1/matches/named/${encodeURIComponent(name)}`));
        if (m.status !== 'Waiting') throw new Error(`"${name}" has already ${m.status === 'Completed' ? 'finished' : 'started'}.`);
        if (!st.waiting.some((w) => w.matchId === m.matchId)) st.waiting.unshift(m);
        st.joinMatch = m.matchId;
        st.error = '';
      } catch (ex) { st.error = ex.status === 404 ? `There's no match called "${name}".` : ex.message; }
      render();
    }
  }

  // A new badger (or a different key) is stored: back to the done state and reload what it owns.
  function signedIn() {
    st.switching = false;
    st.error = '';
    st.live = [];
    refresh();
  }

  async function useKey(key) {
    const err = main.querySelector('#bb-signup-err');
    try {
      const me = await wait(api('/api/v1/players/me', { headers: { authorization: `Bearer ${key}` } }));
      setPlayer({ playerId: me.playerId, name: me.name, apiKey: key }, { keep: st.keep });
      signedIn();
    } catch (ex) { if (err) err.textContent = ex.status === 401 ? 'That key didn’t work.' : ex.message; }
  }

  // A key file is any small text file with an nrts_ key in it: the bare key, the saved
  // registration JSON, a .env line. The first key found wins.
  async function onKeyFile(file) {
    const err = main.querySelector('#bb-signup-err');
    const input = main.querySelector('#bb-key-in');
    if (file.size > 64 * 1024) { if (err) err.textContent = 'That file is too big to be a key file.'; return; }
    const key = (await wait(file.text())).match(/nrts_[A-Za-z0-9_-]{20,}/)?.[0];
    if (!key) { if (err) err.textContent = `There’s no API key (nrts_…) in ${file.name}.`; return; }
    if (input) input.value = key;
    useKey(key);
  }

  function onInput(e) {
    if (e.target.id === 'bb-match-name') st.matchName = e.target.value;
  }

  async function onChange(e) {
    if (e.target.id === 'bb-keep') { st.keep = e.target.checked; return; }
    if (e.target.id === 'bb-keyfile' && e.target.files?.[0]) {
      const file = e.target.files[0];
      e.target.value = '';
      return onKeyFile(file);
    }
    if (e.target.id !== 'bb-open' || !e.target.files?.[0]) return;
    try {
      const program = await wait(readStrategyFile(e.target.files[0]));
      const s = newStrategy(program);
      st.selected = s.id;
      st.fileError = '';
    } catch (ex) { st.fileError = ex.message; }
    render();
  }

  async function start() {
    const s = getStrategy(st.selected);
    if (!s || st.busy) return;
    st.busy = true;
    st.error = '';
    render();
    try {
      let match;
      if (st.mode === 'Real' && st.real === 'create') {
        match = await wait(authed('/api/v1/blocks/matches', {
          method: 'POST',
          body: { program: s.program, mode: 'Create', seats: st.seats, name: st.matchName.trim() || null, tickIntervalMs: st.fast ? 250 : null },
        }));
      } else if (st.mode === 'Real') {
        match = await wait(authed(`/api/v1/blocks/matches/${st.joinMatch}/join`, { method: 'POST', body: { program: s.program } }));
      } else {
        match = await wait(authed('/api/v1/blocks/matches', {
          method: 'POST',
          body: {
            program: s.program,
            mode: st.mode,
            opponents: st.mode === 'FreeForAll' ? st.many : st.mode === 'HouseBot' ? [st.one] : [],
            tickIntervalMs: st.fast ? 250 : null,
          },
        }));
      }
      recordMatch(match.matchId, s.id);
      location.hash = `#/blocks/match/${match.matchId}`;
    } catch (ex) {
      st.busy = false;
      if (ex.status === 401) {
        setPlayer(null);
        st.error = `This server doesn’t know your ${say('bot', 'badger')} yet. Pick a name above and try again.`;
        render();
        return;
      }
      st.error = ex.code === 'INVALID_PROGRAM' ? `Your strategy has a problem: ${ex.message}` : ex.message;
      render();
    }
  }

  render();
  if (nav.jumpToPlay) {
    nav.jumpToPlay = false;
    // After the router's scrollTo(0, 0).
    requestAnimationFrame(() => { if (scope.alive) main.querySelector('#bb-s3')?.scrollIntoView({ block: 'start' }); });
  }
  refresh();
  return () => scope.dispose();
}

function shortBlurb(desc) {
  // House bot descriptions start with the nickname ("The Grub Hoarder: booms..."): keep what it does.
  const s = String(desc || '');
  const i = s.indexOf(':');
  const rest = i > 0 && i < 30 ? s.slice(i + 1).trim() : s;
  return rest.charAt(0).toUpperCase() + rest.slice(1);
}

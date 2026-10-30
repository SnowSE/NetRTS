// #/colab: the classroom slides, then the Colab notebook's cells as blocks to paste into a notebook of your own.
import { html, raw, setHtml, esc } from './util.js';
import { GAME } from './lore.js';

/** The notebook's source: Python with '# %%' cell markers. tools/make_colab_notebook.py builds the .ipynb from it. */
const SOURCE = 'colab/badger_brawl.py';
const OPEN_IN_COLAB = 'https://colab.research.google.com/github/SnowSE/NetRTS/blob/main/src/NetRts.Server/wwwroot/colab/badger_brawl.ipynb';

// The slides are drawn on a 1920x1080 canvas; styles.css scales them to the viewer (.slide, --u).
const SLIDES = [
  html`<section class="slide s-navy s-cover">
    <img class="s-backdrop" src="images/trailer-poster.jpg" alt="Blue and orange badgers battle at the foot of a red-brick clock tower in the 3D view">
    <div class="s-cover-text">
      <p class="eyebrow">Snow College · Software Engineering</p>
      <h1 class="s-huge">Badger<br><span class="s-accent">Brawl</span></h1>
      <p class="s-lede">Write the bot. Command the badgers. Watch them brawl.</p>
    </div>
  </section>`,

  html`<section class="slide s-navy s-center">
    <p class="eyebrow">A real-time strategy game</p>
    <h2 class="s-big">Nobody plays it<br>with a mouse.</h2>
    <p class="s-lede s-soft">You write a program. It runs your army. Then you sit back and watch it win, or figure out why it didn’t.</p>
  </section>`,

  html`<section class="slide s-cream">
    <div><p class="eyebrow">The map</p><h2 class="s-title">It’s set right here in the Sanpete Valley</h2></div>
    <div class="s-row s-top">
      <div class="s-col">
        <p class="s-body">Every team defends a real Snow College building: the <b>Noyes Building</b>, <b>Greenwood Student Center</b>, the <b>Eccles Center</b>, the <b>Huntsman Library</b>.</p>
        <p class="s-body">Your badgers dig up grubs, move into Anderson Hall, and take classes at the Makerspace.</p>
      </div>
      <img class="s-shot" style="width:calc(960 * var(--u));height:calc(416 * var(--u))" src="images/spectator-3d.png" alt="The 3D view mid-battle: a dorm and a makerspace explode while badger soldiers besiege a campus hall" loading="lazy">
    </div>
  </section>`,

  html`<section class="slide s-navy">
    <div><p class="eyebrow">How a bot plays</p><h2 class="s-title">Every tick, your code gets one turn</h2></div>
    <div class="s-row s-middle">
      ${[['1', 'Look', 'The server tells your bot what its badgers can see.', 'GET /state'],
        ['2', 'Decide', 'Your Python function picks what every badger does next.', 'decide(state)'],
        ['3', 'Order', 'Send the orders back. The world moves forward one tick.', 'POST /commands']]
        .map(([n, title, text, code], i) => html`${i ? html`<span class="s-arrow" aria-hidden="true">→</span>` : ''}
          <div class="s-card"><p class="s-num">${n}</p><h3>${title}</h3><p>${text}</p><p class="s-mono s-blue">${code}</p></div>`)}
    </div>
    <p class="s-body">About once a second, over and over, until somebody’s HQ falls.</p>
  </section>`,

  html`<section class="slide s-cream">
    <div><p class="eyebrow">What you command</p><h2 class="s-title">Meet the clan</h2></div>
    <div class="s-row">
      ${[['Worker', 50, 'Digs up grubs and puts up buildings.'], ['Soldier', 100, 'Tough. Fights up close.'],
        ['Archer', 125, 'Shoots from four tiles away.'], ['Scout', 75, 'Twice as fast. Sees farther.'],
        ['Snow Mobile', 350, 'An old tool truck with a cannon on the roof.']]
        .map(([name, cost, text]) => html`<div class="s-card"><h3>${name}</h3><p class="s-mono s-accent">${cost} grubs</p><p>${text}</p></div>`)}
    </div>
    <p class="s-body"><b>Buildings:</b> your HQ trains workers. Housing trains fighters. The Makerspace researches upgrades. The Bell Tower shoots anything that gets too close.</p>
  </section>`,

  html`<section class="slide s-navy">
    <div><p class="eyebrow">Talking to the server</p><h2 class="s-title">Orders are just JSON</h2></div>
    <div class="s-orders">
      <pre class="s-code">{"type": "Gather", "units": "all",
 "unitType": "Worker", "targetId": 4}</pre><p class="s-body">Workers, go dig the grubs in pile 4.</p>
      <pre class="s-code">{"type": "Produce", "buildingId": 77,
 "unitType": "Soldier", "count": 3}</pre><p class="s-body">Housing 77, train three soldiers, please.</p>
      <pre class="s-code">{"type": "Attack", "units": "all",
 "unitType": "Soldier", "x": 56, "y": 56}</pre><p class="s-body">Every soldier: <span class="s-accent">charge!</span> Fight anything on the way to (56, 56).</p>
    </div>
  </section>`,

  html`<section class="slide s-navy">
    <div><p class="eyebrow">Your strategy</p><h2 class="s-title">Your whole bot is one function</h2></div>
    <div class="s-row s-top">
      <div class="s-col"><p class="s-body">One skill: idle workers go dig</p>
<pre class="s-code">def put_workers_to_work(s):
    orders = []
    for w in my_units(s, "Worker"):
        if w["activity"] == "Idle":
            pile = closest(s["resources"], w)
            orders.append(gather(w, pile))
    return orders</pre></div>
      <div class="s-col"><p class="s-body">Stack skills into a strategy</p>
<pre class="s-code">def decide(s):
    return (build_barracks(s)
        + train_workers(s)
        + train_soldiers(s)
        + put_workers_to_work(s)
        + attack_when_ready(s))</pre>
        <p class="s-body">Four skills like this beat the first house badger in about 30 seconds.</p></div>
    </div>
  </section>`,

  html`<section class="slide s-cream s-split">
    <img class="s-shot s-dark" style="width:calc(640 * var(--u));height:calc(640 * var(--u))" src="images/fog-of-war.png" alt="The map through the blue team’s eyes: only the area around its own campus is lit" loading="lazy">
    <div class="s-col">
      <p class="eyebrow">No peeking</p>
      <h2 class="s-title">Fog of war that’s actually fair</h2>
      <p class="s-body">Your bot sees only what its badgers can see. The server enforces it, so no bot can cheat its way past the fog.</p>
      <p class="s-body">Every map is a mirror image, so every starting campus is exactly as good as the others.</p>
    </div>
  </section>`,

  html`<section class="slide s-navy s-split">
    <div class="s-col">
      <p class="eyebrow">How you win</p>
      <h2 class="s-title">Every brawl ends with a box score</h2>
      <p class="s-body">Knock over every enemy HQ, or have the highest score when time runs out.</p>
      <p class="s-body">Score = <span class="s-accent">destruction</span> + <span class="s-accent">economy</span> + <span class="s-accent">survival</span>.</p>
    </div>
    <img class="s-shot" style="width:calc(900 * var(--u));height:calc(625 * var(--u))" src="images/victory.png" alt="The end of a brawl: a winner banner, each side’s badgers trained, lost and defeated, and the final scores" loading="lazy">
  </section>`,

  html`<section class="slide s-navy">
    <div><p class="eyebrow">Your turn</p><h2 class="s-title">Let’s play</h2></div>
    <div class="s-row s-top">
      <ol class="s-steps">
        <li><span>Open <b>colab.research.google.com</b> and start a <b>New notebook</b></span></li>
        <li><span>Go to <b class="s-blue">netrts.snowse.io/#/colab</b></span></li>
        <li><span>Copy each block into its own cell</span></li>
        <li><span>Run them top to bottom: <b>Shift+Enter</b></span></li>
        <li><span>Beat the Sleepy Sitter</span></li>
      </ol>
      <div class="s-qr"><img src="images/colab-qr.svg" alt="QR code for netrts.snowse.io/#/colab"></div>
    </div>
  </section>`,

  html`<section class="slide s-cream">
    <div><p class="eyebrow">Level up</p><h2 class="s-title">Then beat the house badgers</h2></div>
    <div class="s-row">
      ${[['Sleepy Sitter', 'sitter', 'Digs grubs. Never fights back. Your first win.'],
        ['Blue Badger', 'balanced', 'Steady economy, soldiers and archers, upgrades. Attacks in waves.'],
        ['Honey Badger', 'rusher', 'Doesn’t care. Rolls two Snow Mobiles at your campus.'],
        ['Grub Hoarder', 'economist', 'Out-digs everyone, hides behind bell towers, shows up late with a huge army.']]
        .map(([name, key, text]) => html`<div class="s-card"><h3>${name}</h3><p class="s-mono s-accent">vs="${key}"</p><p>${text}</p></div>`)}
    </div>
    <p class="s-body">The four-skill bot beats the Sitter and loses to the Blue Badger. Closing that gap is up to you.</p>
  </section>`,

  html`<section class="slide s-navy">
    <div><p class="eyebrow">Strategy</p><h2 class="s-title">Ways to get smarter</h2></div>
    <div class="s-grid">
      ${[['Mix in archers', 'Soldiers up front, archers shooting from behind them.'],
        ['Build more housing', 'Two barracks train an army twice as fast.'],
        ['Ring the bell towers', 'Guard towers shoot anything that comes near home.'],
        ['Study up', 'The Makerspace researches more damage, armor and speed.'],
        ['Send a scout', 'Find the enemy early, before they find you.'],
        ['Charge together', 'Gather at a meeting spot first, then attack as one.']]
        .map(([title, text]) => html`<div class="s-card"><h3 class="s-accent">${title}</h3><p>${text}</p></div>`)}
    </div>
  </section>`,

  html`<section class="slide s-orange s-center">
    <p class="eyebrow">The finale</p>
    <h2 class="s-big">The class brawl</h2>
    <pre class="s-code s-light">join_match("class-brawl")</pre>
    <p class="s-lede">Every bot on one map, up to 16 clans. Last HQ standing wins.</p>
  </section>`,

  html`<section class="slide s-cream">
    <div><p class="eyebrow">Open source</p><h2 class="s-title">What’s under the hood</h2></div>
    <div class="s-grid s-two">
      ${[['The engine', 'C# on .NET 10. Same map and same orders always give the same match, so any game can be replayed.'],
        ['The server', 'A web API that referees every match and enforces the fog of war. It runs in the cloud on Azure.'],
        ['The spectator view', 'A 2D map and a 3D WebGL valley, streamed live to any browser, phone to projector.'],
        ['The tests', 'Every change runs whole bot-vs-bot brawls automatically before it ships.']]
        .map(([title, text]) => html`<div class="s-rule"><h3>${title}</h3><p>${text}</p></div>`)}
    </div>
    <p class="s-mono s-body">github.com/SnowSE/NetRTS</p>
  </section>`,

  html`<section class="slide s-navy s-center">
    <h2 class="s-big">Keep <span class="s-accent">brawling</span></h2>
    <dl class="s-links">
      <dt>Play</dt><dd>netrts.snowse.io/#/colab</dd>
      <dt>Code</dt><dd>github.com/SnowSE/NetRTS</dd>
      <dt>Study</dt><dd>Software Engineering at Snow College</dd>
    </dl>
    <p class="eyebrow">Go Badgers</p>
  </section>`,
];

export function mountColab(root) {
  document.title = `Play with Google Colab · ${GAME}`;
  setHtml(root, html`
    <div class="colab">
      <header class="colab-head">
        <h1 class="headline">Play with Google Colab</h1>
        <p class="lede">No installs: write your bot in Python in your browser. Page through the slides, then copy the blocks below into a notebook of your own.</p>
      </header>

      <section class="deck" aria-roledescription="carousel" aria-label="Slides">
        <div class="deck-stage">
          <div class="deck-frame">
            ${SLIDES.map((slide, i) => html`<div class="deck-slide" role="group" aria-roledescription="slide" aria-label="${i + 1} of ${SLIDES.length}" ${i ? raw('hidden') : ''}>${slide}</div>`)}
          </div>
        </div>
        <div class="deck-bar">
          <button type="button" class="btn ghost" data-step="-1" aria-label="Previous slide">←</button>
          <span class="deck-count" aria-live="polite">1 / ${SLIDES.length}</span>
          <button type="button" class="btn ghost" data-step="1" aria-label="Next slide">→</button>
          <button type="button" class="btn ghost" data-full>Full screen</button>
          <span class="muted small">Arrow keys work too.</span>
        </div>
      </section>

      <section class="colab-how" aria-labelledby="colab-how-h">
        <h2 id="colab-how-h">Get set up</h2>
        <ol class="steps">
          <li><p>Open <a href="https://colab.research.google.com/" target="_blank" rel="noopener">colab.research.google.com</a>, sign in with a Google account, and choose <b>New notebook</b>.</p></li>
          <li><p>Copy <b>Block 1</b> below, paste it into the first cell, and press <b>Shift+Enter</b> to run it.</p></li>
          <li><p>Click <b>+ Code</b> for a new cell, paste the next block, and run that. Keep going, top to bottom.</p></li>
        </ol>
        <p class="muted">Rather start with every block already in place?
          <a href="${OPEN_IN_COLAB}" target="_blank" rel="noopener">Open the whole notebook in Colab</a>
          (then <b>File › Save a copy in Drive</b>), or <a href="colab/badger_brawl.ipynb" download>download it</a>.</p>
      </section>

      <section class="colab-blocks" aria-label="Code blocks" aria-busy="true"><p class="muted">Loading the code…</p></section>
    </div>`);

  const stopDeck = mountDeck(root.querySelector('.deck'));
  const blocks = root.querySelector('.colab-blocks');
  fetch(SOURCE)
    .then((r) => { if (!r.ok) throw new Error(r.statusText); return r.text(); })
    .then((source) => { setHtml(blocks, renderCells(source)); blocks.removeAttribute('aria-busy'); })
    .catch(() => setHtml(blocks, html`<p class="error">Couldn't load the code. <a href="${OPEN_IN_COLAB}" target="_blank" rel="noopener">Open the notebook in Colab</a> instead.</p>`));
  blocks.addEventListener('click', copyBlock);
  return stopDeck;
}

/* ------------------------------------------------------------------ the slide viewer */

function mountDeck(deck) {
  const stage = deck.querySelector('.deck-stage');
  const slides = [...deck.querySelectorAll('.deck-slide')];
  const count = deck.querySelector('.deck-count');
  let current = 0;

  const show = (i) => {
    current = Math.max(0, Math.min(slides.length - 1, i));
    slides.forEach((s, n) => { s.hidden = n !== current; });
    count.textContent = `${current + 1} / ${slides.length}`;
    deck.querySelector('[data-step="-1"]').disabled = current === 0;
    deck.querySelector('[data-step="1"]').disabled = current === slides.length - 1;
  };

  deck.addEventListener('click', (e) => {
    const step = e.target.closest('[data-step]');
    if (step) show(current + Number(step.dataset.step));
    if (e.target.closest('[data-full]')) {
      if (document.fullscreenElement) document.exitFullscreen(); else stage.requestFullscreen?.();
    }
  });

  // Arrow keys page the slides while they are on screen (or full screen).
  const onKey = (e) => {
    if (e.altKey || e.ctrlKey || e.metaKey || e.target.closest?.('input, textarea, select')) return;
    const r = stage.getBoundingClientRect();
    if (!document.fullscreenElement && (r.bottom < 0 || r.top > innerHeight)) return;
    const step = { ArrowRight: 1, PageDown: 1, ' ': document.fullscreenElement ? 1 : 0, ArrowLeft: -1, PageUp: -1 }[e.key];
    if (!step) return;
    e.preventDefault();
    show(current + step);
  };
  document.addEventListener('keydown', onKey);

  // Swipe on phones and tablets.
  let startX = null;
  stage.addEventListener('pointerdown', (e) => { startX = e.clientX; });
  stage.addEventListener('pointerup', (e) => {
    if (startX !== null && Math.abs(e.clientX - startX) > 50) show(current + (e.clientX < startX ? 1 : -1));
    startX = null;
  });

  show(0);
  return () => {
    document.removeEventListener('keydown', onKey);
    if (document.fullscreenElement === stage) document.exitFullscreen();
  };
}

/* ------------------------------------------------------------------ the notebook as blocks */

/** Splits the '# %%' source into cells: markdown becomes prose, code becomes numbered blocks with a Copy button. */
function renderCells(source) {
  const parts = source.replace(/\r\n/g, '\n').split(/^# %%(.*)\n/m);
  const out = [];
  let block = 0;
  // parts: [preamble, header1, body1, header2, body2, ...]; the first cell is the notebook's title, which this page replaces.
  for (let i = 3; i < parts.length; i += 2) {
    const body = parts[i + 1].replace(/^\n+|\n+$/g, '');
    if (parts[i].trim() === '[markdown]') {
      out.push(markdown(body.split('\n').map((l) => (l.startsWith('# ') ? l.slice(2) : l.replace(/^#+/, ''))).join('\n')));
    } else {
      block += 1;
      const long = body.split('\n').length > 40;
      // A '# @title' line is what Colab shows above the cell; show it here too.
      const title = body.match(/^# @title (.*?)(\s*\{.*\})?$/m)?.[1];
      out.push(`<div class="code-block${long ? ' long' : ''}">
        <div class="code-head"><span>Block ${block}${title ? ` · ${esc(title)}` : ''}</span><button type="button" class="btn ghost copy">Copy</button></div>
        <pre class="snippet"><code>${esc(body)}</code></pre></div>`);
    }
  }
  return raw(out.join('\n'));
}

async function copyBlock(e) {
  const button = e.target.closest('.copy');
  if (!button) return;
  const code = button.closest('.code-block').querySelector('code');
  try {
    await navigator.clipboard.writeText(code.textContent);
    button.textContent = 'Copied!';
  } catch {
    getSelection().selectAllChildren(code);   // no clipboard access: select it for Ctrl+C
    button.textContent = 'Press Ctrl+C';
  }
  setTimeout(() => { button.textContent = 'Copy'; }, 2000);
}

/** Just enough Markdown for the notebook's text cells: headings, paragraphs, lists, tables and fenced code. */
function markdown(text) {
  const inline = (s) => esc(s)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>')
    .replace(/\*([^*]+)\*/g, '<i>$1</i>')
    .replace(/\[([^\]]+)\]\((https?:[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
  const lines = text.split('\n');
  const out = [];
  for (let i = 0; i < lines.length;) {
    const line = lines[i];
    const heading = line.match(/^(#{2,3}) (.*)/);
    if (!line.trim()) {
      i += 1;
    } else if (heading) {
      out.push(`<h${heading[1].length}>${inline(heading[2])}</h${heading[1].length}>`);
      i += 1;
    } else if (line.startsWith('```')) {
      const code = [];
      for (i += 1; i < lines.length && !lines[i].startsWith('```'); i += 1) code.push(lines[i]);
      i += 1;
      out.push(`<pre class="snippet"><code>${esc(code.join('\n'))}</code></pre>`);
    } else if (line.startsWith('|')) {
      const rows = [];
      for (; i < lines.length && lines[i].startsWith('|'); i += 1) {
        if (!/^\|[\s|:-]+\|$/.test(lines[i])) rows.push(lines[i].slice(1, -1).split('|').map((c) => inline(c.trim())));
      }
      const [head, ...body] = rows;
      out.push(`<div class="table-wrap"><table><thead><tr>${head.map((c) => `<th scope="col">${c}</th>`).join('')}</tr></thead>
        <tbody>${body.map((r) => `<tr>${r.map((c) => `<td>${c}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`);
    } else if (line.startsWith('- ')) {
      const items = [];
      for (; i < lines.length && lines[i].startsWith('- '); i += 1) items.push(`<li>${inline(lines[i].slice(2))}</li>`);
      out.push(`<ul>${items.join('')}</ul>`);
    } else {
      const para = [];
      for (; i < lines.length && lines[i].trim() && !/^(#{2,3} |```|\||- )/.test(lines[i]); i += 1) para.push(lines[i]);
      out.push(`<p>${inline(para.join(' '))}</p>`);
    }
  }
  return out.join('\n');
}

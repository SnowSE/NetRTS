// #/api: the bot API as a walkthrough (register, start a match, read state, send orders) and then every
// endpoint, each with copy-and-paste samples in curl, Python, C# and JavaScript and a Run button.
// The endpoint list comes from the server's OpenAPI document, so new endpoints show up here on their own.
import { html, raw, setHtml } from './util.js';
import { GAME } from './lore.js';

const OPENAPI = '/openapi/v1.json';
const KEY_STORE = 'netrts-api-key';
const LANG_STORE = 'netrts-api-lang';
const PLACEHOLDER_KEY = 'nrts_your_key_here';

const LANGS = [
  { id: 'curl', label: 'curl' },
  { id: 'python', label: 'Python' },
  { id: 'csharp', label: 'C#' },
  { id: 'js', label: 'JavaScript' },
];

/** Example bodies, ready to send. Every endpoint that takes a body and is shown on this page has one. */
const EXAMPLES = {
  'POST /api/v1/players': { name: 'my-bot' },
  'POST /api/v1/matches': { houseBots: ['sitter'] },
  'POST /api/v1/matches/{matchId}/commands': {
    commands: [
      { type: 'Gather', units: 'idle', unitType: 'Worker', targetId: 4 },
      { type: 'Produce', buildingId: 30, unitType: 'Worker', count: 1 },
    ],
  },
  'POST /api/v1/matches/{matchId}/speed': { tickIntervalMs: 250 },
  'POST /api/v1/exhibitions': { bots: ['rusher', 'economist'] },
};

/** The reference, in the order a bot meets these calls. Endpoints not listed here land in "More". */
const GROUPS = [
  { title: 'Your bot', keys: ['POST /api/v1/players', 'GET /api/v1/players/me'] },
  {
    title: 'Playing a match',
    keys: [
      'POST /api/v1/matches', 'GET /api/v1/matches', 'GET /api/v1/matches/named/{name}', 'POST /api/v1/matches/{matchId}/join',
      'POST /api/v1/matches/{matchId}/leave', 'GET /api/v1/matches/{matchId}/map', 'GET /api/v1/matches/{matchId}/state',
      'POST /api/v1/matches/{matchId}/commands', 'DELETE /api/v1/matches/{matchId}/commands', 'POST /api/v1/matches/{matchId}/surrender',
      'POST /api/v1/matches/{matchId}/speed', 'POST /api/v1/matches/{matchId}/step',
    ],
  },
  {
    title: 'After a match',
    keys: [
      'GET /api/v1/matches/{matchId}/result', 'GET /api/v1/matches/{matchId}/replay', 'GET /api/v1/matches/{matchId}/frames',
      'GET /api/v1/matches/{matchId}/frames/{index}', 'GET /api/v1/matches/{matchId}/standings',
    ],
  },
  {
    title: 'Watching matches',
    keys: [
      'GET /api/v1/matches/{matchId}', 'GET /api/v1/matches/history', 'GET /api/v1/matches/archive', 'GET /api/v1/matches/{matchId}/spectate',
      'GET /api/v1/matches/{matchId}/spectate/stream', 'POST /api/v1/exhibitions',
    ],
  },
  {
    title: 'Players, ladders and rules',
    keys: [
      'GET /api/v1/rules', 'GET /api/v1/bots', 'GET /api/v1/leaderboard', 'GET /api/v1/players/{playerId}', 'GET /api/v1/achievements',
      'GET /api/v1/players/{playerId}/achievements', 'GET /api/v1/people', 'GET /api/v1/people/{slug}',
    ],
  },
];

/** Calls bots don't make: the website's own, the admins' and the recap job's. They stay in the full reference at /scalar. */
const HIDDEN_TAGS = new Set(['Admin', 'Accounts (website)', 'Recaps']);

/** A server-sent event stream never finishes, so the Run button would never come back. */
const NO_RUN = new Set(['GET /api/v1/matches/{matchId}/spectate/stream']);

/** What the bare parameter names mean. The OpenAPI document has the types; these say what to put in. */
const PARAM_HINTS = {
  matchId: 'From creating or joining a match.',
  playerId: 'A bot\'s id, from registering or any match.',
  name: 'The name the match was given.',
  waitForTick: 'Long-poll: answer once this tick has been simulated (0 waits for the start).',
  sinceTick: 'Include every event after this tick.',
  index: 'Chunk number, from 0 to chunkCount − 1.',
  fog: 'true: one player\'s fog of war.',
  slug: 'The name in a person\'s profile link, e.g. jordan.',
};

export function mountApiDocs(root) {
  document.title = `API · ${GAME}`;
  const session = {
    key: load('sessionStorage', KEY_STORE) ?? '',
    lang: LANGS.some((l) => l.id === load('localStorage', LANG_STORE)) ? load('localStorage', LANG_STORE) : 'python',
    playerId: '',
    matchId: '',
    tick: null,
    state: null,
  };
  let ops = new Map();   // 'METHOD /path' -> operation from the OpenAPI document
  let doc = null;
  let alive = true;
  let uid = 0;           // the walkthrough and the reference show some endpoints twice: keep their ids apart

  setHtml(root, html`
    <div class="apidocs">
      <header class="apidocs-head">
        <h1 class="headline">The ${GAME} API</h1>
        <p class="lede">A bot is any program that makes HTTP requests. Walk through your first match below, pressing
          <b>Run</b> to make each call from this page, then copy the code for your own bot.</p>
        <p class="small muted">Server: <code>${location.origin}</code> · Rules and strategy:
          <a href="https://github.com/SnowSE/NetRTS/blob/main/docs/bot-guide.md" target="_blank" rel="noopener">the bot guide</a> ·
          Every schema and field: <a href="/scalar">the full reference</a> ·
          <a href="#requests">Request files for your editor</a></p>
      </header>

      <section class="apidocs-bar" aria-label="Your key and language">
        <div class="field">
          <label for="api-key">Your API key</label>
          <div class="apidocs-key">
            <input id="api-key" type="password" autocomplete="off" spellcheck="false" placeholder="nrts_… (step 1 fills this in)">
            <button type="button" class="btn ghost" id="key-show" aria-pressed="false">Show</button>
            <button type="button" class="btn ghost" id="key-forget">Forget</button>
          </div>
          <p class="muted small" id="key-note"></p>
        </div>
        <div class="field">
          <label for="match-id">Match id</label>
          <input id="match-id" autocomplete="off" spellcheck="false" placeholder="step 3 fills this in">
          <p class="muted small" id="match-note"></p>
        </div>
        <fieldset class="field">
          <legend>Code samples in</legend>
          <div class="segmented">
            ${LANGS.map((l) => html`<input type="radio" name="api-lang" id="lang-${l.id}" value="${l.id}" ${l.id === session.lang ? raw('checked') : ''}>
              <label for="lang-${l.id}">${l.label}</label>`)}
          </div>
        </fieldset>
      </section>

      <section aria-labelledby="walk-h" class="apidocs-walk">
        <h2 id="walk-h">Your first match</h2>
        <div id="walk" aria-busy="true"><p class="muted">Loading the API description…</p></div>
      </section>

      <section aria-labelledby="ref-h" class="apidocs-ref">
        <h2 id="ref-h">Every endpoint</h2>
        <p class="muted">The calls a bot can make, with what each one takes. Open one to see its code and run it.
          Calls only the website, the admins and the recap job make are in the <a href="/scalar">full reference</a>.</p>
        <div id="ref"></div>
      </section>

      <section aria-labelledby="requests-h" id="requests" class="apidocs-files">
        <h2 id="requests-h">Request files for your editor</h2>
        <p>Rather click through the calls in your editor or an API client? Download the bot's calls, in the same order as above:</p>
        <div class="apidocs-downloads">
          <div class="apidocs-download">
            <h3><a href="requests/badger-brawl.http" download>badger-brawl.http</a></h3>
            <p class="small">For <b>VS Code</b> (with the REST Client extension), <b>Visual Studio</b> and <b>Rider</b>.
              Open it and click <b>Send Request</b> above any call. Paste your key into <code>@key</code> at the top,
              and each match id into <code>@matchId</code> as you go.</p>
          </div>
          <div class="apidocs-download">
            <h3><a href="requests/badger-brawl.postman_collection.json" download>badger-brawl.postman_collection.json</a></h3>
            <p class="small">For <b>Postman</b>, <b>Bruno</b> and <b>Insomnia</b>: <b>Import</b> the file. Registering saves your
              key, and starting a match saves its id, so the calls after them just work. Set <code>baseUrl</code> to
              point at another server.</p>
          </div>
        </div>
        <p class="small muted">For every endpoint, including the website's own, import <a href="${OPENAPI}">${OPENAPI}</a> into
          Postman, Bruno or Insomnia instead.</p>
      </section>
    </div>`);

  const $ = (id) => root.querySelector('#' + id);
  const walk = $('walk'), ref = $('ref');

  // ---- the key, the match and the language
  const keyInput = $('api-key'), matchInput = $('match-id');
  keyInput.value = session.key;
  const showKeyNote = () => {
    $('key-note').textContent = session.key
      ? 'Kept in this tab only (closing it forgets the key), and sent only to this server. The code samples never show it.'
      : 'Register in step 1, or paste the key of a bot you already have.';
  };
  showKeyNote();
  keyInput.addEventListener('input', () => setKey(keyInput.value.trim()));
  $('key-show').addEventListener('click', (e) => {
    const shown = keyInput.type === 'text';
    keyInput.type = shown ? 'password' : 'text';
    e.currentTarget.textContent = shown ? 'Show' : 'Hide';
    e.currentTarget.setAttribute('aria-pressed', String(!shown));
  });
  $('key-forget').addEventListener('click', () => { keyInput.value = ''; setKey(''); });
  matchInput.addEventListener('input', () => setMatch(matchInput.value.trim(), { fromInput: true }));
  root.querySelector('.apidocs-bar .segmented').addEventListener('change', (e) => {
    session.lang = e.target.value;
    save('localStorage', LANG_STORE, session.lang);
    root.querySelectorAll('.endpoint, .loop').forEach(renderCode);
  });

  function setKey(key) {
    session.key = key;
    if (key) save('sessionStorage', KEY_STORE, key); else forget('sessionStorage', KEY_STORE);
    if (keyInput.value !== key) keyInput.value = key;
    showKeyNote();
  }

  function setMatch(id, { fromInput = false } = {}) {
    if (id !== session.matchId) { session.tick = null; session.state = null; }
    session.matchId = id;
    if (!fromInput) matchInput.value = id;
    setHtml($('match-note'), id && /^[0-9a-fA-F-]{36}$/.test(id)
      ? html`<a href="#/match/${id}" target="_blank" rel="noopener">Watch it live</a> (opens a new tab)`
      : '');
    refreshSessionFields();
  }

  // ---- load the API description and draw the page
  fetch(OPENAPI)
    .then((r) => { if (!r.ok) throw new Error(r.statusText); return r.json(); })
    .then((d) => {
      if (!alive) return;
      doc = d;
      ops = operations(d);
      drawWalkthrough();
      drawReference();
      walk.removeAttribute('aria-busy');
    })
    .catch(() => {
      if (alive) setHtml(walk, html`<p class="error">Couldn't load the API description. Try the <a href="/scalar">full reference</a>.</p>`);
    });

  function drawWalkthrough() {
    const steps = [
      {
        key: 'POST /api/v1/players',
        title: 'Register your bot',
        text: html`<p>Pick a name (3–32 letters, digits, <code>_</code> or <code>-</code>). The answer holds your bot's
          <code>apiKey</code>. It is shown only once, so copy it somewhere safe (press <b>Show</b> in the key box): this page keeps it only
          until you close the tab. Already have a bot? Paste its key above and skip to step 2.</p>`,
        body: { name: `my-bot-${Math.random().toString(36).slice(2, 7)}` },
      },
      {
        key: 'GET /api/v1/players/me',
        title: 'Check your key works',
        text: html`<p>Every call that acts for your bot sends the key as <code>Authorization: Bearer nrts_…</code>. This one returns your bot's profile and record.</p>`,
      },
      {
        key: 'POST /api/v1/matches',
        title: 'Start a match against a house bot',
        text: html`<p>The Sleepy Sitter never fights back, so it's the one to beat first. With a house bot in the other seat the
          match starts at once. The answer's <code>matchId</code> goes in every call after this. A bot can have three matches
          running at once; starting another ends any it has stopped playing.</p>`,
      },
      {
        key: 'GET /api/v1/matches/{matchId}/state',
        title: 'Read what your badgers can see',
        text: html`<p>This is a <b>long-poll</b>: the server holds the request until tick <code>waitForTick</code> has been
          simulated, so your bot never needs to sleep. Ask for tick 0 first; each time you run it, this page asks for the next
          tick. Look for your <code>CommandCenter</code> in <code>buildings</code>, your workers in <code>units</code> and the grub
          deposits in <code>resources</code>.</p>`,
        query: () => ({ waitForTick: session.tick == null ? '0' : String(session.tick + 1), sinceTick: session.tick == null ? '' : String(session.tick) }),
      },
      {
        key: 'POST /api/v1/matches/{matchId}/commands',
        title: 'Send orders',
        text: html`<p>Orders are JSON. Once you have read the state, this example fills in your real ids: your HQ trains a worker,
          and your idle workers dig at the nearest grubs. Every command is checked at once, and the answer says which were accepted.
          The full list of commands is in <a href="https://github.com/SnowSE/NetRTS/blob/main/docs/bot-guide.md#commands" target="_blank" rel="noopener">the bot guide</a>.</p>`,
        body: () => exampleOrders(session.state),
      },
    ];
    setHtml(walk, html`
      <ol class="apidocs-steps">
        ${steps.map((s) => {
          const op = ops.get(s.key);
          if (!op) return '';
          return html`<li><h3>${s.title}</h3>${s.text}${endpoint(op, { open: true, step: s })}</li>`;
        })}
        <li>
          <h3>Put it in a loop</h3>
          <p>That's the whole game: read the state, decide, send orders, and wait for the next tick, until the match ends.
            Here is that loop as a complete bot, with a first strategy that keeps your workers digging. Fill in your key and match id and run it.</p>
          <div class="loop code-block long"><div class="code-head"><span></span><button type="button" class="btn ghost copy">Copy</button></div><pre class="snippet"><code></code></pre></div>
        </li>
        <li>
          <h3>See how it went</h3>
          <p>When the match ends (or you <code>surrender</code>), the result has the winner, the scores and your stats.</p>
          ${ops.get('GET /api/v1/matches/{matchId}/result') ? endpoint(ops.get('GET /api/v1/matches/{matchId}/result'), { open: true }) : ''}
        </li>
      </ol>`);
    walk.querySelectorAll('.endpoint, .loop').forEach(renderCode);
  }

  function drawReference() {
    const listed = new Set(GROUPS.flatMap((g) => g.keys));
    const more = [...ops.keys()].filter((k) => !listed.has(k) && !HIDDEN_TAGS.has(ops.get(k).tag));
    const groups = [...GROUPS, { title: 'More', keys: more }]
      .map((g) => ({ ...g, keys: g.keys.filter((k) => ops.has(k)) }))
      .filter((g) => g.keys.length);
    setHtml(ref, html`${groups.map((g) => html`
      <h3>${g.title}</h3>
      <div class="apidocs-list">${g.keys.map((k) => endpoint(ops.get(k), { open: false }))}</div>`)}`);
    ref.querySelectorAll('.endpoint').forEach(renderCode);
  }

  // ---- one endpoint: its form, its code and its Run button
  function endpoint(op, { open, step }) {
    const key = op.key;
    const params = op.parameters.filter((p) => p.in === 'path' || p.in === 'query');
    const preset = typeof step?.query === 'function' ? step.query() : {};
    const takesBody = Boolean(op.bodySchema) || key in EXAMPLES;
    const body = step?.body ? (typeof step.body === 'function' ? step.body() : step.body) : EXAMPLES[key] ?? {};
    const fields = bodyFields(op.bodySchema, doc);
    const n = ++uid;
    return html`
      <details class="endpoint" data-key="${key}" ${step ? raw('data-step') : ''} ${open ? raw('open') : ''}>
        <summary>
          <span class="verb ${op.method}">${op.method}</span>
          <code class="ep-path">${op.path}</code>
          ${op.auth ? html`<span class="tag" title="Send your API key">key</span>` : ''}
          <span class="ep-summary">${op.summary}</span>
        </summary>
        <div class="ep-body">
          ${params.length ? html`<div class="ep-params">${params.map((p) => {
            const values = enumValues(p.schema, doc);
            const value = p.name in preset ? preset[p.name] : sessionValue(p.name);
            const id = `ep${n}-${p.name}`;
            return html`<div class="field">
              <label for="${id}"><code>${p.name}</code>${p.required ? '' : html` <span class="muted small">optional</span>`}</label>
              ${values
                ? html`<select id="${id}" data-param="${p.name}" data-in="${p.in}"><option value="">(default)</option>${values.map((v) => html`<option>${v}</option>`)}</select>`
                : html`<input id="${id}" data-param="${p.name}" data-in="${p.in}" value="${value}" autocomplete="off" spellcheck="false"
                    placeholder="${p.schema?.default ?? ''}" ${isSessionParam(p.name) ? raw('data-session') : ''}>`}
              ${PARAM_HINTS[p.name] ? html`<p class="muted small">${PARAM_HINTS[p.name]}</p>` : ''}
            </div>`;
          })}</div>` : ''}
          ${takesBody ? html`<div class="field">
            <label for="ep${n}-body">Request body (JSON)</label>
            <textarea id="ep${n}-body" class="ep-json" data-body wrap="off" rows="${Math.min(14, pretty(body).split('\n').length + 1)}" spellcheck="false">${pretty(body)}</textarea>
            ${fields.length ? html`<details class="ep-fields"><summary class="small">What each field means</summary>
              <div class="table-wrap"><table>
                <thead><tr><th scope="col">Field</th><th scope="col">Type</th><th scope="col">Meaning</th></tr></thead>
                <tbody>${fields.map((f) => html`<tr><td><code>${f.name}</code></td><td class="muted">${f.type}</td><td>${f.description}</td></tr>`)}</tbody>
              </table></div></details>` : ''}
          </div>` : ''}
          <div class="code-block"><div class="code-head"><span></span><button type="button" class="btn ghost copy">Copy</button></div>
            <pre class="snippet"><code></code></pre></div>
          <div class="ep-run">
            ${NO_RUN.has(key)
              ? html`<p class="muted small">A stream of server-sent events that runs until the match ends: try it with the code above.</p>`
              : html`<button type="button" class="btn" data-run>Run</button>`}
            ${op.returns ? html`<span class="muted small">Returns <code>${op.returns}</code></span>` : ''}
            <span class="ep-status small" aria-live="polite"></span>
          </div>
          <pre class="snippet ep-response" hidden></pre>
        </div>
      </details>`;
  }

  /** Redraws one block of code for the language chosen: an endpoint's request, or the full bot loop. */
  function renderCode(el) {
    const loop = el.classList.contains('loop');
    const block = loop ? el : el.querySelector('.ep-body > .code-block');
    block.querySelector('.code-head span').textContent = LANGS.find((l) => l.id === session.lang).label;
    block.querySelector('pre code').textContent = loop ? loopProgram(session.lang, session.matchId) : sample(session.lang, request(el));
  }

  /** The request an endpoint's form describes now. */
  function request(el) {
    const op = ops.get(el.dataset.key);
    let path = op.path;
    const query = [];
    el.querySelectorAll('[data-param]').forEach((input) => {
      const value = input.value.trim();
      if (input.dataset.in === 'path') {
        path = path.replace(`{${input.dataset.param}}`, value ? encodeURIComponent(value) : placeholder(input.dataset.param));
      } else if (value) {
        query.push([input.dataset.param, value]);
      }
    });
    const textarea = el.querySelector('[data-body]');
    let body;
    if (textarea) {
      try { body = JSON.parse(textarea.value || 'null'); } catch { body = undefined; }
    }
    return { method: op.method, path, query, body, bodyText: textarea?.value, auth: op.auth, key: op.key };
  }

  // Typing in a form redraws its code; Copy and Run do what they say.
  root.addEventListener('input', (e) => {
    const ep = e.target.closest('.endpoint');
    if (!ep) return;
    if (e.target.matches('[data-body]')) e.target.dataset.edited = '1';
    if (e.target.matches('[data-session]')) e.target.dataset.edited = '1';
    renderCode(ep);
  });
  root.addEventListener('click', (e) => {
    if (e.target.closest('.copy')) copy(e.target.closest('.copy'));
    const run = e.target.closest('[data-run]');
    if (run) send(run.closest('.endpoint'), run);
  });

  async function send(el, button) {
    const req = request(el);
    const status = el.querySelector('.ep-status');
    const out = el.querySelector('.ep-response');
    if (req.bodyText !== undefined && req.body === undefined) {
      status.textContent = 'The request body isn\'t valid JSON.';
      status.className = 'ep-status small error';
      return;
    }
    const missing = [...el.querySelectorAll('[data-in="path"]')].find((input) => !input.value.trim());
    if (missing) {
      status.textContent = `Fill in ${missing.dataset.param} first.`;
      status.className = 'ep-status small error';
      return;
    }
    const headers = { accept: 'application/json' };
    if (req.auth) {
      if (!session.key) {
        status.textContent = 'This call needs your API key: register in step 1, or paste one at the top.';
        status.className = 'ep-status small error';
        return;
      }
      headers.authorization = `Bearer ${session.key}`;
    }
    if (req.body !== undefined && req.bodyText !== undefined) headers['content-type'] = 'application/json';
    button.disabled = true;
    status.className = 'ep-status small muted';
    status.textContent = req.key.endsWith('/state') ? 'Waiting for the tick…' : 'Sending…';
    const started = performance.now();
    try {
      const qs = req.query.length ? '?' + new URLSearchParams(req.query) : '';
      const res = await fetch(req.path + qs, {
        method: req.method,
        headers,
        body: req.bodyText !== undefined ? JSON.stringify(req.body) : undefined,
      });
      const text = await res.text();
      if (!alive) return;
      let json = null;
      try { json = text ? JSON.parse(text) : null; } catch { json = null; }
      const ms = Math.round(performance.now() - started);
      status.textContent = `${res.status} ${res.statusText || ''} · ${ms} ms`.replace('  ', ' ');
      status.className = `ep-status small ${res.ok ? 'ok' : 'error'}`;
      // The key goes in the key box, not on screen for anyone looking over a shoulder or at the projector.
      const masked = typeof json?.apiKey === 'string' ? { ...json, apiKey: 'nrts_… (in the key box above: press Show to see it)' } : json;
      const shown = masked !== null ? JSON.stringify(masked, null, 2) : text || '(no body)';
      out.textContent = shown.length > 200_000 ? shown.slice(0, 200_000) + '\n… (cut short here; the rest came too)' : shown;
      out.hidden = false;
      if (res.ok && json) remember(req.key, json);
    } catch {
      if (!alive) return;
      status.textContent = `Couldn't reach the ${GAME} server.`;
      status.className = 'ep-status small error';
    } finally {
      button.disabled = false;
    }
  }

  /** Keeps what the next calls need: the key from registering, the match id, and the tick from the state. */
  function remember(key, json) {
    if (typeof json.apiKey === 'string') setKey(json.apiKey);
    if (key === 'POST /api/v1/players' || key === 'GET /api/v1/players/me') session.playerId = json.playerId ?? session.playerId;
    if (typeof json.matchId === 'string' && /^POST |\/named\//.test(key)) setMatch(json.matchId);
    if (key === 'GET /api/v1/matches/{matchId}/state') {
      session.tick = json.tick;
      session.state = json;
    }
    refreshSessionFields();
  }

  /** Fills session values (match id, player id, the next tick, orders built from the state) into the forms that use them. */
  function refreshSessionFields() {
    root.querySelectorAll('.endpoint').forEach((el) => {
      el.querySelectorAll('[data-session]').forEach((input) => {
        if (input.dataset.edited) return;
        input.value = sessionValue(input.dataset.param);
      });
      if (el.hasAttribute('data-step') && el.dataset.key === 'GET /api/v1/matches/{matchId}/state') {
        el.querySelector('[data-param="waitForTick"]').value = session.tick == null ? '0' : String(session.tick + 1);
        el.querySelector('[data-param="sinceTick"]').value = session.tick == null ? '' : String(session.tick);
      }
      if (el.hasAttribute('data-step') && el.dataset.key === 'POST /api/v1/matches/{matchId}/commands') {
        const textarea = el.querySelector('[data-body]');
        if (!textarea.dataset.edited) textarea.value = pretty(exampleOrders(session.state));
      }
      renderCode(el);
    });
    root.querySelectorAll('.loop').forEach(renderCode);
  }

  function sessionValue(name) {
    return name === 'matchId' ? session.matchId : name === 'playerId' ? session.playerId : '';
  }

  return () => { alive = false; };
}

/* ------------------------------------------------------------------ the OpenAPI document */

/** 'METHOD /path' -> { key, method, path, summary, tag, auth, parameters, bodySchema, returns }. */
function operations(doc) {
  const out = new Map();
  for (const [path, item] of Object.entries(doc.paths ?? {})) {
    for (const [verb, op] of Object.entries(item)) {
      if (!['get', 'post', 'put', 'delete', 'patch'].includes(verb)) continue;
      const method = verb.toUpperCase();
      const key = `${method} ${path}`;
      const ok = Object.entries(op.responses ?? {}).find(([code]) => code.startsWith('2'))?.[1];
      const schema = ok?.content?.['application/json']?.schema;
      out.set(key, {
        key, method, path,
        summary: op.summary ?? '',
        tag: op.tags?.[0] ?? '',
        auth: Boolean(op.security?.length),
        parameters: op.parameters ?? [],
        bodySchema: resolve(op.requestBody?.content?.['application/json']?.schema, doc),
        returns: typeName(schema),
      });
    }
  }
  return out;
}

function resolve(schema, doc) {
  if (!schema) return null;
  if (schema.$ref) return doc.components?.schemas?.[schema.$ref.split('/').pop()] ?? null;
  const ref = schema.oneOf?.find((s) => s.$ref);
  return ref ? resolve(ref, doc) : schema;
}

function typeName(schema) {
  if (!schema) return '';
  if (schema.$ref) return schema.$ref.split('/').pop();
  if (schema.type === 'array' || schema.type?.includes?.('array')) return `${typeName(schema.items)}[]`;
  const ref = schema.oneOf?.find((s) => s.$ref);
  return ref ? typeName(ref) : '';
}

function enumValues(schema, doc) {
  const resolved = resolve(schema, doc);
  return resolved?.enum?.filter((v) => v != null) ?? null;
}

/** A body's fields, plus one level into an object or array of objects (a command's fields, say). */
function bodyFields(schema, doc, prefix = '', depth = 0) {
  if (!schema?.properties) return [];
  return Object.entries(schema.properties).flatMap(([name, prop]) => {
    const target = resolve(prop, doc);
    const items = resolve(target?.items, doc);
    const description = prop.description ?? prop.oneOf?.find((s) => s.description)?.description ?? target?.description ?? '';
    const row = { name: prefix + name, type: fieldType(prop, doc), description };
    if (depth > 0) return [row];
    const inner = items?.properties ? bodyFields(items, doc, `${prefix}${name}[].`, 1)
      : target?.properties ? bodyFields(target, doc, `${prefix}${name}.`, 1) : [];
    return [row, ...inner];
  });
}

/** A field's type as a reader wants it: "integer", "string[]", "Move | Attack | …", "MatchSettingsDto". */
function fieldType(prop, doc) {
  const target = resolve(prop, doc);
  if (target?.enum) return target.enum.filter((v) => v != null).join(' | ');
  const ref = prop?.$ref ?? prop?.oneOf?.find((s) => s.$ref)?.$ref;
  if (ref) return ref.split('/').pop();
  const types = [].concat(target?.type ?? []).filter((t) => t !== 'null');
  if (types.includes('array')) return `${fieldType(target.items ?? {}, doc)}[]`;
  if (types.includes('integer')) return 'integer';   // also accepted as a numeric string
  return types.join(' or ') || 'any';
}

/* ------------------------------------------------------------------ code samples */

const PLACEHOLDERS = { matchId: 'MATCH_ID', playerId: 'PLAYER_ID', index: '0', name: 'MATCH_NAME', slug: 'SLUG' };
const placeholder = (name) => PLACEHOLDERS[name] ?? name.toUpperCase();

/** One request as code: setup lines, the call, and printing the answer. */
function sample(lang, req) {
  const origin = location.origin;
  const qs = req.query.length ? '?' + req.query.map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&') : '';
  const hasBody = req.bodyText !== undefined;
  const body = req.body === undefined ? null : req.body;
  const bodyJson = hasBody ? (req.body === undefined ? req.bodyText : pretty(body)) : '';
  switch (lang) {
    case 'curl': {
      const lines = [`curl${req.method === 'GET' ? '' : ` -X ${req.method}`} "${origin}${req.path}${qs}"`];
      if (req.auth) lines.push(`  -H "Authorization: Bearer $NETRTS_KEY"`);
      if (hasBody) {
        lines.push(`  -H "Content-Type: application/json"`);
        lines.push(`  -d '${(req.body === undefined ? req.bodyText : JSON.stringify(body)).replace(/'/g, "'\\''")}'`);
      }
      return (req.auth ? `export NETRTS_KEY=${PLACEHOLDER_KEY}\n\n` : '') + lines.join(' \\\n');
    }
    case 'python': {
      const args = [`f"{SERVER}${req.path.replace(/[{}]/g, (c) => c + c)}"`];
      if (req.query.length) args.push(`params={${req.query.map(([k, v]) => `"${k}": ${pyValue(v)}`).join(', ')}}`);
      if (req.auth) args.push('headers={"Authorization": f"Bearer {API_KEY}"}');
      if (hasBody) args.push(`json=${python(pretty(body, 4, 4))}`);
      if (req.key.endsWith('/state')) args.push('timeout=60');
      return [
        'import requests',
        '',
        `SERVER = "${origin}"`,
        ...(req.auth ? [`API_KEY = "${PLACEHOLDER_KEY}"`] : []),
        '',
        `r = requests.${req.method.toLowerCase()}(${args.length > 2 || hasBody ? '\n    ' + args.join(',\n    ') + ',\n' : args.join(', ')})`,
        'r.raise_for_status()',
        req.key.endsWith('/frames/{index}') ? 'print(r.text)' : 'print(r.json() if r.content else r.status_code)',
      ].join('\n');
    }
    case 'csharp': {
      const call = {
        GET: `await http.GetAsync("${req.path}${qs}")`,
        DELETE: `await http.DeleteAsync("${req.path}${qs}")`,
      }[req.method] ?? `await http.${req.method === 'PUT' ? 'PutAsync' : 'PostAsync'}("${req.path}${qs}", ${hasBody ? 'new StringContent(body, Encoding.UTF8, "application/json")' : 'null'})`;
      return [
        ...(req.auth ? ['using System.Net.Http.Headers;'] : []),
        ...(hasBody ? ['using System.Text;'] : []),
        ...(req.auth || hasBody ? [''] : []),
        `var http = new HttpClient { BaseAddress = new Uri("${origin}") };`,
        ...(req.auth ? [`http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "${PLACEHOLDER_KEY}");`] : []),
        ...(hasBody ? ['', 'var body = """', ...indent(bodyJson, 4).split('\n'), '    """;'] : []),
        '',
        `var response = ${call};`,
        'response.EnsureSuccessStatusCode();',
        'Console.WriteLine(await response.Content.ReadAsStringAsync());',
      ].join('\n');
    }
    default: {
      const init = [`method: "${req.method}"`];
      const headers = [
        ...(req.auth ? ['Authorization: `Bearer ${API_KEY}`'] : []),
        ...(hasBody ? ['"Content-Type": "application/json"'] : []),
      ];
      if (headers.length) init.push(`headers: { ${headers.join(', ')} }`);
      if (hasBody) init.push(`body: JSON.stringify(${req.body === undefined ? req.bodyText : pretty(body, 2, 2)})`);
      return [
        `const SERVER = "${origin}";`,
        ...(req.auth ? [`const API_KEY = "${PLACEHOLDER_KEY}";`] : []),
        '',
        `const res = await fetch(\`\${SERVER}${req.path}${qs}\`${req.method === 'GET' && !headers.length ? '' : `, {\n  ${init.join(',\n  ')},\n}`});`,
        'if (!res.ok) throw new Error(await res.text());',
        req.key.endsWith('/frames/{index}') ? 'console.log(await res.text());' : 'console.log(res.status === 204 ? "done" : await res.json());',
      ].join('\n');
    }
  }
}

/** A complete bot: the long-poll loop and a first decide() that keeps idle workers digging. */
function loopProgram(lang, matchId) {
  const origin = location.origin;
  const match = matchId || 'MATCH_ID';
  switch (lang) {
    case 'curl': return `#!/usr/bin/env bash
# Needs curl and jq. Idle workers dig at the grubs nearest the first of them.
SERVER="${origin}"
KEY="${PLACEHOLDER_KEY}"
MATCH="${match}"

tick=0
while :; do
  state=$(curl -s -H "Authorization: Bearer $KEY" "$SERVER/api/v1/matches/$MATCH/state?waitForTick=$tick")
  [ "$(jq -r .status <<<"$state")" = Completed ] && break

  orders=$(jq -c '.you.slot as $me
    | [.units[] | select(.owner == $me and .type == "Worker" and .activity == "Idle")] as $idle
    | if ($idle | length) == 0 or (.resources | length) == 0 then {commands: []}
      else $idle[0] as $w
        | (.resources | min_by((.x - $w.x | fabs) + (.y - $w.y | fabs))) as $ore
        | {commands: [{type: "Gather", unitIds: [$idle[].id], targetId: $ore.id}]} end' <<<"$state")
  if [ "$(jq '.commands | length' <<<"$orders")" -gt 0 ]; then
    curl -s -X POST "$SERVER/api/v1/matches/$MATCH/commands" -H "Authorization: Bearer $KEY" \\
      -H "Content-Type: application/json" -d "$orders" > /dev/null
  fi
  tick=$(( $(jq .tick <<<"$state") + 1 ))
done
curl -s "$SERVER/api/v1/matches/$MATCH/result" | jq .`;

    case 'python': return `import requests

SERVER = "${origin}"
API_KEY = "${PLACEHOLDER_KEY}"
MATCH_ID = "${match}"
AUTH = {"Authorization": f"Bearer {API_KEY}"}


def decide(state):
    """This tick's orders. A start: idle workers dig at the grubs nearest the first of them."""
    me = state["you"]["slot"]
    idle = [u for u in state["units"] if u["owner"] == me and u["type"] == "Worker" and u["activity"] == "Idle"]
    if not idle or not state["resources"]:
        return []
    w = idle[0]
    ore = min(state["resources"], key=lambda r: abs(r["x"] - w["x"]) + abs(r["y"] - w["y"]))
    return [{"type": "Gather", "unitIds": [u["id"] for u in idle], "targetId": ore["id"]}]


def get_state(wait_for, since=None):
    params = {"waitForTick": wait_for} if since is None else {"waitForTick": wait_for, "sinceTick": since}
    r = requests.get(f"{SERVER}/api/v1/matches/{MATCH_ID}/state", params=params, headers=AUTH, timeout=60)
    r.raise_for_status()
    return r.json()


state = get_state(0)  # waits for the match to start
while state["status"] != "Completed":
    orders = decide(state)
    if orders:
        r = requests.post(f"{SERVER}/api/v1/matches/{MATCH_ID}/commands", json={"commands": orders}, headers=AUTH)
        r.raise_for_status()
    state = get_state(state["tick"] + 1, since=state["tick"])

print(requests.get(f"{SERVER}/api/v1/matches/{MATCH_ID}/result").json())`;

    case 'csharp': return `using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var http = new HttpClient { BaseAddress = new Uri("${origin}"), Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "${PLACEHOLDER_KEY}");
var matchId = "${match}";

var state = await GetState(0);   // waits for the match to start
while ((string?)state["status"] != "Completed")
{
    var orders = Decide(state);
    if (orders.Count > 0)
    {
        var sent = await http.PostAsJsonAsync($"/api/v1/matches/{matchId}/commands", new JsonObject { ["commands"] = orders });
        sent.EnsureSuccessStatusCode();
    }
    var tick = (int)state["tick"]!;
    state = await GetState(tick + 1, tick);
}
Console.WriteLine(await http.GetStringAsync($"/api/v1/matches/{matchId}/result"));

async Task<JsonNode> GetState(int waitForTick, int? sinceTick = null) =>
    (await http.GetFromJsonAsync<JsonNode>(
        $"/api/v1/matches/{matchId}/state?waitForTick={waitForTick}" + (sinceTick is { } s ? $"&sinceTick={s}" : "")))!;

// This tick's orders. A start: idle workers dig at the grubs nearest the first of them.
static JsonArray Decide(JsonNode state)
{
    var me = (int)state["you"]!["slot"]!;
    var idle = state["units"]!.AsArray()
        .Where(u => (int)u!["owner"]! == me && (string?)u["type"] == "Worker" && (string?)u["activity"] == "Idle")
        .ToList();
    var deposits = state["resources"]!.AsArray();
    if (idle.Count == 0 || deposits.Count == 0) return [];
    var w = idle[0]!;
    var ore = deposits.MinBy(r => Math.Abs((int)r!["x"]! - (int)w["x"]!) + Math.Abs((int)r!["y"]! - (int)w["y"]!))!;
    return [new JsonObject
    {
        ["type"] = "Gather",
        ["unitIds"] = new JsonArray(idle.Select(u => JsonValue.Create((int)u!["id"]!)).ToArray<JsonNode?>()),
        ["targetId"] = (int)ore["id"]!,
    }];
}`;

    default: return `// Node 18 or later: save as bot.mjs and run "node bot.mjs".
const SERVER = "${origin}";
const API_KEY = "${PLACEHOLDER_KEY}";
const MATCH_ID = "${match}";
const auth = { Authorization: \`Bearer \${API_KEY}\` };

// This tick's orders. A start: idle workers dig at the grubs nearest the first of them.
function decide(state) {
  const me = state.you.slot;
  const idle = state.units.filter((u) => u.owner === me && u.type === "Worker" && u.activity === "Idle");
  if (!idle.length || !state.resources.length) return [];
  const w = idle[0];
  const dist = (r) => Math.abs(r.x - w.x) + Math.abs(r.y - w.y);
  const ore = state.resources.reduce((a, b) => (dist(b) < dist(a) ? b : a));
  return [{ type: "Gather", unitIds: idle.map((u) => u.id), targetId: ore.id }];
}

async function getState(waitForTick, sinceTick) {
  const qs = sinceTick === undefined ? \`waitForTick=\${waitForTick}\` : \`waitForTick=\${waitForTick}&sinceTick=\${sinceTick}\`;
  const res = await fetch(\`\${SERVER}/api/v1/matches/\${MATCH_ID}/state?\${qs}\`, { headers: auth });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

let state = await getState(0); // waits for the match to start
while (state.status !== "Completed") {
  const orders = decide(state);
  if (orders.length) {
    const res = await fetch(\`\${SERVER}/api/v1/matches/\${MATCH_ID}/commands\`, {
      method: "POST",
      headers: { ...auth, "Content-Type": "application/json" },
      body: JSON.stringify({ commands: orders }),
    });
    if (!res.ok) throw new Error(await res.text());
  }
  state = await getState(state.tick + 1, state.tick);
}
console.log(await (await fetch(\`\${SERVER}/api/v1/matches/\${MATCH_ID}/result\`)).json());`;
  }
}

/**
 * Orders worth sending, built from a state when there is one: the HQ trains a worker and the idle
 * workers dig at the grubs nearest it. Without a state, the generic example.
 */
function exampleOrders(state) {
  if (!state?.you) return EXAMPLES['POST /api/v1/matches/{matchId}/commands'];
  const me = state.you.slot;
  const hq = state.buildings?.find((b) => b.owner === me && b.type === 'CommandCenter');
  const from = hq ?? state.units?.find((u) => u.owner === me);
  const near = (r) => (from ? Math.abs(r.x - from.x) + Math.abs(r.y - from.y) : 0);
  const ore = state.resources?.length ? state.resources.reduce((a, b) => (near(b) < near(a) ? b : a)) : null;
  const commands = [];
  if (ore) commands.push({ type: 'Gather', units: 'idle', unitType: 'Worker', targetId: ore.id });
  if (hq) commands.push({ type: 'Produce', buildingId: hq.id, unitType: 'Worker', count: 1 });
  return commands.length ? { commands } : EXAMPLES['POST /api/v1/matches/{matchId}/commands'];
}

/**
 * JSON for reading: anything that fits on a line stays on one ({"type": "Gather", "targetId": 4}), the
 * rest is indented by step. pad is the column the text starts at, so it can sit inside a call.
 */
function pretty(value, step = 2, pad = 0) {
  if (value === null || typeof value !== 'object') return JSON.stringify(value);
  const list = Array.isArray(value);
  const parts = list
    ? value.map((v) => pretty(v, step, pad + step))
    : Object.entries(value).map(([k, v]) => `${JSON.stringify(k)}: ${pretty(v, step, pad + step)}`);
  const [open, close] = list ? ['[', ']'] : ['{', '}'];
  const flat = `${open}${parts.join(', ')}${close}`;
  if (!flat.includes('\n') && pad + flat.length <= 88) return flat;
  return `${open}\n${parts.map((p) => ' '.repeat(pad + step) + p).join(',\n')}\n${' '.repeat(pad)}${close}`;
}

/** JSON text as a Python literal: true, false and null outside strings become True, False and None. */
const python = (json) => json.replace(/("(?:[^"\\]|\\.)*")|\b(true|false|null)\b/g,
  (m, str, word) => str ?? { true: 'True', false: 'False', null: 'None' }[word]);

const pyValue = (v) => (/^-?\d+$/.test(v) ? v : v === 'true' ? 'True' : v === 'false' ? 'False' : `"${v.replace(/["\\]/g, '\\$&')}"`);
const indent = (text, n) => text.split('\n').map((l) => ' '.repeat(n) + l).join('\n');
const isSessionParam = (name) => name === 'matchId' || name === 'playerId';

async function copy(button) {
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

// The key lives in sessionStorage, so a shared lab computer forgets it when the tab closes; the language
// choice in localStorage. Either can be blocked (private windows, strict settings); the page works without them.
// Even reading window.sessionStorage can throw, so each store is looked up by name inside the try.
function load(store, key) { try { return window[store].getItem(key); } catch { return null; } }
function save(store, key, value) { try { window[store].setItem(key, value); } catch { /* not kept */ } }
function forget(store, key) { try { window[store].removeItem(key); } catch { /* nothing kept */ } }

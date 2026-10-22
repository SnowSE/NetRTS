// The nightly recap show: last night's episode on the home page, and every episode on #/recaps.
import { api, html, setHtml } from './util.js';
import { GAME, say } from './lore.js';

export const SHOW = say('NetRTS Nightly', 'Badger Brawl Tonight');

const when = (date) => new Date(`${date}T12:00:00`).toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' });
const length = (s) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;

/** A player for one episode: poster until it's played, so nothing downloads until someone asks. */
function player(e) {
  return html`<video class="recap-video" controls preload="none" playsinline
    ${e.thumbnailUrl ? html`poster="${e.thumbnailUrl}"` : ''} src="${e.videoUrl}" aria-label="${SHOW}: ${e.title}"></video>`;
}

/** New episodes arrive overnight; an open page looks again this often, and whenever it's shown again. */
const CHECK_EVERY_MS = 5 * 60 * 1000;

/**
 * Calls refresh() now, every few minutes, and when the tab comes back into view, but never while a
 * video on the page is playing. Returns a function that stops it.
 */
function keepFresh(root, refresh) {
  const check = () => {
    if (document.hidden || [...root.querySelectorAll('video')].some((v) => !v.paused && !v.ended)) return;
    refresh();
  };
  const timer = setInterval(check, CHECK_EVERY_MS);
  const onVisible = () => { if (!document.hidden) check(); };
  document.addEventListener('visibilitychange', onVisible);
  refresh();
  return () => { clearInterval(timer); document.removeEventListener('visibilitychange', onVisible); };
}

/**
 * Keeps the home page's recap card showing the newest episode (hidden until there is one). Returns a
 * function that stops it.
 */
export function showLatestRecap(section) {
  let shown = null;
  return keepFresh(section, async () => {
    let e;
    try {
      e = await api('/api/v1/recaps/latest');
    } catch {
      return;
    }
    if (!section.isConnected || (shown && shown.id === e.id && shown.title === e.title)) return;
    shown = e;
    renderLatest(section, e);
  });
}

function renderLatest(section, e) {
  section.hidden = false;
  setHtml(section, html`
    <div class="section-head">
      <h2 id="recap-h">${say("Last night's recap", "Last night's brawl recap")}</h2>
      <a class="small" href="#/recaps">All episodes</a>
    </div>
    <div class="recap-card">
      ${player(e)}
      <div class="recap-meta">
        <p class="recap-title">${e.title}</p>
        <p class="muted small">${SHOW} · ${when(e.date)} · ${length(e.durationSeconds)}</p>
        ${e.summary ? html`<p class="small">${e.summary}</p>` : ''}
      </div>
    </div>`);
}

/** #/recaps: every episode, newest first, with its transcript. #/recaps/<id> opens one. */
export function mountRecaps(root, openId) {
  document.title = `Recaps · ${GAME}`;
  setHtml(root, html`<div class="recaps">
    <h1 class="headline">${SHOW}</h1>
    <p class="lede">Every night our three ${say('', 'badger ')}commentators, Zip the Scout, Sarge the Soldier and Quill the Archer,
      run down the day: new ${say('bots', 'badgers')}, new strategies, the ladder, the upsets and who's gone missing.
      New episodes show up here on their own.</p>
    <div id="episodes" aria-busy="true" aria-live="polite"><p class="empty">Loading episodes…</p></div>
  </div>`);
  const el = root.querySelector('#episodes');
  let shown = null;   // what's on the page: ids and titles, so a refresh only redraws when something changed
  let opened = false;

  const stop = keepFresh(root, async () => {
    let list;
    try {
      list = await api('/api/v1/recaps?limit=365');
    } catch (e) {
      if (shown === null && el.isConnected) setHtml(el, html`<p class="error">Could not load the episodes: ${e.message}</p>`);
      return;
    }
    const key = list.map((e) => `${e.id}:${e.title}`).join('|');
    if (!el.isConnected || key === shown) return;
    shown = key;
    el.setAttribute('aria-busy', 'false');
    // Transcripts someone has open stay open.
    const open = new Set([...el.querySelectorAll('details[open]')].map((d) => d.dataset.transcript));
    render(list, open);
    if (openId && !opened) {
      opened = true;
      const item = root.querySelector(`#ep-${CSS.escape(openId)}`);
      item?.scrollIntoView();
      item?.querySelector('details')?.setAttribute('open', '');
    }
  });

  function render(list, open) {
    if (!list.length) {
      setHtml(el, html`<p class="empty">No episodes yet. The first one airs the night after the first matches.</p>`);
      return;
    }
    setHtml(el, html`<ol class="episode-list">${list.map((e) => html`
      <li id="ep-${e.id}">
        <article class="episode">
          ${player(e)}
          <div>
            <h2>${e.title}</h2>
            <p class="muted small">${when(e.date)} · ${length(e.durationSeconds)}</p>
            ${e.summary ? html`<p>${e.summary}</p>` : ''}
            <details data-transcript="${e.id}"><summary>Transcript</summary><div class="transcript"><p class="muted small">Loading…</p></div></details>
          </div>
        </article>
      </li>`)}</ol>`);

    el.querySelectorAll('details[data-transcript]').forEach((d) => {
      d.addEventListener('toggle', () => loadTranscript(d));
      if (open.has(d.dataset.transcript)) d.open = true;
    });
  }

  async function loadTranscript(d) {
    if (!d.open || d.dataset.loaded) return;
    d.dataset.loaded = 'true';
    const box = d.querySelector('.transcript');
    try {
      const e = await api(`/api/v1/recaps/${d.dataset.transcript}`);
      setHtml(box, html`${(e.transcript || []).map((l) => html`
        <p><span class="speaker ${l.speaker}">${speakerName(l.speaker)}</span> ${l.text}</p>`)}`);
    } catch (err) {
      setHtml(box, html`<p class="error">Could not load the transcript: ${err.message}</p>`);
    }
  }

  return stop;
}

const SPEAKERS = { zip: 'Zip', sarge: 'Sarge', quill: 'Quill' };
const speakerName = (key) => SPEAKERS[key] ?? key;

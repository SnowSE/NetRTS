// Past matches: the whole archive, a page at a time, searchable by bot or owner name.
import { api, html, setHtml, fmt, matchRow } from './util.js';
import { GAME, say } from './lore.js';

const SIZES = [10, 25, 50, 100];
const CATEGORIES = [
  { key: '', label: 'All' },
  { key: 'Practice', label: say('Practice', 'Scrimmage') },
  { key: 'Preseason', label: 'Preseason' },
  { key: 'Season', label: 'Season' },
  { key: 'Championship', label: 'Championship' },
];

/** The page's state lives in the hash (#/history?q=…&cat=…&size=…&page=…), so back/forward and links work. */
function hrefFor({ q, cat, size, page }) {
  const p = new URLSearchParams();
  if (q) p.set('q', q);
  if (cat) p.set('cat', cat);
  if (size !== 25) p.set('size', size);
  if (page > 1) p.set('page', page);
  const s = p.toString();
  return `#/history${s ? '?' + s : ''}`;
}

/** "Today", "Yesterday", else e.g. "Tuesday, October 21". */
function dayLabel(date) {
  const day = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const diff = Math.round((day(new Date()) - day(date)) / 86_400_000);
  if (diff === 0) return 'Today';
  if (diff === 1) return 'Yesterday';
  return date.toLocaleDateString([], {
    weekday: 'long', month: 'long', day: 'numeric',
    year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric',
  });
}

export function mountHistory(root, params) {
  document.title = `${say('Past matches', 'Past brawls')} · ${GAME}`;
  const state = {
    q: (params.get('q') || '').trim(),
    cat: CATEGORIES.some((c) => c.key === params.get('cat')) ? params.get('cat') : '',
    size: SIZES.includes(Number(params.get('size'))) ? Number(params.get('size')) : 25,
    page: Math.max(1, Math.floor(Number(params.get('page')) || 1)),
  };
  let alive = true;

  setHtml(root, html`<div class="history-page">
    <p class="small"><a href="#/">← ${say('Matches', 'Brawls')}</a></p>
    <h1 class="headline">${say('Past matches', 'Past brawls')}</h1>
    <p class="lede">Every finished ${say('match', 'brawl')}, newest first. Pick one to watch its replay.</p>
    <form class="history-filters" id="filters" role="search">
      <div class="field grow"><label for="h-q">${say('Bot', 'Badger')} or player name</label>
        <input id="h-q" name="q" type="search" autocomplete="off" spellcheck="false" value="${state.q}"
          placeholder="e.g. rusher" aria-describedby="h-q-hint">
        <span class="muted small" id="h-q-hint">Separate names with commas to find ${say('matches', 'brawls')} with all of them.</span></div>
      <div class="field"><label for="h-cat">Category</label>
        <select id="h-cat" name="cat">${CATEGORIES.map((c) => html`<option value="${c.key}" ${c.key === state.cat ? html`selected` : ''}>${c.label}</option>`)}</select></div>
      <div class="field"><label for="h-size">Per page</label>
        <select id="h-size" name="size">${SIZES.map((n) => html`<option value="${n}" ${n === state.size ? html`selected` : ''}>${n}</option>`)}</select></div>
      <div class="field"><button class="btn" type="submit">Search</button></div>
    </form>
    <div id="h-results" aria-live="polite" aria-busy="true"><p class="empty">Loading…</p></div>
  </div>`);

  const form = root.querySelector('#filters');
  form.addEventListener('submit', (e) => {
    e.preventDefault();
    location.hash = hrefFor({ q: form.q.value.trim(), cat: form.cat.value, size: Number(form.size.value), page: 1 });
  });
  // Category and page size apply straight away; the name waits for Search (or Enter).
  form.addEventListener('change', (e) => {
    if (e.target.tagName === 'SELECT') form.requestSubmit();
  });

  const el = root.querySelector('#h-results');
  const query = new URLSearchParams({ skip: (state.page - 1) * state.size, limit: state.size });
  if (state.q) query.set('player', state.q);
  if (state.cat) query.set('category', state.cat);
  api(`/api/v1/matches/archive?${query}`).then((res) => {
    if (!alive) return;
    const pages = Math.max(1, Math.ceil(res.total / state.size));
    if (!res.matches.length) {
      setHtml(el, html`<p class="empty">${res.total
        ? html`There are only ${pages} page${pages === 1 ? '' : 's'}. <a href="${hrefFor({ ...state, page: pages })}">Go to the last one</a>.`
        : state.q || state.cat ? `No finished ${say('matches', 'brawls')} match that search.` : `No finished ${say('matches', 'brawls')} yet.`}</p>`);
      return;
    }

    // Group by the day each match started, in the viewer's time zone.
    const days = [];
    for (const m of res.matches) {
      const label = dayLabel(new Date(m.createdAt));
      if (days.at(-1)?.label !== label) days.push({ label, matches: [] });
      days.at(-1).matches.push(m);
    }
    const from = res.skip + 1, to = res.skip + res.matches.length;
    const pager = (where) => html`<nav class="pager" aria-label="Pages${where}">
      ${state.page > 1
        ? html`<a class="btn ghost small-btn" href="${hrefFor({ ...state, page: state.page - 1 })}" rel="prev">← Newer</a>`
        : html`<span class="btn ghost small-btn" aria-disabled="true">← Newer</span>`}
      <span class="muted small">${fmt(from)}–${fmt(to)} of ${fmt(res.total)} · page ${fmt(state.page)} of ${fmt(pages)}</span>
      ${state.page < pages
        ? html`<a class="btn ghost small-btn" href="${hrefFor({ ...state, page: state.page + 1 })}" rel="next">Older →</a>`
        : html`<span class="btn ghost small-btn" aria-disabled="true">Older →</span>`}
    </nav>`;
    setHtml(el, html`${pager('')}
      ${days.map((d) => html`<section class="history-day"><h2>${d.label}</h2>
        <ul class="match-list">${d.matches.map((m) => matchRow(m, { status: false, clock: true }))}</ul></section>`)}
      ${pager(' (bottom)')}`);
  }).catch((e) => {
    if (alive) setHtml(el, html`<p class="error">Could not load past ${say('matches', 'brawls')}: ${e.message}</p>`);
  }).finally(() => el.setAttribute('aria-busy', 'false'));

  return () => { alive = false; };
}

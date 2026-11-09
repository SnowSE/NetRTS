// A match as it plays, for anything that draws it (the spectator page, the Block Bot cockpit): the map,
// then every tick's spectator frame from the server-sent-events stream, until the match ends.
import { api } from './util.js';

const FIRST_RETRY_MS = 2000;
const MAX_RETRY_MS = 30000;

/**
 * Opens the feed for matchId. Callbacks (all optional):
 *   onMap(map)     the map, fetched first when this is given
 *   onState(s)     each spectator frame (fog=true: every player's vision is in it)
 *   onEnd()        the match is over: after its final frame, or the final snapshot when the stream
 *                  missed it (that snapshot comes through onState first, while the server still has it)
 *   onGone()       the match isn't on the server any more (a 404)
 *   onError(e)     the map couldn't load for another reason; the feed tries again, less and less often
 * live: false for a match already over: no stream, just the map and the final snapshot.
 *
 * The feed closes itself when the match ends or is gone; close() stops it early (in a page's teardown).
 * No callback runs after it has closed.
 */
export function openMatchFeed(matchId, { onMap, onState, onEnd, onGone, onError, live = true } = {}) {
  let closed = false;
  let es = null;
  let timer = 0;
  let wait = FIRST_RETRY_MS;
  let seen = false;   // a frame has come in

  const close = () => {
    closed = true;
    clearTimeout(timer);
    es?.close();
    es = null;
  };
  const retry = (fn) => {
    clearTimeout(timer);
    timer = setTimeout(() => { if (!closed) fn(); }, wait);
    wait = Math.min(wait * 2, MAX_RETRY_MS);
  };
  const gone = () => { close(); onGone?.(); };
  const frame = (s) => {
    seen = true;
    wait = FIRST_RETRY_MS;   // connected again: the next drop starts the backoff over
    onState?.(s);
  };

  async function loadMap() {
    let map;
    try {
      map = await api(`/api/v1/matches/${matchId}/map`);
    } catch (e) {
      if (closed) return;
      if (e.status === 404) { gone(); return; }
      onError?.(e);
      retry(loadMap);
      return;
    }
    if (closed) return;
    onMap(map);
    if (closed) return;
    if (live) stream(); else finish();
  }

  function stream() {
    es = new EventSource(`/api/v1/matches/${matchId}/spectate/stream?fog=true`);
    es.onmessage = (msg) => {
      if (closed) return;
      let s;
      try { s = JSON.parse(msg.data); } catch { return; }
      frame(s);
      if (s.status === 'Completed' && !closed) { close(); onEnd?.(); }
    };
    es.onerror = async () => {
      es?.close();
      es = null;
      if (closed) return;
      // Why did it drop: the match is over, it's gone, or the network blinked?
      try {
        const summary = await api(`/api/v1/matches/${matchId}`);
        if (closed) return;
        if (summary.status === 'Completed') { finish(); return; }
      } catch (e) {
        if (closed) return;
        if (e.status === 404) { gone(); return; }
      }
      retry(stream);
    };
  }

  /** The match is over: its final positions, unless a frame already showed them, and then onEnd. */
  async function finish() {
    es?.close();
    es = null;
    if (!seen) {
      try {
        const s = await api(`/api/v1/matches/${matchId}/spectate?fog=true`);
        if (closed) return;
        frame(s);
      } catch { /* archived: only the result is left */ }
    }
    if (closed) return;
    close();
    onEnd?.();
  }

  if (onMap) loadMap();
  else if (live) stream();
  else finish();

  return {
    close,
    get open() { return !closed; },
  };
}

// What this browser keeps: localStorage (until cleared) or sessionStorage (until the tab closes, so a
// shared lab computer forgets it). Either can be blocked (private windows, strict settings), and even
// reading window.sessionStorage can throw, so each area is looked up by name inside a try. Nothing here
// throws: a blocked store reads as the fallback and a save reports false.
//
//   area: 'localStorage' or 'sessionStorage'

/** A JSON value, or fallback when there is none (or it can't be read). */
export function load(area, key, fallback = null) {
  try {
    const v = window[area].getItem(key);
    return v == null ? fallback : JSON.parse(v);
  } catch { return fallback; }
}

/** Keeps a value as JSON; false when it couldn't be kept. */
export function save(area, key, value) {
  try { window[area].setItem(key, JSON.stringify(value)); return true; } catch { return false; }
}

export function forget(area, key) {
  try { window[area].removeItem(key); } catch { /* nothing kept */ }
}

/** A plain string (for values other code, like index.html's first-paint script, reads as they are). */
export function loadText(area, key, fallback = null) {
  try { return window[area].getItem(key) ?? fallback; } catch { return fallback; }
}

export function saveText(area, key, value) {
  try { window[area].setItem(key, String(value)); return true; } catch { return false; }
}

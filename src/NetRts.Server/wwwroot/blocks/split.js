// The bar between the cockpit's rules panel and its match view: drag it (or focus it and press ← →,
// Home, End) to make the panel wider or narrower, double-click it for the usual size, and use its
// button (or Enter) to fold the panel away and back. The width and the fold are remembered in this browser.
import { prefs, setPref } from './store.js';

const STEP = 32;

/**
 * @param grid  the cockpit grid; it gets --bb-left-w (the panel's width) and the class folded
 * @param bar   the role="separator" element to drag
 * @param fold  the button that folds the panel
 * @param min   the narrowest the panel goes; keep is the least the rest keeps
 * Returns a function that stops it.
 */
export function sidePanel(grid, bar, fold, { min = 320, keep = 360, pref = 'cockpitPanel' } = {}) {
  let { width = null, folded = false } = prefs()[pref] ?? {};
  const max = () => Math.max(min, grid.clientWidth - keep);
  const clamp = (w) => Math.round(Math.min(max(), Math.max(min, w)));
  const panelWidth = () => grid.querySelector('.bb-left')?.getBoundingClientRect().width ?? 0;
  const remember = () => setPref(pref, { width, folded });

  const apply = () => {
    grid.classList.toggle('folded', folded);
    if (width == null) grid.style.removeProperty('--bb-left-w');
    else grid.style.setProperty('--bb-left-w', `${clamp(width)}px`);
    bar.setAttribute('aria-valuemin', String(min));
    bar.setAttribute('aria-valuemax', String(Math.round(max())));
    bar.setAttribute('aria-valuenow', String(Math.round(folded ? 0 : panelWidth())));
    fold.setAttribute('aria-expanded', String(!folded));
    const say = folded ? 'Show your rules' : 'Hide your rules';
    fold.setAttribute('aria-label', say);
    fold.title = say;
  };
  const set = (w, keepIt = true) => {
    width = w == null ? null : clamp(w);
    folded = false;
    apply();
    if (keepIt) remember();
  };
  const toggle = () => { folded = !folded; apply(); remember(); };

  const stop = new AbortController();
  const on = (target, type, fn) => target.addEventListener(type, fn, { signal: stop.signal });

  let dragging = null;   // { id, frame, x }
  on(bar, 'pointerdown', (e) => {
    if (e.button !== 0) return;
    e.preventDefault();
    try { bar.setPointerCapture(e.pointerId); } catch { /* the pointer may already be gone */ }
    dragging = { id: e.pointerId, frame: 0, x: e.clientX };
    grid.classList.add('resizing');
  });
  on(bar, 'pointermove', (e) => {
    if (!dragging || e.pointerId !== dragging.id) return;
    dragging.x = e.clientX;
    // One resize a frame: the 3D view redraws at its new size each time.
    dragging.frame ||= requestAnimationFrame(() => {
      if (!dragging) return;
      dragging.frame = 0;
      set(dragging.x - grid.getBoundingClientRect().left - bar.offsetWidth / 2, false);
    });
  });
  const end = () => {
    if (!dragging) return;
    cancelAnimationFrame(dragging.frame);
    dragging = null;
    grid.classList.remove('resizing');
    remember();
  };
  on(bar, 'pointerup', end);
  on(bar, 'pointercancel', end);
  on(bar, 'lostpointercapture', end);
  on(bar, 'dblclick', () => set(null));
  on(bar, 'keydown', (e) => {
    const now = folded ? min : panelWidth();
    const by = e.shiftKey ? STEP * 4 : STEP;
    const to = { ArrowLeft: now - by, ArrowRight: now + by, Home: min, End: max() }[e.key];
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); return; }
    if (to == null) return;
    e.preventDefault();
    set(to);
  });
  on(fold, 'click', toggle);
  on(window, 'resize', apply);
  apply();
  return () => { end(); stop.abort(); };
}

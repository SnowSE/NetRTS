// Drag and drop with pointer events, so it works with a mouse, a pen or a finger (the browser's own
// drag and drop doesn't on touch screens). A copy of the block follows the pointer; the page says what
// each spot would do as it passes over, and what a drop there does.

const CONTROLS = 'select, input, textarea, label, a, button';

/**
 * Makes [data-drag] elements inside root draggable.
 * @param pick(el)          what dragging el would carry, or null if it can't be dragged now
 * @param over(payload, x, y)  where a drop at x, y would go: { el, cls } to mark el with cls, or null
 * @param drop(payload, target)  makes the drop (target is what over returned)
 * @param onStart(payload)  the drag began (past a few pixels of movement)
 * @param onEnd()           it ended, dropped or not
 * Returns a function that stops it.
 */
export function pointerDrag(root, { pick, over, drop, onStart, onEnd, threshold = 5 }) {
  let pending = null;    // { el, payload, x, y, id }
  let drag = null;       // { el, payload, ghost, dx, dy, target }
  let suppressClick = false;

  const mark = (target) => {
    if (drag.target?.el === target?.el && drag.target?.cls === target?.cls) return;
    drag.target?.el.classList.remove(drag.target.cls);
    target?.el.classList.add(target.cls);
    drag.target = target;
  };

  const down = (e) => {
    if (e.button !== 0 || drag) return;
    const el = e.target.closest('[data-drag]');
    if (!el || !root.contains(el)) return;
    // The dropdowns, numbers and buttons on a block keep working; anywhere else on it picks it up.
    const control = e.target.closest(CONTROLS);
    if (control && control !== el && el.contains(control)) return;
    const payload = pick(el);
    if (!payload) return;
    pending = { el, payload, x: e.clientX, y: e.clientY, id: e.pointerId };
  };

  const begin = (e) => {
    const { el, payload } = pending;
    const r = el.getBoundingClientRect();
    const ghost = el.cloneNode(true);
    ghost.classList.add('bb-ghost');
    ghost.removeAttribute('id');
    ghost.querySelectorAll('[id]').forEach((x) => x.removeAttribute('id'));
    ghost.setAttribute('aria-hidden', 'true');
    Object.assign(ghost.style, { width: `${r.width}px`, left: `${r.left}px`, top: `${r.top}px` });
    document.body.append(ghost);
    drag = { el, payload, ghost, dx: pending.x - r.left, dy: pending.y - r.top, target: null };
    pending = null;
    el.classList.add('bb-drag-src');
    try { root.setPointerCapture(e.pointerId); } catch { /* the pointer may already be gone */ }
    onStart?.(payload);
  };

  const move = (e) => {
    if (pending && e.pointerId === pending.id && Math.hypot(e.clientX - pending.x, e.clientY - pending.y) > threshold) begin(e);
    if (!drag) return;
    e.preventDefault();
    drag.ghost.style.left = `${e.clientX - drag.dx}px`;
    drag.ghost.style.top = `${e.clientY - drag.dy}px`;
    mark(over(drag.payload, e.clientX, e.clientY));
    autoScroll(root, e.clientY);
  };

  const finish = (dropIt) => {
    if (!drag) { pending = null; return; }
    const { el, payload, ghost, target } = drag;
    mark(null);
    ghost.remove();
    el.classList.remove('bb-drag-src');
    drag = null;
    suppressClick = true;   // the click that follows a drag isn't a click on what's under it
    setTimeout(() => { suppressClick = false; }, 0);
    onEnd?.();
    if (dropIt && target) drop(payload, target);
  };

  const up = () => finish(true);
  const cancel = () => finish(false);
  const key = (e) => { if (e.key === 'Escape' && drag) { e.preventDefault(); cancel(); } };
  const click = (e) => { if (suppressClick) { e.stopPropagation(); e.preventDefault(); } };

  root.addEventListener('pointerdown', down);
  root.addEventListener('pointermove', move);
  root.addEventListener('pointerup', up);
  root.addEventListener('pointercancel', cancel);
  root.addEventListener('lostpointercapture', cancel);
  root.addEventListener('click', click, true);
  document.addEventListener('keydown', key);
  return () => {
    cancel();
    root.removeEventListener('pointerdown', down);
    root.removeEventListener('pointermove', move);
    root.removeEventListener('pointerup', up);
    root.removeEventListener('pointercancel', cancel);
    root.removeEventListener('lostpointercapture', cancel);
    root.removeEventListener('click', click, true);
    document.removeEventListener('keydown', key);
  };
}

/** The element under x, y that's a drop spot (the dragged copy lets the pointer through). */
export const dropSpot = (x, y, selector) => document.elementFromPoint(x, y)?.closest(selector) ?? null;

/** Where among items (in a column) a drop at y goes: the index to insert at. */
export function slotAmong(items, y) {
  for (let i = 0; i < items.length; i++) {
    const r = items[i].getBoundingClientRect();
    if (y < r.top + r.height / 2) return i;
  }
  return items.length;
}

/** Near the top or bottom of the scrolling column (or the window), scroll it a little. */
function autoScroll(root, y) {
  const edge = 48;
  const step = 14;
  let box = root;
  while (box && box !== document.body && !(box.scrollHeight > box.clientHeight && /(auto|scroll)/.test(getComputedStyle(box).overflowY))) box = box.parentElement;
  if (box && box !== document.body) {
    const r = box.getBoundingClientRect();
    if (y < r.top + edge) box.scrollTop -= step;
    else if (y > r.bottom - edge) box.scrollTop += step;
    return;
  }
  if (y < edge) window.scrollBy(0, -step);
  else if (y > window.innerHeight - edge) window.scrollBy(0, step);
}

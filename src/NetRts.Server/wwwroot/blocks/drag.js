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
 * @param covered()         where the page shows from, when something pinned covers its top (else 0)
 * @param layer()           the element the copy goes in: one inside the styles that colour the block
 *                          (in document.body, a block whose colours come from its editor shows blank)
 * Returns a function that stops it.
 */
export function pointerDrag(root, { pick, over, drop, onStart, onEnd, covered = () => 0, layer = () => document.body, threshold = 5 }) {
  let pending = null;    // { el, payload, x, y, id }
  let drag = null;       // { el, payload, ghost, dx, dy, target, x, y, frame }
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
    (layer() ?? document.body).append(ghost);
    drag = { el, payload, ghost, dx: pending.x - r.left, dy: pending.y - r.top, target: null, x: pending.x, y: pending.y, frame: 0 };
    pending = null;
    el.classList.add('bb-drag-src');
    try { root.setPointerCapture(e.pointerId); } catch { /* the pointer may already be gone */ }
    onStart?.(payload);
  };

  const move = (e) => {
    if (pending && e.pointerId === pending.id && Math.hypot(e.clientX - pending.x, e.clientY - pending.y) > threshold) begin(e);
    if (!drag) return;
    e.preventDefault();
    drag.x = e.clientX;
    drag.y = e.clientY;
    drag.ghost.style.left = `${drag.x - drag.dx}px`;
    drag.ghost.style.top = `${drag.y - drag.dy}px`;
    mark(over(drag.payload, drag.x, drag.y));
    if (!drag.frame) scrollLoop();
  };

  // Held near an edge, the rules keep scrolling (not only while the pointer moves), and what's under
  // the pointer is looked at again as they go by.
  const scrollLoop = () => {
    drag.frame = 0;
    if (!autoScroll(root, drag.y, covered())) return;
    mark(over(drag.payload, drag.x, drag.y));
    drag.frame = requestAnimationFrame(() => { if (drag) scrollLoop(); });
  };

  const finish = (dropIt) => {
    if (!drag) { pending = null; return; }
    const { el, payload, ghost, target, frame } = drag;
    cancelAnimationFrame(frame);
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

/**
 * Near the top or bottom of the scrolling column (or the window), scroll it a little: faster the closer
 * to the edge. Under something pinned over the top (from y = cover down), the band just below it scrolls
 * up instead of the top edge. Says whether it scrolled.
 */
function autoScroll(root, y, cover = 0) {
  const edge = 48;
  const speed = (into) => Math.ceil(16 * Math.min(1, Math.max(0, into) / edge));
  let box = root;
  while (box && box !== document.body && !(box.scrollHeight > box.clientHeight && /(auto|scroll)/.test(getComputedStyle(box).overflowY))) box = box.parentElement;
  const inBox = box && box !== document.body;
  const r = inBox ? box.getBoundingClientRect() : { top: 0, bottom: window.innerHeight };
  const top = Math.max(r.top, cover);
  let by = 0;
  if (y < top + edge && (!cover || y >= top)) by = -speed(top + edge - y);
  else if (y > r.bottom - edge) by = speed(y - (r.bottom - edge));
  if (!by) return false;
  const before = inBox ? box.scrollTop : window.scrollY;
  if (inBox) box.scrollTop += by; else window.scrollBy(0, by);
  return (inBox ? box.scrollTop : window.scrollY) !== before;
}

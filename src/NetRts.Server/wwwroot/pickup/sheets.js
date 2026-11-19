// The phone's bottom sheets (a list of choices, or a number with − / + and quick picks) and its toast.
// One sheet at a time; the page's click handler runs the sheet's actions (sheets.actions).
import { esc } from '../util.js';
import { icon } from './cards.js';

/** Up to eight handy numbers between min and max for the number sheet. */
function quick(min, max) {
  let c = [...new Set([min, ...[2, 3, 5, 8, 10, 12, 15, 20, 25, 30].filter((x) => x > min && x < max), max])];
  if (c.length > 8) { const step = (c.length - 1) / 7; c = Array.from({ length: 8 }, (_, i) => c[Math.round(i * step)]); }
  return c;
}

/** wrap: the sheet's backdrop element; toastEl: the toast; scope: the page's mountScope. */
export function createSheets(wrap, toastEl, scope) {
  let SH = null;
  let toastT = 0;

  function render() {
    if (!SH) { wrap.hidden = true; wrap.innerHTML = ''; return; }
    wrap.hidden = false;
    const body = SH.kind === 'list'
      ? SH.opts.map((o, i) => `<button type="button" class="opt ${o.danger ? 'danger' : ''}" data-a="sheet-pick" data-v="${i}" ${o.disabled ? 'disabled' : ''}>${o.icon ? `<span class="o-ic">${icon(o.icon)}</span>` : ''}<span class="o-t"><b>${esc(o.label)}</b>${o.sub ? `<small>${esc(o.sub)}</small>` : ''}</span>${o.cost != null ? `<span class="o-c">${esc(o.cost)}</span>` : ''}${o.on ? '<span class="o-on">✓</span>' : ''}</button>`).join('')
      : `<div class="num-row"><button type="button" class="nb" data-a="num-step" data-v="-1" aria-label="Less">−</button><output class="nv">${SH.value}</output><button type="button" class="nb" data-a="num-step" data-v="1" aria-label="More">+</button></div>
         <div class="quick">${quick(SH.min, SH.max).map((q) => `<button type="button" data-a="num-set" data-v="${q}" class="${q === SH.value ? 'on' : ''}">${q}</button>`).join('')}</div>
         ${SH.check ? `<label class="chk" for="pk-keepchk"><input type="checkbox" id="pk-keepchk" ${SH.check.on ? 'checked' : ''}><span><b>${esc(SH.check.label)}</b><small>${esc(SH.check.sub)}</small></span></label>` : ''}
         <button type="button" class="num-done" data-a="num-done">Done</button>`;
    wrap.innerHTML = `<div class="sheet" role="dialog" aria-modal="true" aria-label="${esc(SH.title)}"><div class="grab"></div><div class="sh-h"><b>${esc(SH.title)}</b><button type="button" class="x" data-a="sheet-close" aria-label="Close">✕</button></div>${body}</div>`;
    wrap.querySelector('.sheet button:not([disabled])')?.focus({ preventScroll: true });
  }

  const close = () => { SH = null; render(); };
  scope.on(wrap, 'click', (e) => { if (e.target === wrap) close(); });
  scope.on(wrap, 'change', (e) => { if (e.target.id === 'pk-keepchk' && SH?.check) SH.check.on = e.target.checked; });
  scope.on(document, 'keydown', (e) => { if (e.key === 'Escape' && SH) close(); });

  return {
    get open() { return !!SH; },
    /** opts: [{ label, sub, cost, icon, disabled, danger, on, fn }] */
    list(title, opts) { SH = { kind: 'list', title, opts }; render(); },
    /** check: { on, label, sub } for a checkbox under the number; onDone(value, checked). */
    num(title, value, min, max, onDone, check) {
      SH = { kind: 'num', title, value: Math.min(max, Math.max(min, value)), min, max, onDone, check };
      render();
    },
    close,
    toast(msg) {
      toastEl.textContent = msg;
      toastEl.hidden = false;
      scope.clear(toastT);
      toastT = scope.later(() => { toastEl.hidden = true; }, 1800);
    },
    actions: {
      'sheet-pick': (v) => { const o = SH.opts[+v]; close(); o.fn(); },
      'sheet-close': close,
      'num-step': (v) => { SH.value = Math.min(SH.max, Math.max(SH.min, SH.value + +v)); render(); },
      'num-set': (v) => { SH.value = +v; render(); },
      'num-done': () => { const f = SH.onDone, v = SH.value, keep = SH.check?.on; close(); f(v, keep); },
    },
  };
}

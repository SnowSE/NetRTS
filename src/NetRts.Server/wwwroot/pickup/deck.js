// The Deck: the plan as cards, under four tabs. Start (buy from the 1,500-grub pile), Then (up to six
// steps, run once in order, dragged into order by the grip), Forever (one to three cards done over and
// over) and Attack (when, at what, and whether to defend). The plan is the page's object (API shape);
// every change goes through ctx.changed(), which saves it and redraws.
import { esc } from '../util.js';
import { card, icon, artHTML } from './cards.js';
import {
  item, tcls, nameOf, plural, clock, START_ITEMS, ALL_ITEMS, MAX_STEPS, MAX_FOREVER, FREE_BUILDING,
  startN, left, owned, missing, workersBefore, haveBefore, issue, stepText, stepSub, stepItem, foreverText, startBlock, normalize, samePlan,
} from './items.js';

const MAX_COUNT = 60;
const MAX_WORKERS = 30;
const ARMY_SIZES = { min: 1, max: 30, start: 12 };
const ATTACK_TIMES = [30, 60, 90, 120];
const KEEP_CHECK = (on) => ({ on, label: 'Keep it at this level', sub: 'Replace any that die, before working on later steps' });

/**
 * ctx: { plan() (the plan object, changed in place), replace(plan), changed(), decks() ([{id, name, plan}]),
 *        sheets (sheets.js) }
 */
export function createDeck(ctx) {
  const ui = { tab: 'start' };
  const P = () => ctx.plan();
  const { sheets } = ctx;
  const toast = (m) => sheets.toast(m);
  const changed = () => ctx.changed();

  // ---- start pile

  function addStart(id) {
    const plan = P(), why = startBlock(plan, id);
    if (why) { toast(why === 'Three at most' ? 'Three of each building at most' : 'Not enough grubs left'); return; }
    const x = plan.start.find((s) => s.item === id);
    if (x) x.count++; else plan.start.push({ item: id, count: 1 });
    changed();
  }
  function decStart(id) {
    const plan = P(), k = plan.start.findIndex((x) => x.item === id);
    if (k < 0) return;
    if (--plan.start[k].count <= 0) plan.start.splice(k, 1);
    changed();
  }

  // ---- steps

  /** "Soldiers up to N" (or workers), with the keep-at-this-level box. after: runs once it's set. */
  function countSheet(id, idx, cur, after) {
    const plan = P(), workers = id === 'Worker';
    const h = workers ? workersBefore(plan, idx) : haveBefore(plan, id, idx);
    const max = workers ? Math.max(h + 1, MAX_WORKERS) : Math.min(MAX_COUNT, h + 20);
    sheets.num(`${plural(id, 2)} up to… (you'll have ${h})`, cur?.count ?? h + 5, Math.min(h + 1, max), max, (n, keep) => {
      plan.steps[idx] = workers ? { kind: 'workers', item: null, count: n, keep: !!keep } : { kind: 'train', item: id, count: n, keep: !!keep };
      changed();
      after?.();
    }, KEEP_CHECK(cur?.keep ?? false));
  }
  function pickStepItem(kind, idx, cur) {
    if (kind === 'workers') { countSheet('Worker', idx, cur); return; }
    const o = owned(P(), idx);
    const want = { train: 'unit', build: 'building', research: 'research' }[kind];
    const ids = ALL_ITEMS.filter((id) => item(id)?.type === want && id !== 'Worker');
    sheets.list({ train: 'Train army', build: 'Build', research: 'Take a class' }[kind], ids.map((id) => {
      const it = item(id), m = missing(id, o), taken = kind === 'research' && o.has(id);
      return { label: it.name, icon: id, cost: kind === 'train' ? `${it.cost} each` : it.cost,
        sub: taken ? 'Already taken' : m.length ? `Needs ${m.join(' and ')} first` : it.blurb, disabled: taken || m.length > 0,
        fn: () => {
          if (kind === 'train') countSheet(id, idx, cur?.item === id ? cur : null);
          else { P().steps[idx] = { kind, item: id, count: 1, keep: false }; changed(); }
        } };
    }));
  }
  function swap(a, b) { const s = P().steps; [s[a], s[b]] = [s[b], s[a]]; changed(); }
  function editStep(i) {
    const plan = P(), s = plan.steps[i], acts = [];
    if (s.kind === 'train' || s.kind === 'workers') acts.push({ label: 'Change how many', sub: s.keep ? 'Kept at this level' : null, fn: () => countSheet(stepItem(s), i, s) });
    acts.push({ label: 'Change this step', fn: () => pickStepItem(s.kind, i, s) });
    if (i > 0) acts.push({ label: 'Move up', fn: () => swap(i, i - 1) });
    if (i < plan.steps.length - 1) acts.push({ label: 'Move down', fn: () => swap(i, i + 1) });
    acts.push({ label: 'Delete step', danger: true, fn: () => { plan.steps.splice(i, 1); changed(); } });
    sheets.list(`Step ${i + 1}: ${stepText(s)}`, acts);
  }
  function stepBy(i, d) {
    const plan = P(), s = plan.steps[i];
    if (s.kind === 'train') s.count = Math.max(haveBefore(plan, s.item, i) + 1, Math.min(MAX_COUNT, s.count + d));
    else if (s.kind === 'workers') s.count = Math.max(workersBefore(plan, i) + 1, Math.min(MAX_WORKERS, s.count + d));
    changed();
  }

  // ---- playing a card from the hand

  function cardPlay(id) {
    const plan = P();
    if (ui.tab === 'start') { addStart(id); return; }
    if (ui.tab === 'forever') {
      if (plan.forever.length >= MAX_FOREVER) { toast('Three cards at most. Tap one to take it back.'); return; }
      plan.forever.push(id);
      changed();
      return;
    }
    if (plan.steps.length >= MAX_STEPS) { toast('Six steps at most'); return; }
    const it = item(id), idx = plan.steps.length;
    if (it.type === 'unit') { countSheet(id, idx, null, () => toast(`Step ${idx + 1}: ${stepText(P().steps[idx])}`)); return; }
    const s = { kind: it.type === 'building' ? 'build' : 'research', item: id, count: 1, keep: false };
    plan.steps.push(s);
    changed();
    toast(`Step ${plan.steps.length}: ${stepText(s)}`);
  }

  // ---- drawing

  function startZone(plan) {
    const live = ctx.live?.();
    const zone = `${live ? '<p class="live-note">Your match started with this pile. Changes here are for your next match.</p>' : ''}<div class="minis"><span class="mini t-building fixed">${icon(FREE_BUILDING, 16)} ${esc(nameOf(FREE_BUILDING))} <span class="ct">free</span></span>${plan.start.map((x) => `<button type="button" class="mini t-${tcls(x.item)}" data-a="start-dec" data-v="${x.item}" aria-label="Take back one ${esc(nameOf(x.item))}">${icon(x.item, 16)} ${esc(nameOf(x.item))} <span class="ct">×${x.count}</span></button>`).join('')}</div>
      <p class="hint">${plan.start.length ? 'Tap a card here to take one back.' : 'Play cards from your hand. Each one costs grubs.'}</p>`;
    const hand = `<p class="hand-h">Your hand · tap to buy</p><div class="hand">${START_ITEMS.map((id) => { const why = startBlock(plan, id); return card(id, !!why, why); }).join('')}</div>`;
    return [zone, hand];
  }

  function thenZone(plan) {
    const live = ctx.live?.();
    const o = owned(plan, plan.steps.length), full = plan.steps.length >= MAX_STEPS;
    const zone = `<ol class="slots" data-drag="steps">${Array.from({ length: MAX_STEPS }, (_, i) => {
      const s = plan.steps[i];
      if (!s) return `<li class="slot empty"><span class="no">${i + 1}</span><span>${i === plan.steps.length ? 'Play a card from your hand' : ''}</span></li>`;
      const e = issue(plan, i), counts = s.kind === 'train' || s.kind === 'workers';
      const state = live ? (i < live.step ? 'done' : i === live.step ? 'now' : '') : '';
      return `<li class="slot t-${tcls(stepItem(s))} ${state}" data-di="${i}"><span class="grip" data-grip title="Drag to reorder" aria-hidden="true">⠿</span><span class="no">${state === 'done' ? '✓' : i + 1}</span><button type="button" class="sl-main" data-a="step-edit" data-v="${i}"><span class="sl-ic">${icon(stepItem(s), 26)}</span><span><b>${esc(stepText(s))}</b><small class="${e ? 'bad' : ''}">${state === 'done' ? 'Done' : state === 'now' ? 'Your bot is on this step now' : esc(e || stepSub(s))}</small></span></button>${counts ? `<span class="sl-n"><button type="button" data-a="step-dec" data-v="${i}" aria-label="Fewer">−</button><button type="button" data-a="step-inc" data-v="${i}" aria-label="More">+</button></span>` : ''}</li>`;
    }).join('')}</ol>
    <p class="hint">${live ? 'Steps already done stay done. Changes reach your bot right away.' : 'Each step runs once, top to bottom. Drag ⠿ to change the order. Then the Forever cards take over.'}</p>`;
    const hand = `<p class="hand-h">${full ? 'Six steps is the most' : 'Your hand · tap to add a step'}</p><div class="hand">${ALL_ITEMS.map((id) => {
      const it = item(id), m = missing(id, o), taken = it.type === 'research' && o.has(id);
      return card(id, full || m.length > 0 || taken, taken ? 'Already taken' : m.length ? `Needs ${m.join(' + ')}` : '');
    }).join('')}</div>`;
    return [zone, hand];
  }

  function foreverZone(plan) {
    const o = owned(plan, plan.steps.length);
    plan.forever.forEach((id) => { if (item(id).type !== 'unit') o.add(id); });
    const zone = `<div class="mixrow" data-drag="mix" data-axis="x">${[0, 1, 2].map((i) => {
      const id = plan.forever[i];
      if (!id) return `<div class="mixslot empty">${i === plan.forever.length ? 'Play a card' : ''}</div>`;
      return `<button type="button" class="mixslot card t-${tcls(id)}" data-a="mix-drop" data-v="${i}" data-di="${i}" data-grip aria-label="Take back ${esc(nameOf(id))}"><span class="art">${artHTML(id)}</span><span class="cost">${item(id).cost}</span><span class="nm">${esc(nameOf(id))}</span></button>`;
    }).join('')}</div>
    <p class="hint">After the last step your bot does these in order, over and over, until the match ends: ${esc(foreverText(plan))}, ${esc(foreverText(plan))}, … Each card is one unit, one building or one class. A class it already has gets skipped. Drag to reorder; tap a card to take it back.</p>`;
    const full = plan.forever.length >= MAX_FOREVER;
    const hand = `<p class="hand-h">${full ? 'Three cards is the most' : 'Your hand · tap to add to the loop'}</p><div class="hand">${ALL_ITEMS.map((id) => {
      const m = missing(id, o);
      return card(id, full || m.length > 0, m.length ? `Needs ${m.join(' + ')}` : '');
    }).join('')}</div>`;
    return [zone, hand];
  }

  function attackZone(plan) {
    const a = plan.attack, army = a.trigger === 'army';
    return [`<div class="at">
      <h3>Attack when</h3>
      <div class="tiles">
        <div class="tile wide ${army ? 'on' : ''}"><button type="button" class="tl-main" data-a="atk-mode">Army reaches <b>${army ? a.army : ARMY_SIZES.start}</b></button>${army ? `<span class="sl-n"><button type="button" data-a="atk-by" data-v="-1" aria-label="Smaller army">−</button><button type="button" data-a="atk-by" data-v="1" aria-label="Bigger army">+</button></span>` : ''}</div>
        ${ATTACK_TIMES.map((t) => `<button type="button" class="tile ${!army && a.atSecond === t ? 'on' : ''}" data-a="atk-time" data-v="${t}">At ${clock(t)}</button>`).join('')}
      </div>
      <h3>Go for</h3>
      <div class="tiles"><button type="button" class="tile ${a.target === 'base' ? 'on' : ''}" data-a="tgt-set" data-v="base">Their base</button><button type="button" class="tile ${a.target === 'nearest' ? 'on' : ''}" data-a="tgt-set" data-v="nearest">Nearest enemies</button></div>
      <h3>If they attack my base</h3>
      <div class="tiles"><button type="button" class="tile ${a.defend ? 'on' : ''}" data-a="def-set" data-v="1">Defend it</button><button type="button" class="tile ${!a.defend ? 'on' : ''}" data-a="def-set" data-v="0">Keep attacking</button></div>
      <p class="hint">Nobody can fight during the 30-second treaty.</p>
    </div>`, ''];
  }

  const ZONES = { start: startZone, then: thenZone, forever: foreverZone, attack: attackZone };

  /** Everything under the top bar: the decks, the tabs, the zone and the hand. */
  function html() {
    const plan = P(), tab = ui.tab, decks = ctx.decks();
    const [zone, hand] = ZONES[tab](plan);
    const tabs = [['start', 'Start', left(plan)], ['then', 'Then', `${plan.steps.length}/${MAX_STEPS}`], ['forever', 'Forever', `${plan.forever.length}/${MAX_FOREVER}`], ['attack', 'Attack', '']];
    return `${decks.length ? `<div class="cd-pre"><span>Decks:</span>${decks.map((d) => `<button type="button" class="pill ${samePlan(d.plan, plan) ? 'on' : ''}" data-a="preset" data-v="${esc(d.id)}">${esc(d.name)}</button>`).join('')}</div>` : ''}
      <div class="cd-tabs" role="tablist">${tabs.map(([k, l, c]) => `<button type="button" role="tab" aria-selected="${tab === k}" class="${tab === k ? 'on' : ''}" data-a="tab" data-v="${k}">${l}${c !== '' ? ` <small>${c}</small>` : ''}</button>`).join('')}</div>
      <div class="zone">${zone}</div>
      ${hand}`;
  }

  /** A drag in the Then list or the Forever row ended: move the item from one place to another. */
  function move(kind, from, to) {
    const arr = kind === 'steps' ? P().steps : P().forever;
    const [x] = arr.splice(from, 1);
    arr.splice(to, 0, x);
    changed();
  }

  const actions = {
    preset: (v) => { const d = ctx.decks().find((x) => x.id === v); if (d) ctx.replace(normalize(d.plan)); },
    tab: (v) => { ui.tab = v; ctx.redraw(); },
    card: (v) => cardPlay(v),
    'start-dec': (v) => decStart(v),
    'step-edit': (v) => editStep(+v),
    'step-inc': (v) => stepBy(+v, 1),
    'step-dec': (v) => stepBy(+v, -1),
    'mix-drop': (v) => {
      const f = P().forever;
      if (f.length <= 1) { toast('Keep at least one card here'); return; }
      f.splice(+v, 1);
      changed();
    },
    'atk-mode': () => { const a = P().attack; if (a.trigger !== 'army') { a.trigger = 'army'; changed(); } },
    'atk-by': (v) => { const a = P().attack; a.army = Math.min(ARMY_SIZES.max, Math.max(ARMY_SIZES.min, a.army + +v)); changed(); },
    'atk-time': (v) => { const a = P().attack; a.trigger = 'time'; a.atSecond = +v; changed(); },
    'tgt-set': (v) => { P().attack.target = v; changed(); },
    'def-set': (v) => { P().attack.defend = v === '1'; changed(); },
  };

  return { html, actions, move, get tab() { return ui.tab; } };
}

/**
 * Drag to reorder inside root: a press on [data-grip] inside an item ([data-di]) of a list with
 * data-drag (data-axis="x" for a row) moves it; onMove(kind, from, to) when it's dropped somewhere new.
 * Returns eat(): true once just after a drag, so the click that ends it doesn't also tap.
 */
export function dragToReorder(root, scope, onMove) {
  let D = null, eatClick = false;
  scope.on(root, 'pointerdown', (e) => {
    const g = e.target.closest('[data-grip]');
    if (!g) return;
    const it = g.closest('[data-di]'), list = it?.parentElement;
    if (!list?.dataset.drag) return;
    const items = [...list.children].filter((el) => el.dataset.di != null);
    D = { kind: list.dataset.drag, from: +it.dataset.di, to: +it.dataset.di, item: it, items, horiz: list.dataset.axis === 'x',
      rects: items.map((el) => el.getBoundingClientRect()), x: e.clientX, y: e.clientY, moved: false, id: e.pointerId };
    if (g !== it) e.preventDefault();
  });
  scope.on(document, 'pointermove', (e) => {
    if (!D || e.pointerId !== D.id) return;
    const dx = e.clientX - D.x, dy = e.clientY - D.y;
    if (!D.moved) {
      if (Math.hypot(dx, dy) < 6) return;
      D.moved = true;
      D.item.classList.add('dragging');
      try { D.item.setPointerCapture(D.id); } catch { /* the pointer may be gone */ }
    }
    e.preventDefault();
    const r = D.rects, f = D.from, mid = (q) => (D.horiz ? q.left + q.width / 2 : q.top + q.height / 2);
    const c = mid(r[f]) + (D.horiz ? dx : dy);
    let to = f;
    r.forEach((q, i) => { if (i < f && c < mid(q)) to = Math.min(to, i); if (i > f && c > mid(q)) to = Math.max(to, i); });
    D.to = to;
    const size = r.length > 1 ? (D.horiz ? r[1].left - r[0].left : r[1].top - r[0].top) : 0;
    D.items.forEach((el, i) => {
      const s = i === f ? (D.horiz ? dx : dy) : f < to && i > f && i <= to ? -size : to < f && i >= to && i < f ? size : 0;
      el.style.transform = s ? (D.horiz ? `translateX(${s}px)` : `translateY(${s}px)`) : '';
    });
  }, { passive: false });
  const end = (e) => {
    if (!D || e.pointerId !== D.id) return;
    const d = D;
    D = null;
    if (!d.moved) return;
    eatClick = true;
    scope.later(() => { eatClick = false; }, 80);
    d.items.forEach((el) => { el.style.transform = ''; });
    d.item.classList.remove('dragging');
    if (d.to !== d.from) onMove(d.kind, d.from, d.to);
  };
  scope.on(document, 'pointerup', end);
  scope.on(document, 'pointercancel', end);
  return () => { const was = eatClick; eatClick = false; return was; };
}

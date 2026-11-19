// Match over (phase "over"): where you placed on a podium over a scene from the game, what your bot did,
// the way back into line, and the way to Snow College and to Block Bot.
import { esc } from '../util.js';
import { ordinal } from './items.js';
import { takeHomeLink } from './convert.js';

// Through the server, which counts each click for the booth (netrts.pickup.link_clicks) and sends you on.
const GO = '/api/v1/pickup/go/';
const LINKS = { program: `${GO}program`, cost: `${GO}cost`, degree: `${GO}degree`, blocks: `${GO}blocks` };
const fmtN = (n) => (typeof n === 'number' ? n.toLocaleString('en-US') : '–');
const nameOf = (x) => (x.you ? 'You' : x.name);

function podiumHTML(players) {
  const podium = [players[1], players[0], players[2]].map((x) => {
    if (!x) return '<div class="pod none"></div>';
    const rank = players.indexOf(x) + 1;
    return `<div class="pod r${rank} ${x.you ? 'me' : ''}"><span class="pn">${rank === 1 ? '<span class="crown">♛</span> ' : ''}${esc(nameOf(x))}</span><span class="ps2">${fmtN(x.score)}</span><span class="blk">${rank}</span></div>`;
  }).join('');
  const rest = players.slice(3).map((x, i) => `<p class="rest">${ordinal(i + 4)} · ${esc(nameOf(x))} · ${fmtN(x.score)}</p>`).join('');
  return `<div class="podium">${podium}</div>${rest}`;
}

/** The match-over screen for me (phase "over", me.result set) and the plan that played. */
export function overHTML(me, plan) {
  const r = me.result ?? { players: [], place: 0, won: false };
  const players = r.players || [];
  const others = players.filter((p) => !p.you).map((p) => p.name);
  const winner = players[0];
  let head, sub;
  if (r.won) {
    head = me.champion && me.wins > 1 ? 'You held the hill!' : 'You took the hill!';
    sub = me.champion
      ? `${me.name} beat ${others.join(' and ') || 'everyone'}. You stay on as champion${me.wins > 1 ? ` (${me.wins} wins)` : ''}.`
      : `${me.name} beat ${others.join(' and ') || 'everyone'}. That makes you a legend of the hill.`;
  } else {
    head = r.place ? `${ordinal(r.place)} place` : 'Match over';
    sub = winner ? `${winner.name} won this one. Change one thing and try again.` : 'Change one thing and try again.';
  }
  const did = [
    [`${r.stepsDone ?? 0} of ${r.steps ?? plan.steps.length}`, 'steps done'],
    [r.fightersTrained, 'fighters trained'],
    [r.fightersLost, 'fighters lost'],
    [r.buildings, 'buildings'],
    ...(r.classes ? [[r.classes, 'classes taken']] : []),
  ];
  return `<section class="over-hero">
      <p class="eyebrow-o">Match over</p><h2>${esc(head)}</h2><p class="sub">${esc(sub)}</p>
      <div class="scene-gap" aria-hidden="true"></div>
      ${podiumHTML(players)}
    </section>
    <section class="over-body">
      <h3>What your bot did</h3>
      <ul class="did">${did.map(([v, l]) => `<li><b>${typeof v === 'number' ? fmtN(v) : esc(v ?? '–')}</b>${l}</li>`).join('')}</ul>
      <button type="button" class="big-btn" data-a="rejoin">${r.won && me.champion ? 'Tweak your plan (you defend the hill automatically)' : 'Tweak your plan and get back in line'}</button>
      <p class="pk-error" id="pk-over-err" role="alert" hidden></p>
      <div class="snow">
        <p class="snow-k">You just programmed a bot</p>
        <p>A plan the computer follows step by step is a program. Snow College students build all sorts of cool software (like this game) in the Software Engineering program.</p>
        <a class="snow-a pri" href="${LINKS.program}" target="_blank" rel="noopener">Software Engineering at Snow College</a>
        <div class="snow-pair">
          <a class="snow-a" href="${LINKS.cost}" target="_blank" rel="noopener">What would my degree cost?</a>
          <a class="snow-a" href="${LINKS.degree}" target="_blank" rel="noopener">Build your degree schedule</a>
        </div>
        <a class="snow-a" href="${esc(takeHomeLink(plan, me.name))}" target="_blank" rel="noopener">Take your bot home</a>
        <p class="snow-note">Opens Block Bot with your plan turned into rules you can keep changing. Or <a href="${LINKS.blocks}" target="_blank" rel="noopener">start fresh in Block Bot</a>.</p>
      </div>
    </section>`;
}

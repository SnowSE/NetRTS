// #/spring-rules: what changes when the spring rules replace classic. Every number comes from the two rule
// sets (GET /api/v1/rulesets/{name}), so the page can't drift from the game.
import { api, html, setHtml, fmt, mountScope } from './util.js';
import { GAME, ORE, say, buildingName, plainBuildingName, unitName, upgradeName } from './lore.js';
import { ruleSets, rolloutLine, springIsDefault, SPRING } from './rulesets.js';

const building = (rules, type) => rules.buildings.find((b) => b.type === type) ?? {};
const upgrade = (rules, type) => rules.upgrades.find((u) => u.type === type) ?? {};

/** What a Command Center costs someone who owns `owned` already: base x (1 + percent/100)^owned, rounded down. */
function hqCost(base, percent, owned) {
  return Number((BigInt(base) * BigInt(100 + percent) ** BigInt(owned)) / BigInt(100) ** BigInt(owned));
}

/** The changes, by theme: [heading, [[title, text], ...]]. c = classic, s = spring. */
function changes(c, s) {
  const tower = building(s, 'GuardTower'), oldTower = building(c, 'GuardTower');
  const rapid = upgrade(s, 'RapidFire');
  const siege = upgrade(s, 'SiegeEngineering'), oldSiege = upgrade(c, 'SiegeEngineering');
  const hqBase = building(s, 'CommandCenter').cost;
  const hqPercent = s.extraCommandCenterCostPercent ?? 0;
  const prices = [0, 1, 2, 3].map((n) => fmt(hqCost(hqBase, hqPercent, n))).join(', ');
  const room = s.units.filter((u) => u.occupancy).map((u) => `${unitName(u.type)} ${u.occupancy}`).join(', ');
  const farm = s.farm;
  return [
    ['Space', [
      [`A cell holds ${s.cellCapacity} points`, `Each unit takes room while it stands in a cell: ${room}. Units still walk through each other.`],
      ['First in keeps the cell', 'A unit that stops where there isn’t room is bumped to the nearest cell that has some. When two players’ units arrive together and don’t all fit, they take turns, one each.'],
      [`${s.minersPerEdge} dig from each cell`, `${s.minersPerEdge} workers dig from each cell beside a patch, and others there wait their turn. A worker bumped away looks within sight for a free spot, and stops if there isn’t one.`],
    ]],
    [say('Ore', 'Grubs'), [
      ['Patches grow back', `A patch below its starting amount regrows ${(s.regrowPartsPerMillion / 10000).toLocaleString()}% of what it holds every tick, never past where it started. Dig one to 0 and it’s gone.`],
      farm ? [`${buildingName('Farm')}s`, `A farm: ${fmt(building(s, 'Farm').cost)} to build, grows ${fmt(farm.grubs)} ${ORE}, dug ${farm.gatherPerTick} a tick (a patch: ${s.economy.gatherPerTick}). When it’s empty, send a worker to replant it for ${fmt(farm.replantCost)}.`] : null,
      ['Spoils', `Flatten an enemy building and bank ${s.spoilsPercent}% of what it cost.`],
      ['Every HQ costs more', `Each costs ${hqPercent}% more than the one before: ${prices}.`],
    ]],
    ['Defense', [
      [`A slower ${plainBuildingName('GuardTower')}`, `It fires every ${tower.attackCooldown} ticks (was ${oldTower.attackCooldown === 1 ? 'every tick' : `every ${oldTower.attackCooldown}`}) and costs ${fmt(tower.cost)} (was ${fmt(oldTower.cost)}). New class: ${upgradeName('RapidFire')}, ${fmt(rapid.cost)} ${ORE}. ${(rapid.effect ?? '').replace(/^Guard towers/, `${plainBuildingName('GuardTower')}s`)}.`],
      ['Tougher buildings', `${upgradeName('Fortify1')}: ${upgrade(s, 'Fortify1').effect} (was ${upgrade(c, 'Fortify1').effect.replace(' for all completed buildings', '')}).`],
    ]],
    ['Siege', [
      [`${unitName('Trebuchet')}s sooner`, `${upgradeName('SiegeEngineering')} costs ${fmt(siege.cost)} and takes ${siege.researchTicks} ticks (was ${fmt(oldSiege.cost)} and ${oldSiege.researchTicks}).`],
      ['Shells splash', `A shell also hits enemies on the 8 cells around where it lands, for ${s.shellSplashPercent}% of the damage.`],
    ]],
    ['At the clock', [
      s.scoreNetWorth ? ['Score what you own', `If nobody is out by the tick limit, the score is destruction + survival + ${ORE} in the bank. Every ${say('bit of ore', 'grub')} you ever dug used to count too (twice, once spent); now it only breaks ties.`] : null,
    ]],
  ].map(([heading, items]) => [heading, items.filter(Boolean)]).filter(([, items]) => items.length);
}

export function mountSpring(root) {
  document.title = `Spring rules · ${GAME}`;
  const scope = mountScope();
  setHtml(root, html`<p class="empty">Loading the rules…</p>`);

  scope.wait(Promise.all([ruleSets(), api('/api/v1/rulesets/classic'), api(`/api/v1/rulesets/${SPRING}`)]))
    .then(([sets, classic, spring]) => {
      const live = springIsDefault(sets);
      setHtml(root, html`
        <article class="spring-page">
          <header>
            <p class="kicker">${say('NetRTS', 'Badger Brawl')} · Spring 2027</p>
            <h1 class="headline">${live ? 'The spring rules' : 'New rules for the spring'}</h1>
            <p class="lede">${rolloutLine(sets)}</p>
            ${live ? '' : html`<p>Try them now: choose <b>Spring 2027 (draft)</b> under Rules when you start an exhibition on the
              <a href="#/">home page</a> or host a match from your <a href="#/account">account</a>.</p>`}
          </header>
          ${changes(classic, spring).map(([heading, items]) => html`
            <section>
              <h2>${heading}</h2>
              <ul class="spring-list">
                ${items.map(([title, text]) => html`<li><h3>${title}</h3><p>${text}</p></li>`)}
              </ul>
            </section>`)}
          <section>
            <h2>For your ${say('bot', 'badger')}</h2>
            <ul class="spring-bot">
              <li>The API stays at <code>/api/v1</code> and every command works the same.</li>
              <li>Read the numbers for the match you’re in from <code>GET /api/v1/matches/{id}/rules</code>, not <code>/api/v1/rules</code>: a match can be played under either rule set.</li>
              <li>To opt a match in, send <code>"settings": { "rules": "${SPRING}" }</code> when you create it. <code>GET /api/v1/rulesets</code> lists the choices.</li>
              <li>New in state: a worker waiting its turn to dig shows activity <code>Waiting</code>; a farm has <code>grubs</code> left; scores have a <code>bank</code>.</li>
            </ul>
            <p class="small muted">Draft: the numbers may still change before the season. The <a href="#/api">API page</a> has every call.</p>
          </section>
        </article>`);
    })
    .catch((e) => setHtml(root, html`<p class="error" role="alert">Couldn’t load the rules: ${e.message}</p>`));

  return () => scope.dispose();
}

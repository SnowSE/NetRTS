// What spectators read, in either brand: the original NetRts look or the Snow College "Badger Brawl"
// theme. The API keeps its own names (Worker, CommandCenter, "rusher", ore) so bots never break; this
// file only changes how the site says them.
//
// Snow College (Ephraim, Utah) opened in 1888 as Sanpete Stake Academy and was renamed for Lorenzo
// and Erastus Snow, not the weather. Every team gets its own campus landmark and its own housing.
//
// index.html sets <html data-brand> from localStorage before first paint; switching reloads the page.

export const SNOW = document.documentElement.dataset.brand === 'snow';

/** Pick the text for the active brand. */
export const say = (classic, snow) => (SNOW ? snow : classic);

export const GAME = say('NetRts', 'Badger Brawl');


/** Each team's headquarters is a different Snow College building, by player slot. */
export const HQ_NAME = [
  'Noyes Building', 'Greenwood Student Center', 'Eccles Center', 'Huntsman Library',
  'Humanities Building', 'Social Science Building', 'Business Building', 'Health Science Center',
  'Horne Activity Center', 'Bergeson Athletic Center', 'Badger Stadium', 'Lucy Phillips Building',
  'Career Center', 'Planetarium', 'Sevier Valley Center', 'Richfield Administration Building',
];

/** Each team trains its army out of different student housing, by player slot: on campus first, then Ephraim apartments. */
export const HOUSING_NAME = [
  'Suites at Academy Square', 'Anderson Hall', 'Mary Nielson Hall', 'Snow Hall',
  'Nuttall Hall', 'Castilleja Hall', 'Summit Valley Apartments', 'Badger Studios',
  'Nordic Point', 'Snow Garden', 'Pinetree Condominiums', 'Acorn & Oaktree',
  'Badger House', 'Badger Loft', 'The Up House', 'Alpine Canyon Apartments',
];

export const BUILDING_NAME = {
  CommandCenter: 'HQ',
  Barracks: 'Housing',
  ResourceDepot: 'Co-op Store',
  TechLab: 'GRSC Makerspace',
  GuardTower: 'Guard Tower',
};

/** Upgrades are coursework: tier 1 is the intro class, tier 2 the upper-division one. */
export const UPGRADE_NAME = {
  Weapons1: 'Intro to Chemistry', Weapons2: 'Organic Chemistry',
  Armor1: 'Unit Testing', Armor2: 'Code Review',
  Mobility1: 'Algorithms', Mobility2: 'Parallel Computing',
  Harvesting1: 'Intro to Biology', Harvesting2: 'Entomology',
};

/** House bots keep their API names; these are their mascot-league nicknames. */
export const BOT_NICKNAME = {
  sitter: 'Sleepy Sitter',
  rusher: 'Honey Badger',
  balanced: 'Blue Badger',
  economist: 'Grub Hoarder',
};

/** The resource: ore in the API, grubs on the field. Badgers dig for them. */
export const ORE = say('ore', 'grubs');

const bySlot = (list, slot) => list[((slot % list.length) + list.length) % list.length];

/** "CommandCenter" -> "Command Center". */
export const spaced = (s) => String(s ?? '').replace(/([a-z])([A-Z0-9])/g, '$1 $2');

/** Units keep their API names (Worker, Soldier, Archer, Scout) in every theme. */
export const unitName = (t) => t;
export const upgradeName = (t) => (SNOW ? UPGRADE_NAME[t] ?? t : spaced(t));
export const hqName = (slot) => bySlot(HQ_NAME, slot);
export const housingName = (slot) => bySlot(HOUSING_NAME, slot);
/** A house bot's nickname, or null in the original brand. */
export const nickname = (bot) => (SNOW ? BOT_NICKNAME[bot] ?? null : null);

/** A building's display name; in the Snow theme HQs and housing are named per team when the owner slot is known. */
export function buildingName(type, owner) {
  if (!SNOW) return spaced(type);
  if (owner != null && type === 'CommandCenter') return hqName(owner);
  if (owner != null && type === 'Barracks') return housingName(owner);
  return BUILDING_NAME[type] ?? type;
}

/** Battle-log lines come from the engine in API terms; the Snow theme retells them in badger. */
export function badgerize(text) {
  if (!SNOW) return String(text ?? '');
  return String(text ?? '')
    .replace(/\bhouse-(\w+)/g, (m, bot) => BOT_NICKNAME[bot] ?? m)
    .replace(/\b(CommandCenter|Barracks|ResourceDepot|TechLab|GuardTower)\b/g, (m) => BUILDING_NAME[m])
    .replace(/\b((?:Weapons|Armor|Mobility|Harvesting)[12])\b/g, (m) => UPGRADE_NAME[m])
    .replace(/\bDeposit\b/g, 'Grub patch')
    .replace(/\bKilled enemy\b/g, 'Bowled over enemy')
    .replace(/\bDestroyed enemy\b/g, 'Flattened enemy');
}

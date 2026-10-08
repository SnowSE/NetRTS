// Badger Brawl vocabulary: what spectators read. The API keeps its own names (Worker, CommandCenter,
// "rusher", ore) so bots never break; this file only changes how the Snow College site says them.
//
// Snow College (Ephraim, Utah) opened in 1888 as Sanpete Stake Academy and was renamed for Lorenzo
// and Erastus Snow, not the weather. Every team gets its own campus landmark and its own housing.

export const GAME = 'Badger Brawl';

export const UNIT_NAME = { Worker: 'Digger', Soldier: 'Soldier', Archer: 'Archer', Scout: 'Scout' };

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
export const ORE = 'grubs';

const bySlot = (list, slot) => list[((slot % list.length) + list.length) % list.length];

export const unitName = (t) => UNIT_NAME[t] ?? t;
export const upgradeName = (t) => UPGRADE_NAME[t] ?? t;
export const hqName = (slot) => bySlot(HQ_NAME, slot);
export const housingName = (slot) => bySlot(HOUSING_NAME, slot);

/** A building's field name; HQs and housing are named per team when the owner slot is known. */
export function buildingName(type, owner) {
  if (owner != null && type === 'CommandCenter') return hqName(owner);
  if (owner != null && type === 'Barracks') return housingName(owner);
  return BUILDING_NAME[type] ?? type;
}

/** Battle-log lines come from the engine in API terms; retell them in badger. */
export function badgerize(text) {
  return String(text ?? '')
    .replace(/\bhouse-(\w+)/g, (m, bot) => BOT_NICKNAME[bot] ?? m)
    .replace(/\b(CommandCenter|Barracks|ResourceDepot|TechLab|GuardTower)\b/g, (m) => BUILDING_NAME[m])
    .replace(/\bWorker\b/g, UNIT_NAME.Worker)
    .replace(/\b((?:Weapons|Armor|Mobility|Harvesting)[12])\b/g, (m) => UPGRADE_NAME[m])
    .replace(/\bDeposit\b/g, 'Grub patch')
    .replace(/\bKilled enemy\b/g, 'Bowled over enemy')
    .replace(/\bDestroyed enemy\b/g, 'Flattened enemy');
}

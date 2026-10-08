// Badger Brawl vocabulary: what spectators read. The API keeps its own names (Worker, CommandCenter,
// "rusher", ore) so bots never break; this file only changes how the Snow College site says them.
//
// Snow College (Ephraim, Utah) opened in 1888 as Sanpete Stake Academy, holding its first classes
// above the Co-op Store, and was renamed for Lorenzo and Erastus Snow, not the weather. Ephraim
// families sold their "Sunday eggs" to help pay for the school, so that's what everything costs here.

export const GAME = 'Badger Brawl';

export const UNIT_NAME = { Worker: 'Digger', Soldier: 'Linebacker', Archer: 'Quarterback', Scout: 'Wide Receiver' };

/** Every team's headquarters is a different landmark on the Ephraim campus, by player slot. */
export const HQ_NAME = ['Noyes Building', 'Greenwood Student Center', 'Eccles Center', 'Huntsman Library'];

export const BUILDING_NAME = {
  CommandCenter: 'HQ',
  Barracks: 'Badger Stadium',
  ResourceDepot: 'Co-op Store',
  TechLab: 'Graham Science Center',
  GuardTower: 'Snow Hall',
};

export const UPGRADE_NAME = {
  Weapons1: 'Strength & Conditioning I', Weapons2: 'Strength & Conditioning II',
  Armor1: 'Thicker Fur I', Armor2: 'Thicker Fur II',
  Mobility1: 'Track & Field I', Mobility2: 'Track & Field II',
  Harvesting1: 'Ag Science I', Harvesting2: 'Ag Science II',
};

/** House bots keep their API names; these are their mascot-league nicknames. */
export const BOT_NICKNAME = {
  sitter: 'Sleepy Sitter',
  rusher: 'Honey Badger',
  balanced: 'Blue Badger',
  economist: 'Egg Hoarder',
};

/** The resource: ore in the API, Sunday eggs on the field. */
export const ORE = 'eggs';

export const unitName = (t) => UNIT_NAME[t] ?? t;
export const upgradeName = (t) => UPGRADE_NAME[t] ?? t;

/** A building's field name; a team's HQ is named for its campus landmark when the owner slot is known. */
export function buildingName(type, owner) {
  if (type === 'CommandCenter' && owner != null) return HQ_NAME[((owner % 4) + 4) % 4];
  return BUILDING_NAME[type] ?? type;
}

/** Battle-log lines come from the engine in API terms; retell them in badger. */
export function badgerize(text) {
  return String(text ?? '')
    .replace(/\bhouse-(\w+)/g, (m, bot) => BOT_NICKNAME[bot] ?? m)
    .replace(/\b(CommandCenter|Barracks|ResourceDepot|TechLab|GuardTower)\b/g, (m) => BUILDING_NAME[m])
    .replace(/\b(Worker|Soldier|Archer|Scout)\b/g, (m) => UNIT_NAME[m])
    .replace(/\b((?:Weapons|Armor|Mobility|Harvesting)[12])\b/g, (m) => UPGRADE_NAME[m])
    .replace(/\bDeposit\b/g, 'Henhouse')
    .replace(/\bKilled enemy\b/g, 'Sacked enemy')
    .replace(/\bDestroyed enemy\b/g, 'Flattened enemy');
}

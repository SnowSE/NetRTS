// Badger Brawl vocabulary: what spectators read. The API keeps its own names (Worker, CommandCenter,
// "rusher", ore) so bots never break; this file only changes how the Snow College site says them.

export const GAME = 'Badger Brawl';

export const UNIT_NAME = { Worker: 'Digger', Soldier: 'Brawler', Archer: 'Snowballer', Scout: 'Sniffer' };

export const BUILDING_NAME = {
  CommandCenter: 'Sett',
  Barracks: 'Den',
  ResourceDepot: 'Larder',
  TechLab: 'Lecture Hall',
  GuardTower: 'Snow Fort',
};

export const UPGRADE_NAME = {
  Weapons1: 'Sharper Claws I', Weapons2: 'Sharper Claws II',
  Armor1: 'Thicker Fur I', Armor2: 'Thicker Fur II',
  Mobility1: 'Snowshoes I', Mobility2: 'Snowshoes II',
  Harvesting1: 'Digging Degree I', Harvesting2: 'Digging Degree II',
};

/** House bots keep their API names; these are their mascot-league nicknames. */
export const BOT_NICKNAME = {
  sitter: 'Sleepy Sitter',
  rusher: 'Honey Badger',
  balanced: 'Blue Badger',
  economist: 'Grub Hoarder',
};

/** The resource: ore in the API, grubs on the field. */
export const ORE = 'grubs';

export const unitName = (t) => UNIT_NAME[t] ?? t;
export const buildingName = (t) => BUILDING_NAME[t] ?? t;
export const upgradeName = (t) => UPGRADE_NAME[t] ?? t;

/** Battle-log lines come from the engine in API terms; retell them in badger. */
export function badgerize(text) {
  return String(text ?? '')
    .replace(/\bhouse-(\w+)/g, (m, bot) => BOT_NICKNAME[bot] ?? m)
    .replace(/\b(CommandCenter|Barracks|ResourceDepot|TechLab|GuardTower)\b/g, (m) => BUILDING_NAME[m])
    .replace(/\b(Worker|Soldier|Archer|Scout)\b/g, (m) => UNIT_NAME[m])
    .replace(/\b((?:Weapons|Armor|Mobility|Harvesting)[12])\b/g, (m) => UPGRADE_NAME[m])
    .replace(/\bDeposit\b/g, 'Grub patch')
    .replace(/\bKilled enemy\b/g, 'Bowled over enemy')
    .replace(/\bDestroyed enemy\b/g, 'Flattened enemy');
}

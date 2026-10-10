import { readFileSync, writeFileSync } from 'node:fs';
for (const [name, keepFog] of [['before', false], ['after', true], ['fixed', false]]) {
  const d = JSON.parse(readFileSync(`data/${name}.json`));
  const frames = d.frames.map((f) => ({
    tick: f.tick, status: f.status, tickIntervalMs: 1000,
    players: f.players.map(({ visibility, playerId, ...p }) => (keepFog ? { ...p, playerId, visibility } : { ...p, playerId })),
    units: f.units.map(({ destination, ...u }) => u),
    buildings: f.buildings.map(({ rally, ...b }) => b),
    resources: f.resources, events: f.events, outcome: f.outcome,
  }));
  writeFileSync(`data/${name}.js`, `window.MATCHES=window.MATCHES||{};window.MATCHES[${JSON.stringify(name)}]=` + JSON.stringify({ map: d.map, frames, result: d.result }) + ';');
}

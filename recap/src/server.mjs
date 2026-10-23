// Talking to the game server: the night's brief, the match of the night's frames, and publishing.
import { config } from './config.mjs';

/** The server has no recap endpoints, or they're turned off (no Recap:ApiKey there). */
export class RecapsDisabled extends Error {}

async function call(path, init = {}) {
  const res = await fetch(`${config.server}${path}`, {
    ...init,
    headers: { accept: 'application/json', 'X-Recap-Key': config.recapKey, ...(init.headers ?? {}) },
  });
  if (!res.ok) throw new Error(`${init.method ?? 'GET'} ${path}: ${res.status} ${await res.text()}`);
  return res;
}

export async function getBrief(date) {
  const path = `/api/v1/recaps/brief${date ? `?date=${date}` : ''}`;
  const res = await fetch(`${config.server}${path}`, { headers: { accept: 'application/json', 'X-Recap-Key': config.recapKey } });
  if (res.status === 404) throw new RecapsDisabled(`${config.server} doesn't serve recaps (${(await res.text()).slice(0, 200)})`);
  if (!res.ok) throw new Error(`GET ${path}: ${res.status} ${await res.text()}`);
  return res.json();
}

export async function publishEpisode(episode) {
  return (await call('/api/v1/recaps', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(episode),
  })).json();
}

/** The busiest stretch of a finished match, as compact frames for the big screen. Null if it can't be had. */
export async function matchClip(matchId, ticks = 120) {
  let manifest;
  try {
    manifest = await (await call(`/api/v1/matches/${matchId}/frames`)).json();
  } catch (e) {
    console.warn(`No frames for the match of the night: ${e.message}`);
    return null;
  }

  // Where the fighting was: kills and eliminations per tick, then the window with the most.
  const heat = new Map();
  for (const e of manifest.events ?? []) {
    const w = e.kind === 'PlayerEliminated' ? 8 : e.kind === 'BuildingDestroyed' ? 3 : e.kind === 'UnitKilled' ? 1 : 0;
    if (w) heat.set(e.tick, (heat.get(e.tick) ?? 0) + w);
  }
  const last = manifest.lastTick;
  let best = Math.max(0, last - ticks), bestHeat = -1;
  for (let start = 0; start <= Math.max(0, last - ticks); start += 5) {
    let h = 0;
    for (let t = start; t < start + ticks; t++) h += heat.get(t) ?? 0;
    if (h > bestHeat) { bestHeat = h; best = start; }
  }

  const frames = [];
  for (let chunk = Math.floor(best / manifest.chunkTicks); chunk * manifest.chunkTicks <= best + ticks && chunk < manifest.chunkCount; chunk++) {
    const text = await (await call(`/api/v1/matches/${matchId}/frames/${chunk}`)).text();
    for (const line of text.split('\n')) {
      if (!line) continue;
      const f = JSON.parse(line);
      if (f.tick < best || f.tick > best + ticks) continue;
      frames.push({
        tick: f.tick,
        units: f.units.map((u) => [u.x, u.y, u.owner, ['Worker', 'Soldier', 'Archer', 'Scout', 'Trebuchet'].indexOf(u.type), +(u.hp / u.maxHp).toFixed(2)]),
        buildings: f.buildings.map((b) => [b.x, b.y, b.owner, b.type, +(b.hp / b.maxHp).toFixed(2), b.completed ? 1 : 0]),
        resources: f.resources.map((r) => [r.x, r.y]),
        players: f.players.map((p) => ({ slot: p.slot, name: p.name, eliminated: p.eliminated, score: p.score.destruction + p.score.economy + p.score.survival })),
      });
    }
  }
  return frames.length ? { matchId, terrain: manifest.map.terrain, width: manifest.map.width, height: manifest.map.height, frames } : null;
}

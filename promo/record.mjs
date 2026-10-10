// Records a house-bot exhibition from a local server: map + every spectator snapshot (with fog data).
// node record.mjs <out-name> <seed> <ticks> bot1 bot2 ...
import { writeFileSync } from 'node:fs';
const [name, seed, maxTicks, ...bots] = process.argv.slice(2);
const base = process.env.SERVER ?? 'http://localhost:5080';
const post = async (p, body) => (await fetch(base + p, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })).json();
const get = async (p) => (await fetch(base + p)).json();
const settings = { tickIntervalMs: 100, maxTicks: +maxTicks, seed: +seed };
const m = await post('/api/v1/exhibitions', { bots, settings });
if (!m.matchId) { console.error(m); process.exit(1); }
let map;
for (;;) { const r = await fetch(`${base}/api/v1/matches/${m.matchId}/map`); if (r.ok) { map = await r.json(); break; } await new Promise((r) => setTimeout(r, 200)); }
const res = await fetch(`${base}/api/v1/matches/${m.matchId}/spectate/stream?fog=true`);
const frames = []; let buf = '';
const dec = new TextDecoder();
outer: for await (const chunk of res.body) {
  buf += dec.decode(chunk, { stream: true });
  let i;
  while ((i = buf.indexOf('\n\n')) >= 0) {
    const ev = buf.slice(0, i); buf = buf.slice(i + 2);
    if (!ev.startsWith('data: ')) continue;
    const s = JSON.parse(ev.slice(6));
    if (!frames.length || frames.at(-1).tick !== s.tick) frames.push(s);
    if (s.status === 'Completed') break outer;
  }
}
let result = null; try { result = await get(`/api/v1/matches/${m.matchId}/result`); } catch {}
writeFileSync(`data/${name}.json`, JSON.stringify({ map, frames, result }));
console.log(name, 'ticks', frames.length, 'last', frames.at(-1).tick, 'winner', JSON.stringify(result?.winner ?? result?.outcome ?? frames.at(-1).outcome));

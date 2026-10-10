// Waits for match.txt in a bot folder, then records that match's spectator stream.
import { readFileSync, writeFileSync, existsSync, rmSync } from 'node:fs';
const [dir, name] = process.argv.slice(2);
const base = 'http://localhost:5181';
const f = `${dir}/match.txt`;
while (!existsSync(f)) await new Promise((r) => setTimeout(r, 100));
const id = readFileSync(f, 'utf8').trim(); rmSync(f);
let map;
for (;;) { const r = await fetch(`${base}/api/v1/matches/${id}/map`); if (r.ok) { map = await r.json(); break; } await new Promise((r) => setTimeout(r, 100)); }
const res = await fetch(`${base}/api/v1/matches/${id}/spectate/stream?fog=true`);
const frames = []; let buf = ''; const dec = new TextDecoder();
outer: for await (const c of res.body) {
  buf += dec.decode(c, { stream: true }); let i;
  while ((i = buf.indexOf('\n\n')) >= 0) {
    const ev = buf.slice(0, i); buf = buf.slice(i + 2);
    if (!ev.startsWith('data: ')) continue;
    const s = JSON.parse(ev.slice(6));
    if (!frames.length || frames.at(-1).tick !== s.tick) frames.push(s);
    if (s.status === 'Completed') break outer;
  }
}
const result = await (await fetch(`${base}/api/v1/matches/${id}/result`)).json();
writeFileSync(`../data/${name}.json`, JSON.stringify({ map, frames, result }));
console.log(name, frames.length, JSON.stringify(result.outcome), result.players.map((p) => `${p.name}:${p.score.total}`).join(' '));

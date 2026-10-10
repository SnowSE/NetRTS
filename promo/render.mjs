// Render a cut to MP4: frame-exact screenshots of promo.html piped into ffmpeg, in parallel segments,
// then joined and muxed with the soundtrack. node render.mjs short|long [fps] [workers]
import { chromium } from 'playwright-core';
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, rmSync } from 'node:fs';

const cut = process.argv[2] ?? 'short';
const FPS = +(process.argv[3] ?? 30);
const WORKERS = +(process.argv[4] ?? 3);
mkdirSync('out/tmp', { recursive: true });

const browser = await chromium.launch({
  executablePath: `${process.env.LOCALAPPDATA}/ms-playwright/chromium-1217/chrome-win64/chrome.exe`,
  args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist'],
});

const run = (args) => new Promise((resolve, reject) => {
  const p = spawn('ffmpeg', args, { stdio: ['ignore', 'ignore', 'inherit'] });
  p.on('exit', (c) => (c === 0 ? resolve() : reject(new Error(`ffmpeg ${c}`))));
});

async function segment(w, total, first, last) {
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  await page.goto(`http://localhost:8123/promo/promo.html?cut=${cut}&render=1`);
  await page.waitForFunction(() => window.ready === true, null, { timeout: 180000 });
  await page.evaluate(() => document.fonts.ready);
  const file = `out/tmp/${cut}-${w}.mp4`;
  const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'mjpeg', '-i', '-',
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '17', '-pix_fmt', 'yuv420p', '-r', String(FPS), file], { stdio: ['pipe', 'ignore', 'inherit'] });
  const done = new Promise((r) => ff.on('exit', r));
  // Warm up the frame before the segment so stateful bits (unit facing) match the previous segment.
  if (first > 0) await page.evaluate((t) => window.seek(t), (first - 1) / FPS);
  const t0 = Date.now();
  for (let f = first; f < last; f++) {
    await page.evaluate((t) => window.seek(t), f / FPS);
    const jpg = await page.screenshot({ type: 'jpeg', quality: 95, clip: { x: 0, y: 0, width: 1920, height: 1080 } });
    if (!ff.stdin.write(jpg)) await new Promise((r) => ff.stdin.once('drain', r));
    if ((f - first) % 150 === 0) console.log(`[${cut} w${w}] ${f - first}/${last - first} frames, ${((Date.now() - t0) / 1000).toFixed(0)}s`);
  }
  ff.stdin.end();
  await done;
  await page.close();
  return file;
}

const probe = await browser.newPage();
await probe.goto(`http://localhost:8123/promo/promo.html?cut=${cut}&render=1`);
await probe.waitForFunction(() => window.ready === true, null, { timeout: 180000 });
const duration = await probe.evaluate(() => window.duration);
await probe.close();
const total = Math.round(duration * FPS);
const per = Math.ceil(total / WORKERS);
const files = await Promise.all(Array.from({ length: WORKERS }, (_, w) => segment(w, total, w * per, Math.min(total, (w + 1) * per))));
await browser.close();

writeFileSync(`out/tmp/${cut}-list.txt`, files.map((f) => `file '${f.replace('out/tmp/', '')}'`).join('\n'));
const name = cut === 'short' ? 'badger-brawl-30s' : 'badger-brawl-2min';
await run(['-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', `out/tmp/${cut}-list.txt`, '-i', `audio-${cut}.wav`,
  '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-af', 'loudnorm=I=-14:TP=-1.0:LRA=11', '-c:a', 'aac', '-b:a', '256k', '-ar', '48000',
  '-t', String(duration), '-movflags', '+faststart', `out/${name}.mp4`]);
for (const f of files) rmSync(f);
console.log(`out/${name}.mp4`, duration, 's');

// node snap.mjs <cut> t1 t2 ... -> shots/<cut>-<t>.jpg
import { chromium } from 'playwright-core';
import { mkdirSync } from 'node:fs';
const [cut, ...times] = process.argv.slice(2);
mkdirSync('shots', { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROME ?? `${process.env.LOCALAPPDATA}/ms-playwright/chromium-1217/chrome-win64/chrome.exe`, args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist', '--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') console.log('console:', m.text()); });
page.on('pageerror', (e) => console.log('pageerror:', e.message));
await page.goto(`http://localhost:8123/promo/promo.html?cut=${cut}&render=1`);
await page.waitForFunction(() => window.ready === true, null, { timeout: 120000 });
for (const t of times) {
  const t0 = Date.now();
  await page.evaluate((t) => window.seek(+t), t);
  await page.screenshot({ path: `shots/${cut}-${t}.jpg`, type: 'jpeg', quality: 85, clip: { x: 0, y: 0, width: 1920, height: 1080 } });
  console.log(t, Date.now() - t0, 'ms');
}
console.log(await page.evaluate(() => { const g = document.querySelector('#gl').getContext('webgl2'); const d = g.getExtension('WEBGL_debug_renderer_info'); return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) : 'n/a'; }));
await browser.close();

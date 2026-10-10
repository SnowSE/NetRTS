import { chromium } from 'playwright-core';
import { writeFileSync } from 'node:fs';
const browser = await chromium.launch({ executablePath: `${process.env.LOCALAPPDATA}/ms-playwright/chromium-1217/chrome-win64/chrome.exe`, args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist'] });
for (const cut of ['short', 'long']) {
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  await page.goto(`http://localhost:8123/promo/promo.html?cut=${cut}&render=1`);
  await page.waitForFunction(() => window.ready === true, null, { timeout: 120000 });
  const info = await page.evaluate(() => ({ duration: window.duration, cues: window.cues }));
  writeFileSync(`cues-${cut}.json`, JSON.stringify(info));
  console.log(cut, info.duration, info.cues.length, [...new Set(info.cues.map((c) => c.type))].join(','));
  await page.close();
}
await browser.close();

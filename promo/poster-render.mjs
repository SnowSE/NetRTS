// node poster-render.mjs -> out/badger-brawl-season-poster.pdf + .png
import { chromium } from 'playwright-core';
import { pathToFileURL } from 'node:url';
const browser = await chromium.launch({ executablePath: process.env.CHROME ?? `${process.env.LOCALAPPDATA}/ms-playwright/chromium-1217/chrome-win64/chrome.exe` });
const page = await browser.newPage({ viewport: { width: 816, height: 1056 }, deviceScaleFactor: 3 });
await page.goto(pathToFileURL('poster.html').href);
await page.waitForLoadState('networkidle');
await page.screenshot({ path: 'out/badger-brawl-season-poster.png' });

// Flatten the backdrop (hero, mask fade, glows) to one 300 dpi JPEG. PDF viewers and printers
// often drop CSS masks and background images, but a plain <img> always prints.
await page.addStyleTag({ content: '.page > :not(.hero) { visibility: hidden; }' });
const bg = (await page.screenshot({ type: 'jpeg', quality: 92 })).toString('base64');
await page.evaluate((src) => {
  document.querySelectorAll('style')[1].remove();
  document.querySelector('.hero').remove();
  const img = Object.assign(document.createElement('img'), { src });
  img.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
  const p = document.querySelector('.page');
  p.style.background = 'none';
  p.prepend(img);
}, `data:image/jpeg;base64,${bg}`);
await page.waitForLoadState('networkidle');
await page.pdf({ path: 'out/badger-brawl-season-poster.pdf', width: '8.5in', height: '11in', printBackground: true, preferCSSPageSize: true });
await browser.close();

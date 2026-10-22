// Renders the recap hosts (Zip the Scout, Sarge the Soldier, Quill the Archer) from the 3D game's own
// unit models into show/hosts/*.png. Run it again whenever the models in wwwroot/3d change:
//
//   dotnet run --project src/NetRts.Server                      # the game server, for its 3D files
//   cd recap && node tools/render-hosts.mjs [http://localhost:5080]
//
// Locally, CHROMIUM_CHANNEL=msedge uses the installed Edge.
import { chromium } from 'playwright-core';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', 'show', 'hosts');
const server = (process.argv[2] ?? 'http://localhost:5080').replace(/\/+$/, '');
const SIZE = 1024;

const browser = await chromium.launch({
  channel: process.env.CHROMIUM_CHANNEL,
  executablePath: process.env.CHROMIUM_PATH,
  args: ['--enable-gpu', '--ignore-gpu-blocklist'],
});
try {
  const page = await browser.newPage();
  page.on('console', (m) => { if (m.type() === 'error') console.error(m.text()); });
  // The page is ours, but it runs at the server's address so it can load the game's modules.
  await page.route(`${server}/render-hosts.html`, (route) =>
    route.fulfill({ contentType: 'text/html', body: readFileSync(path.join(here, 'render-hosts.html'), 'utf8') }));
  await page.goto(`${server}/render-hosts.html`);
  await page.waitForFunction(() => window.ready === true, null, { timeout: 60000 });
  mkdirSync(out, { recursive: true });
  for (const key of ['zip', 'sarge', 'quill']) {
    const url = await page.evaluate(([k, s]) => window.renderHost(k, s), [key, SIZE]);
    writeFileSync(path.join(out, `${key}.png`), Buffer.from(url.split(',')[1], 'base64'));
    console.log(`show/hosts/${key}.png`);
  }
} finally {
  await browser.close();
}

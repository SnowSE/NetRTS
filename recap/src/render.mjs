// Renders the show: the broadcast page in headless Chromium, one screenshot per frame, piped into
// ffmpeg with the audio track. The page draws everything from window.seek(t), so frames are exact.
import { chromium } from 'playwright-core';
import { spawn } from 'node:child_process';
import { pathToFileURL, fileURLToPath } from 'node:url';
import path from 'node:path';
import { config } from './config.mjs';

const showPage = pathToFileURL(path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'show', 'index.html')).href;

/** Renders the episode to an MP4 and a poster JPEG. */
export async function render({ show, audioFile, videoFile, posterFile }) {
  const browser = await chromium.launch({
    executablePath: config.chromiumPath,
    channel: config.chromiumChannel,
    args: ['--disable-gpu', '--force-color-profile=srgb', '--hide-scrollbars', '--allow-file-access-from-files'],
  });
  try {
    const page = await browser.newPage({ viewport: { width: config.width, height: config.height }, deviceScaleFactor: 1 });
    page.on('pageerror', (e) => console.error('show page error:', e.message));
    await page.goto(showPage);
    await page.evaluate((data) => window.load(data), show);
    await page.evaluate(() => document.fonts.ready);

    // The poster: a moment into the first line, hosts at the desk.
    const posterAt = (show.lines[0]?.start ?? 0) + 0.6;
    await page.evaluate((t) => window.seek(t), posterAt);
    await page.screenshot({ path: posterFile, type: 'jpeg', quality: 88 });

    const frames = Math.ceil(show.duration * config.fps);
    const ffmpeg = spawn(config.ffmpeg, [
      '-y', '-loglevel', 'error',
      '-f', 'image2pipe', '-framerate', String(config.fps), '-c:v', 'mjpeg', '-i', '-',
      '-i', audioFile,
      '-c:v', 'libx264', '-preset', 'medium', '-crf', '21', '-pix_fmt', 'yuv420p', '-r', String(config.fps),
      '-c:a', 'aac', '-b:a', '160k', '-shortest', '-movflags', '+faststart',
      videoFile,
    ], { stdio: ['pipe', 'ignore', 'inherit'] });
    const done = new Promise((resolve, reject) => {
      ffmpeg.on('error', reject);
      ffmpeg.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`ffmpeg exited with ${code}`))));
    });

    const started = Date.now();
    for (let f = 0; f < frames; f++) {
      await page.evaluate((t) => window.seek(t), f / config.fps);
      const jpg = await page.screenshot({ type: 'jpeg', quality: 92 });
      if (!ffmpeg.stdin.write(jpg)) await new Promise((r) => ffmpeg.stdin.once('drain', r));
      if (f % (config.fps * 30) === 0) {
        console.log(`  frame ${f}/${frames} (${((Date.now() - started) / 1000).toFixed(0)}s)`);
      }
    }
    ffmpeg.stdin.end();
    await done;
  } finally {
    await browser.close();
  }
}

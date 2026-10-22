// The nightly recap job: brief → script → voices → render → upload → publish.
//
//   node src/index.mjs                     # what the Container Apps job runs (see README.md)
//   node src/index.mjs --local --date 2026-10-20 --voices silent --script template
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { config, missingSettings } from './config.mjs';
import { getBrief, matchClip, publishEpisode, RecapsDisabled } from './server.mjs';
import { writeScript } from './script.mjs';
import { speak } from './voices.mjs';
import { buildTimeline, writeWav } from './timeline.mjs';
import { render } from './render.mjs';
import { pruneDay, upload, uploadJson } from './upload.mjs';

const step = (what) => console.log(`[recap] ${what}`);

async function main() {
  // Not set up (a fresh environment, a fork, a local container): do nothing, successfully.
  const missing = missingSettings();
  if (missing.length) {
    step(`not configured, so not running. Missing: ${missing.join(', ')}`);
    return;
  }

  step(`reading the brief${config.date ? ` for ${config.date}` : ''}`);
  let brief;
  try {
    brief = config.briefFile ? JSON.parse(readFileSync(config.briefFile, 'utf8')) : await getBrief(config.date);
  } catch (e) {
    if (!(e instanceof RecapsDisabled)) throw e;
    step(`${e.message}; not running`);
    return;
  }
  const dir = path.join(config.outDir, brief.date);
  mkdirSync(dir, { recursive: true });
  writeFileSync(path.join(dir, 'brief.json'), JSON.stringify(brief, null, 2));
  step(`${brief.date}: ${brief.totals.matches} matches (${brief.totals.realMatches} between real bots)${brief.quiet ? ', a quiet day' : ''}`);

  step('writing the script');
  const script = await writeScript(brief);
  writeFileSync(path.join(dir, 'script.json'), JSON.stringify(script, null, 2));
  step(`"${script.title}": ${script.lines.length} lines by ${script.writer}`);

  step('recording the voices');
  const spoken = await speak(script.lines);
  const timeline = buildTimeline(script, spoken, config.maxSeconds);
  const audioFile = path.join(dir, 'audio.wav');
  writeWav(audioFile, timeline.audio);
  step(`${timeline.lines.length} lines, ${timeline.duration.toFixed(1)} s`);

  const clip = brief.matchOfTheNight && !config.briefFile ? await matchClip(brief.matchOfTheNight.matchId) : null;

  step('rendering');
  const videoFile = path.join(dir, 'recap.mp4');
  const posterFile = path.join(dir, 'poster.jpg');
  await render({
    show: { brief, title: script.title, duration: timeline.duration, lines: timeline.lines, clip },
    audioFile,
    videoFile,
    posterFile,
  });
  step(`rendered ${videoFile}`);

  if (!config.publish) {
    step('local run: not uploading or publishing');
    return;
  }

  step('uploading');
  const video = await upload(videoFile, `${brief.date}/recap-${Date.now()}.mp4`, 'video/mp4');
  const thumbnail = await upload(posterFile, `${brief.date}/poster-${Date.now()}.jpg`, 'image/jpeg');

  // The episode's details go next to the video, so the server finds it by itself even if telling it
  // directly fails; then tell it directly, so it's on the site right away.
  const details = {
    date: brief.date,
    title: script.title,
    summary: script.summary,
    video,
    thumbnail,
    durationSeconds: Math.round(timeline.duration),
    transcript: timeline.lines.map((l) => ({ speaker: l.speaker, text: l.text, start: +l.start.toFixed(2) })),
  };
  await uploadJson(details, `${brief.date}/episode.json`);
  await pruneDay(brief.date, [video, thumbnail, `${brief.date}/episode.json`]);

  step('publishing');
  try {
    const episode = await publishEpisode(details);
    step(`published episode ${episode.id}`);
  } catch (e) {
    step(`couldn't tell the server (${e.message}); it will pick the episode up from storage`);
  }
}

main().catch((e) => {
  console.error('[recap] failed:', e);
  process.exit(1);
});

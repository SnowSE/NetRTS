// The podcast: each episode's soundtrack as an MP3, for people who'd rather listen than watch. The
// server lists them in a podcast feed (/recaps/podcast.xml) and links them next to each video.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { config } from './config.mjs';
import { download, listEpisodes, upload, uploadJson } from './upload.mjs';

/** Encodes an audio track (a WAV, or the sound of a video) as an MP3 tagged with the episode's title; returns its size in bytes. */
export async function encodeMp3(input, output, { title, date }) {
  await new Promise((resolve, reject) => {
    const ffmpeg = spawn(config.ffmpeg, [
      '-y', '-loglevel', 'error', '-i', input, '-vn',
      '-c:a', 'libmp3lame', '-b:a', '128k',
      '-metadata', `title=${title}`, '-metadata', `date=${date}`, '-metadata', 'genre=Podcast',
      output,
    ], { stdio: ['ignore', 'ignore', 'inherit'] });
    ffmpeg.on('error', reject);
    ffmpeg.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`ffmpeg exited with ${code}`))));
  });
  return statSync(output).size;
}

/**
 * Gives every published episode without an MP3 one, taken from its video's soundtrack, and adds it to
 * its episode.json; the server picks the change up from storage within a few minutes. Episodes from
 * before the podcast existed get theirs the first time this runs. Returns how many it did.
 */
export async function addMissingAudio() {
  let added = 0;
  for (const { name, episode } of await listEpisodes()) {
    if (episode.audio || !episode.video || /^https?:/.test(episode.video)) continue;
    const dir = mkdtempSync(path.join(tmpdir(), 'recap-audio-'));
    try {
      const video = path.join(dir, 'recap.mp4');
      const mp3 = path.join(dir, 'recap.mp3');
      await download(episode.video, video);
      const audioBytes = await encodeMp3(video, mp3, episode);
      const audio = await upload(mp3, `${episode.date}/recap-${Date.now()}.mp3`, 'audio/mpeg');
      await uploadJson({ ...episode, audio, audioBytes }, name);
      added++;
      console.log(`  ${episode.date}: added ${audio}`);
    } catch (e) {
      console.warn(`  ${episode.date}: couldn't add its audio (${e.message})`);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  }
  return added;
}

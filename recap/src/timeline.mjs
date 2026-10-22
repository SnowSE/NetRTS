// Lays the spoken lines out in time and mixes them into one audio track.
import { writeFileSync } from 'node:fs';
import { SAMPLE_RATE } from './voices.mjs';

export const INTRO_SECONDS = 4;
export const OUTRO_SECONDS = 3;
const GAP = 0.3;            // between lines
const SEGMENT_GAP = 0.9;    // when the big screen changes subject

/**
 * Places each line after the last, dropping middle lines if the whole show would run past
 * maxSeconds (the cold open, coming up, mentoring and sign-off always stay).
 * Returns { duration, lines: [{ ...line, start, seconds, visemes }], audio: Int16Array }.
 */
export function buildTimeline(script, spoken, maxSeconds) {
  let items = script.lines.map((line, i) => ({ ...line, ...spoken[i] }));
  const length = (list) => INTRO_SECONDS + OUTRO_SECONDS
    + list.reduce((t, l, i) => t + l.seconds + (i && l.graphic !== list[i - 1].graphic ? SEGMENT_GAP : GAP), 0);
  const keep = (l, i, all) => i < 2 || i >= all.length - 2 || ['upcoming', 'mentoring'].includes(l.graphic);
  while (length(items) > maxSeconds) {
    const droppable = items.filter((l, i) => !keep(l, i, items));
    if (!droppable.length) break;
    items = items.filter((l) => l !== droppable.reduce((a, b) => (b.seconds > a.seconds ? b : a)));
  }

  let t = INTRO_SECONDS;
  const lines = items.map((l, i) => {
    if (i) t += l.graphic !== items[i - 1].graphic ? SEGMENT_GAP : GAP;
    const placed = { speaker: l.speaker, text: l.text, mood: l.mood, graphic: l.graphic, focus: l.focus, start: t, seconds: l.seconds, visemes: l.visemes, pcm: l.pcm };
    t += l.seconds;
    return placed;
  });
  const duration = Math.min(maxSeconds, t + OUTRO_SECONDS);

  const audio = new Int16Array(Math.ceil(duration * SAMPLE_RATE));
  for (const l of lines) {
    const at = Math.round(l.start * SAMPLE_RATE);
    audio.set(l.pcm.subarray(0, Math.max(0, Math.min(l.pcm.length, audio.length - at))), at);
    delete l.pcm;
  }
  return { duration, lines, audio };
}

/** Writes 16-bit mono PCM as a WAV file. */
export function writeWav(file, samples) {
  const header = Buffer.alloc(44);
  const bytes = samples.length * 2;
  header.write('RIFF', 0, 'ascii');
  header.writeUInt32LE(36 + bytes, 4);
  header.write('WAVEfmt ', 8, 'ascii');
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(SAMPLE_RATE, 24);
  header.writeUInt32LE(SAMPLE_RATE * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36, 'ascii');
  header.writeUInt32LE(bytes, 40);
  writeFileSync(file, Buffer.concat([header, Buffer.from(samples.buffer, samples.byteOffset, bytes)]));
}

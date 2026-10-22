// The hosts' voices: Azure AI Speech neural voices, with viseme timings for lip sync. --voices silent
// times each line from its length and fakes the mouth, for local previews.
import sdk from 'microsoft-cognitiveservices-speech-sdk';
import { DefaultAzureCredential } from '@azure/identity';
import { config } from './config.mjs';
import { HOSTS } from './hosts.mjs';

export const SAMPLE_RATE = 24000;

/** Speaks every line: [{ pcm: Int16Array, seconds, visemes: [{ t, id }] }] in script order. */
export async function speak(lines) {
  if (config.voices === 'silent') return lines.map(silentLine);
  const synth = synthesizer();
  try {
    const out = [];
    for (const line of lines) out.push(await speakLine(synth, line));
    return out;
  } finally {
    synth.close();
  }
}

function synthesizer() {
  const speech = config.speechKey
    ? sdk.SpeechConfig.fromEndpoint(new URL(config.speechEndpoint), config.speechKey)
    : sdk.SpeechConfig.fromEndpoint(new URL(config.speechEndpoint), new DefaultAzureCredential());
  speech.speechSynthesisOutputFormat = sdk.SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm;
  // No audio device in a container: keep the audio in memory.
  return new sdk.SpeechSynthesizer(speech, null);
}

const escapeXml = (s) => s.replace(/[<>&'"]/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', "'": '&apos;', '"': '&quot;' }[c]));

function ssml(line) {
  const v = HOSTS[line.speaker].voice;
  const style = v.styles[line.mood];
  const lang = v.name.slice(0, 5);
  let body = `<prosody rate="${v.rate}" pitch="${v.pitch}">${escapeXml(line.text)}</prosody>`;
  if (style) body = `<mstts:express-as style="${style}" styledegree="${line.mood === 'excited' || line.mood === 'shouting' ? 1.6 : 1.1}">${body}</mstts:express-as>`;
  return `<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="https://www.w3.org/2001/mstts" xml:lang="${lang}">`
    + `<voice name="${v.name}">${body}</voice></speak>`;
}

function speakLine(synth, line) {
  const visemes = [];
  synth.visemeReceived = (_, e) => visemes.push({ t: e.audioOffset / 1e7, id: e.visemeId });
  return new Promise((resolve, reject) => {
    synth.speakSsmlAsync(ssml(line), (result) => {
      if (result.reason !== sdk.ResultReason.SynthesizingAudioCompleted) {
        reject(new Error(`Speech failed for "${line.text.slice(0, 40)}…": ${result.errorDetails}`));
        return;
      }
      const pcm = wavSamples(Buffer.from(result.audioData));
      resolve({ pcm, seconds: pcm.length / SAMPLE_RATE, visemes });
    }, reject);
  });
}

/** The 16-bit samples of a RIFF/WAVE file (finds the data chunk rather than assuming a 44-byte header). */
export function wavSamples(buf) {
  let at = 12;
  while (at + 8 <= buf.length) {
    const id = buf.toString('ascii', at, at + 4);
    const size = buf.readUInt32LE(at + 4);
    if (id === 'data') {
      const data = buf.subarray(at + 8, at + 8 + size);
      return new Int16Array(data.buffer.slice(data.byteOffset, data.byteOffset + data.length - (data.length % 2)));
    }
    at += 8 + size + (size % 2);
  }
  throw new Error('No audio in the speech result.');
}

/** A silent stand-in: as long as the line would take to say, with the mouth flapping on the words. */
function silentLine(line) {
  const words = line.text.split(/\s+/).filter(Boolean);
  const visemes = [];
  let t = 0.05;
  for (const w of words) {
    for (let i = 0; i < Math.max(1, Math.round(w.length / 2.5)); i++) {
      visemes.push({ t, id: [2, 11, 7, 1, 4][(visemes.length) % 5] });
      t += 0.11;
    }
    visemes.push({ t, id: 0 });
    t += /[.!?]$/.test(w) ? 0.3 : /[,;:]$/.test(w) ? 0.15 : 0.05;
  }
  const seconds = t + 0.2;
  return { pcm: new Int16Array(Math.round(seconds * SAMPLE_RATE)), seconds, visemes };
}

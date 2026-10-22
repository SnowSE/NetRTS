// Settings, from the environment (the Container Apps job) and the command line (local runs).
import { parseArgs } from 'node:util';

const { values: args } = parseArgs({
  options: {
    local: { type: 'boolean', default: false },     // write to out/ instead of uploading and publishing
    publish: { type: 'boolean', default: false },   // with --local: still upload and publish
    date: { type: 'string' },                       // the day to cover, yyyy-MM-dd (default: yesterday, server time)
    brief: { type: 'string' },                      // read the brief from this file instead of the server
    script: { type: 'string', default: 'auto' },    // auto (the model, the template if it fails) | model | template
    voices: { type: 'string', default: 'azure' },   // azure | silent
    fps: { type: 'string' },
    out: { type: 'string', default: 'out' },
  },
});

const env = process.env;

export const config = {
  local: args.local,
  publish: !args.local || args.publish,
  date: args.date ?? env.RECAP_DATE ?? null,
  briefFile: args.brief ?? null,
  outDir: args.out,

  /** The game server, and the key that lets this job read the brief and publish episodes. */
  server: (env.NETRTS_URL ?? '').replace(/\/+$/, ''),
  recapKey: env.RECAP_KEY ?? '',

  /**
   * The script writer: an Azure OpenAI deployment in Microsoft Foundry. AZURE_OPENAI_ENDPOINT is
   * https://{resource}.openai.azure.com/; RECAP_MODEL the deployment's name. Signs in with the job's
   * managed identity unless AZURE_OPENAI_API_KEY is set.
   */
  script: args.script,
  openAiEndpoint: env.AZURE_OPENAI_ENDPOINT ?? '',
  openAiKey: env.AZURE_OPENAI_API_KEY ?? '',
  model: env.RECAP_MODEL ?? 'recap-writer',

  /** Azure AI Speech: the resource endpoint (https://{name}.cognitiveservices.azure.com/), managed identity unless SPEECH_KEY is set. */
  voices: args.voices,
  speechEndpoint: env.SPEECH_ENDPOINT ?? '',
  speechKey: env.SPEECH_KEY ?? '',

  /** Where videos go: the blob service URL (managed identity) or a connection string (Azurite, as Aspire passes it). */
  blobUrl: env.RECAP_BLOB_URL ?? '',
  blobConnection: env.RECAP_BLOB_CONNECTION ?? env.ConnectionStrings__recaps ?? '',
  container: env.RECAP_CONTAINER ?? 'recaps',

  /** Rendering. Locally, CHROMIUM_CHANNEL=msedge uses the installed Edge. */
  fps: Number(args.fps ?? env.RECAP_FPS ?? 24),
  width: 1280,
  height: 720,
  ffmpeg: env.FFMPEG ?? 'ffmpeg',
  chromiumPath: env.CHROMIUM_PATH ?? undefined,
  chromiumChannel: env.CHROMIUM_CHANNEL ?? undefined,

  /** Never longer than this, however much happened. */
  maxSeconds: 600,
};

/**
 * The settings this run needs that aren't set. A real run needs all of them: the server, the model,
 * Speech and storage. A local preview (--local) can do without what it was told to skip
 * (--script template, --voices silent, no --publish).
 */
export function missingSettings() {
  const missing = [];
  const need = (name, value) => { if (!value) missing.push(name); };
  if (!config.briefFile) {
    need('NETRTS_URL', config.server);
    need('RECAP_KEY', config.recapKey);
  }
  if (!(config.local && config.script === 'template')) need('AZURE_OPENAI_ENDPOINT', config.openAiEndpoint);
  if (!(config.local && config.voices === 'silent')) need('SPEECH_ENDPOINT', config.speechEndpoint);
  if (config.publish) need('RECAP_BLOB_URL (or RECAP_BLOB_CONNECTION)', config.blobUrl || config.blobConnection);
  return missing;
}

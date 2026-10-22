// The episode's script: an Azure OpenAI model in Microsoft Foundry writes it from the night's brief;
// if that fails (an outage, a refusal), a plain template does, so there is always a show.
import { DefaultAzureCredential } from '@azure/identity';
import { z } from 'zod';
import { config } from './config.mjs';
import { HOSTS, SPEAKERS, GRAPHICS, MOODS } from './hosts.mjs';

const Line = z.object({
  speaker: z.enum(SPEAKERS),
  text: z.string().describe('What the host says, written to be spoken aloud: no emoji, no markdown, numbers as people say them.'),
  mood: z.enum(MOODS),
  graphic: z.enum(GRAPHICS).describe('What the big screen behind the desk shows while this line plays.'),
  focus: z.string().describe('The bot name, ladder category or other item to highlight on the graphic; empty for none.'),
});

const Script = z.object({
  title: z.string().describe('Episode title, punchy, under 70 characters.'),
  summary: z.string().describe('One or two sentences for the episode list.'),
  lines: z.array(Line),
});

/**
 * How many words the show should run to: short on a quiet day, longer the more happened, and never
 * more than about 8 minutes of talk (the 10-minute cap leaves room for the open, pauses and close).
 * The hosts speak about 2.4 words a second.
 */
export function wordBudget(brief) {
  if (brief.quiet) return 200;
  const t = brief.totals;
  const busy = 250 + 20 * t.realMatches + 40 * t.leagueMatches
    + 8 * Math.min(10, brief.achievements.length) + 25 * Math.min(6, brief.newBots.length) + 20 * Math.min(5, brief.surprises.length);
  return Math.min(1100, Math.round(busy / 50) * 50);
}

export async function writeScript(brief) {
  if (config.script === 'template') return templateScript(brief);
  try {
    return await modelScript(brief);
  } catch (e) {
    // A refusal or an outage shouldn't cost the night its show.
    if (config.script === 'model') throw e;
    console.warn(`The model couldn't write the script (${e.message}); using the template.`);
    return templateScript(brief);
  }
}

/** Structured outputs: the model's reply must match this schema (strict mode: every field required). */
const SCRIPT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['title', 'summary', 'lines'],
  properties: {
    title: { type: 'string', description: 'Episode title, punchy, under 70 characters.' },
    summary: { type: 'string', description: 'One or two sentences for the episode list.' },
    lines: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['speaker', 'text', 'mood', 'graphic', 'focus'],
        properties: {
          speaker: { type: 'string', enum: SPEAKERS },
          text: { type: 'string', description: 'What the host says, written to be spoken aloud: no emoji, no markdown, numbers as people say them.' },
          mood: { type: 'string', enum: MOODS },
          graphic: { type: 'string', enum: GRAPHICS, description: 'What the big screen behind the desk shows while this line plays.' },
          focus: { type: 'string', description: 'The bot name, ladder category or other item to highlight on the graphic; empty for none.' },
        },
      },
    },
  },
};

/** The deployment's auth header: its key if one is set, otherwise the managed identity's token. */
async function authHeader() {
  if (config.openAiKey) return { 'api-key': config.openAiKey };
  const token = await new DefaultAzureCredential().getToken('https://cognitiveservices.azure.com/.default');
  return { authorization: `Bearer ${token.token}` };
}

function systemPrompt(brief) {
  const hosts = SPEAKERS.map((k) => `- ${k}: ${HOSTS[k].who}`).join('\n');
  const words = wordBudget(brief);
  const budget = brief.quiet
    ? `about ${words} words in all: a short show, it was a quiet day`
    : `about ${words} words in all (${Math.round(words / 2.4 / 60)} minutes or so); stay within 10% of that. Spend the words on the best stories; skip the rest`;
  return `You write the script for "${brief.lore.game} Tonight", a nightly highlights show about a bot-programming league.
${brief.lore.setting}

Students write bots that play a real-time strategy game over a REST API. Each bot belongs to a person (its "owner"); house bots belong to the server.

The hosts are three of the game's own units, sitting behind a sports desk:
${hosts}

How they talk: like the announcers in a pod-race booth. Fast, theatrical, over the top; alliteration and wild
similes; they riff on each other, one finishing the other's sentence; Zip gasps, Sarge rumbles, Quill corrects.
The lines are original: never quote a film or use a real franchise's catchphrases or names.

Rules:
- Use only facts in the brief. Never invent a bot, a person, a number or a result. Say names exactly as given.
- Tease and roast bots, never people. Owners are students: be warm about them. Celebrate effort and improvement.
- Say "${brief.lore.ore}" for the resource. You may use the brief's lore names for flavour.
- Length: ${budget}. Lines are short: one to three sentences each. Mix the speakers; nobody talks twice in a row more than once or twice.
- Cover, in roughly this order, whatever the brief has: a cold open; new bots and their owners; the match of the night;
  power plays and surprises; new strategies (and bots switching styles); the ladders and who moved; achievements
  (first-ever and rare ones first); rivalries; bots we haven't seen in a while (a "missing persons" bit: we miss you, come back);
  what's coming up (always: ${brief.upcoming.schedule}${brief.upcoming.nextLeagueDay ? ` Next league day: ${brief.upcoming.nextLeagueDay}.` : ''}
  plus any league matches taking players); and a mentoring reminder (always: ${brief.mentoring}). Then a sign-off.
- Skip sections with nothing in them. If the day was quiet, say so with good humour, keep it short, and still do coming up and mentoring.
- Don't repeat the recent episode titles.
- For each line pick the graphic the big screen should show (match for the match of the night, ladder for standings, and so on)
  and put what to highlight in focus (a bot's name or a category), or leave focus empty.`;
}

async function modelScript(brief) {
  const res = await fetch(`${config.openAiEndpoint.replace(/\/+$/, '')}/openai/v1/chat/completions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({
      model: config.model,
      reasoning_effort: 'medium',
      max_completion_tokens: 16000,
      messages: [
        { role: 'system', content: systemPrompt(brief) },
        { role: 'user', content: `Tonight's brief (JSON):
${JSON.stringify(brief)}` },
      ],
      response_format: { type: 'json_schema', json_schema: { name: 'episode_script', strict: true, schema: SCRIPT_SCHEMA } },
    }),
    signal: AbortSignal.timeout(10 * 60_000),
  });
  if (!res.ok) throw new Error(`${res.status} ${(await res.text()).slice(0, 300)}`);
  const choice = (await res.json()).choices?.[0];
  if (choice?.message?.refusal) throw new Error(`the model declined: ${choice.message.refusal}`);
  if (choice?.finish_reason === 'length') throw new Error('the script ran too long');
  const script = Script.parse(JSON.parse(choice?.message?.content ?? '{}'));
  if (!script.lines.length) throw new Error('no script came back');
  return { ...script, lines: trimToBudget(script.lines, wordBudget(brief) * 1.15), writer: config.model };
}

/** Holds the script to its word budget: drops the longest lines from the middle, never the open, coming up, mentoring or sign-off. */
function trimToBudget(lines, maxWords) {
  const words = (l) => l.text.split(/\s+/).length;
  const kept = [...lines];
  while (kept.length > 6 && kept.reduce((n, l) => n + words(l), 0) > maxWords) {
    // Drop the longest line among the middle ones that isn't coming-up or mentoring.
    const middle = kept.slice(2, -3).filter((l) => !['upcoming', 'mentoring'].includes(l.graphic));
    if (!middle.length) break;
    kept.splice(kept.indexOf(middle.reduce((a, b) => (words(b) > words(a) ? b : a))), 1);
  }
  return kept;
}

// ------------------------------------------------------------------ template

const say = (speaker, text, graphic = 'none', focus = '', mood = 'excited') => ({ speaker, text, mood, graphic, focus });
const list = (items) => (items.length <= 1 ? items.join('') : `${items.slice(0, -1).join(', ')} and ${items.at(-1)}`);
const n = (x) => Number(x).toLocaleString('en-US');

/** A plain script from the brief, for when the model isn't available. */
export function templateScript(brief) {
  const lines = [];
  const game = brief.lore.game;
  lines.push(say('zip', `Welcome, welcome, welcome to ${game} Tonight! It's ${brief.weekday}, and what a day it was!`, 'title'));
  lines.push(say('sarge', brief.quiet
    ? `A quiet one. ${n(brief.totals.matches)} matches. Even the grubs took the day off.`
    : `${n(brief.totals.matches)} matches. ${n(brief.totals.unitsKilled)} units down. ${n(brief.totals.oreGathered)} ${brief.lore.ore} hauled home.`, 'totals', '', 'calm'));

  if (brief.newBots.length) {
    const names = brief.newBots.slice(0, 4).map((b) => (b.owner ? `${b.name}, from ${b.owner}` : b.name));
    lines.push(say('quill', `Fresh faces on the field: ${list(names)}. Welcome, all of you.`, 'newBots', brief.newBots[0].name, 'amused'));
  }

  const m = brief.matchOfTheNight;
  if (m) {
    const winner = m.players.find((p) => p.winner);
    lines.push(say('zip', `Match of the night! ${list(m.players.slice(0, 4).map((p) => p.name))}${m.players.length > 4 ? ' and more' : ''}, and oh, it was a scrap!`, 'match'));
    lines.push(say('quill', winner
      ? `${winner.name} took it with ${n(winner.score)} points${m.why.length ? `: ${m.why[0]}` : ''}.`
      : `A draw after ${n(m.ticks)} ticks. Nobody blinked.`, 'match', winner?.name ?? '', 'calm'));
  }

  for (const p of brief.powerPlays.slice(0, 2)) lines.push(say('sarge', `Power play: ${p.bot}, ${p.text}.`, 'powerPlays', p.bot, 'serious'));
  for (const s of brief.surprises.slice(0, 2)) lines.push(say('zip', `Didn't see that coming! ${s.bot}: ${s.text}!`, 'surprises', s.bot));

  const fresh = brief.strategies.filter((s) => s.newOnServer || s.newForBot).slice(0, 2);
  for (const s of fresh) {
    lines.push(say('quill', s.newOnServer
      ? `Something new on the board: ${s.bot} played a ${s.strategy}, a first for anyone here.`
      : `${s.bot} switched it up: a ${s.strategy}${s.usedToPlay ? `, after a steady diet of ${s.usedToPlay}` : ''}.`, 'strategies', s.bot, 'amused'));
  }

  const ladder = brief.ladders.find((l) => l.category === 'Season') ?? brief.ladders[0];
  if (ladder?.entries.length) {
    const top = ladder.entries[0];
    lines.push(say('sarge', `On the ${ladder.category.toLowerCase()} ladder, ${top.name} sits on top${ladder.rankedBy === 'points' ? ` with ${n(top.points)} points` : ` at ${n(top.rating)}`}.`, 'ladder', ladder.category, 'serious'));
  }
  const climber = brief.movers.find((m) => m.to > m.from);
  if (climber) lines.push(say('zip', `Biggest climber: ${climber.name}, up ${climber.to - climber.from} rating points!`, 'movers', climber.name));

  if (brief.achievements.length) {
    const a = brief.achievements[0];
    lines.push(say('quill', a.firstEver
      ? `A first for the record books: ${a.bot} earned ${a.name}. Nobody had done that before.`
      : `${a.bot} earned ${a.name}${brief.achievements.length > 1 ? `, one of ${brief.achievements.length} achievements today` : ''}.`, 'achievements', a.bot, 'amused'));
  }

  if (brief.missingPersons.length) {
    const gone = brief.missingPersons.slice(0, 3).map((b) => b.name);
    lines.push(say('sarge', `Missing persons. ${list(gone)}. We haven't seen you in a while. Come back. The field misses you.`, 'missing', gone[0], 'serious'));
  }

  const leagues = brief.upcoming.leagueMatches;
  lines.push(say('zip', `Coming up: ${brief.upcoming.schedule}${brief.upcoming.nextLeagueDay ? ` Next league day is ${brief.upcoming.nextLeagueDay}.` : ''}${leagues.length ? ` ${list(leagues.map((l) => l.name))} ${leagues.length === 1 ? 'is' : 'are'} taking players now.` : ''}`, 'upcoming'));
  lines.push(say('quill', brief.mentoring, 'mentoring', '', 'calm'));
  lines.push(say('sarge', `That's the show. Sharpen your bots. We'll be watching.`, 'title', '', 'calm'));
  lines.push(say('zip', `Good night, and good brawling!`, 'title'));

  return {
    title: `${game} Tonight: ${brief.weekday} ${brief.date}`,
    summary: `${n(brief.totals.matches)} matches, ${brief.newBots.length} new bots${m ? ', and a match of the night' : ''}.`,
    lines,
    writer: 'template',
  };
}

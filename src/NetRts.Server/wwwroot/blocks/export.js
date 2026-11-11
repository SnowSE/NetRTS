// "Take it further in code": a strategy as a whole bot, in Python or C#, to download and change any way
// you like. The templates (export/) hold the bot; python.js and csharp.js write the rules into it.
import { downloadText } from '../util.js';
import { pyRule, pySwitches } from './python.js';
import { csRule, csSwitches } from './csharp.js';

const LANGS = {
  python: {
    template: 'bot.py.txt',
    file: (slug) => `${slug.replace(/-/g, '_')}.py`,
    // A Python function needs a statement: with every rule off (commented out), there isn't one.
    rules: (p) => [...p.rules.map(pyRule), ...(p.rules.some((r) => r.enabled !== false) ? [] : ['pass'])].join('\n\n'),
    switches: pySwitches,
    indent: '    ',
  },
  csharp: {
    template: 'Bot.cs.txt',
    file: (slug) => `${slug.split('-').map((w) => w[0].toUpperCase() + w.slice(1)).join('')}.cs`,
    rules: (p) => p.rules.map(csRule).join('\n\n'),
    switches: csSwitches,
    indent: '        ',
  },
};

const indent = (text, pad) => text.split('\n').map((l) => (l ? pad + l : l)).join('\n');

/** The bot file for a program: { name, text }. lang is python or csharp. */
export async function codeFor(program, lang) {
  const L = LANGS[lang];
  const res = await fetch(new URL(`./export/${L.template}`, import.meta.url));
  if (!res.ok) throw new Error(`Couldn't get the ${lang} template (${res.status}).`);
  const title = (program.name || 'My strategy').replace(/["\\{}\r\n]/g, '').trim() || 'My strategy';
  const slug = title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'my-strategy';
  const name = L.file(/^[a-z]/.test(slug) ? slug : `bot-${slug}`);
  const fill = {
    STRATEGY: title,
    FILE: name,
    SERVER: location.origin,
    SWITCHES: indent(L.switches(program), lang === 'csharp' ? '        ' : ''),
    RULES: indent(L.rules(program), L.indent),
  };
  const text = (await res.text()).replace(/\{\{(\w+)\}\}/g, (m, k) => fill[k] ?? m);
  return { name, text };
}

export async function downloadCode(program, lang) {
  const { name, text } = await codeFor(program, lang);
  downloadText(name, text);
}

// A program in a form to compare (and keep): the one the server sends back and the one the page built
// differ only in fields that hold their default. No imports, so a test can run it under node.

// What a field means when it's left out (BlockRuleDto): a rule is on, and runs every time.
const DEFAULTS = { enabled: true, once: false };

/**
 * For comparing a program here with the one the server runs: same key order, and no empty fields or ones
 * at their default (the server sends every field, "enabled": true, "once": false and all; a program here
 * leaves those out). A false that means something stays: a rule turned off, a switch turned off.
 */
export function canon(x) {
  if (Array.isArray(x)) return x.map(canon);
  if (x && typeof x === 'object') {
    const out = {};
    for (const k of Object.keys(x).sort()) {
      const v = x[k];
      if (v === null || v === undefined || (k in DEFAULTS && v === DEFAULTS[k])) continue;
      out[k] = canon(v);
    }
    return out;
  }
  return x;
}

export const sameProgram = (a, b) => JSON.stringify(canon(a)) === JSON.stringify(canon(b));

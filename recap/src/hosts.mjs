// The three hosts: NetRTS units behind the desk, in the spirit of a pod-race booth.

export const HOSTS = {
  zip: {
    name: 'Zip',
    unit: 'Scout',
    who: 'Zip the Scout: the fastest mouth on the field. Breathless play-by-play, piles up adjectives, starts sentences '
      + 'the others have to finish, gasps at everything, loves a nickname.',
    voice: { name: 'en-US-DavisNeural', rate: '+16%', pitch: '+4%', styles: { excited: 'excited', amused: 'cheerful', calm: 'chat', serious: 'friendly', shouting: 'shouting' } },
  },
  sarge: {
    name: 'Sarge',
    unit: 'Soldier',
    who: 'Sarge the Soldier: a grizzled old drill sergeant, veteran of a thousand brawls. Clipped and quick, barks '
      + 'orders at the bots, bone-dry deadpan humor, has seen it all and is impressed by almost none of it, secretly '
      + 'proud of every recruit. Respects a good economy and a stubborn defense. Gruff, never mean: he rides the bots, '
      + 'never the students. Keeps his lines punchy and flowing, not a string of tiny sentences.',
    // pauses: Azure's sentence and comma silences, trimmed so his clipped lines keep up with the others.
    voice: { name: 'en-US-GuyNeural', rate: '+10%', pitch: '-10%', pauses: { sentence: '120ms', comma: '60ms' }, styles: { excited: 'excited', amused: null, calm: null, serious: 'newscast', shouting: 'shouting' } },
  },
  quill: {
    name: 'Quill',
    unit: 'Archer',
    who: 'Quill the Archer: the sharp-eyed statistician, very precise, a little posh, dry wit. Brings the numbers, '
      + 'corrects Zip, and has a soft spot for underdogs.',
    voice: { name: 'en-GB-SoniaNeural', rate: '+4%', pitch: '+0%', styles: { excited: 'cheerful', amused: 'cheerful', calm: null, serious: null, shouting: 'cheerful' } },
  },
};

export const SPEAKERS = Object.keys(HOSTS);

/** What the big screen behind the desk shows during a line. */
export const GRAPHICS = [
  'title', 'totals', 'newBots', 'match', 'powerPlays', 'surprises', 'strategies', 'ladder', 'movers',
  'achievements', 'rivalries', 'missing', 'upcoming', 'mentoring', 'none',
];

export const MOODS = ['excited', 'calm', 'serious', 'amused', 'shouting'];

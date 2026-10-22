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
    who: 'Sarge the Soldier: a big, booming veteran of a thousand brawls. Speaks in grand, slow pronouncements, '
      + 'deadpan, unimpressed by flash, respects a good economy and a stubborn defense. Short sentences.',
    voice: { name: 'en-US-GuyNeural', rate: '-4%', pitch: '-10%', styles: { excited: 'excited', amused: 'cheerful', calm: 'newscast', serious: 'newscast', shouting: 'shouting' } },
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

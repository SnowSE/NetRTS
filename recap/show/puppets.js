// The hosts as SVG puppets: three badgers in their unit's kit. Each puppet exposes the parts the
// show animates (head, mouth, eyelids, pupils, body) and is drawn once; seek() only moves them.
'use strict';

window.Puppets = (() => {
  const NS = 'http://www.w3.org/2000/svg';

  // The badger: grey head, white blaze down the middle, black stripes over the eyes.
  const head = (fur, extra = '') => `
    <g class="ears">
      <ellipse cx="-46" cy="-50" rx="15" ry="12" fill="#2b2b2b"/><ellipse cx="46" cy="-50" rx="15" ry="12" fill="#2b2b2b"/>
      <ellipse cx="-46" cy="-50" rx="7" ry="6" fill="#f2f2f2"/><ellipse cx="46" cy="-50" rx="7" ry="6" fill="#f2f2f2"/>
    </g>
    <ellipse cx="0" cy="0" rx="62" ry="58" fill="${fur}"/>
    <path d="M -34 -52 Q -20 -10 -22 40 Q -12 46 0 46 Q 12 46 22 40 Q 20 -10 34 -52 Q 0 -66 -34 -52 Z" fill="#1d1d1d"/>
    <path d="M -10 -60 Q -6 -10 -12 44 Q 0 52 12 44 Q 6 -10 10 -60 Q 0 -62 -10 -60 Z" fill="#f4f4f4"/>
    <ellipse cx="0" cy="40" rx="30" ry="18" fill="#f4f4f4"/>
    <ellipse cx="0" cy="26" rx="10" ry="7" fill="#151515"/>
    <g class="eyes">
      <ellipse cx="-24" cy="2" rx="11" ry="12" fill="#fff"/><ellipse cx="24" cy="2" rx="11" ry="12" fill="#fff"/>
      <g class="pupils"><circle cx="-24" cy="3" r="6" fill="#111"/><circle cx="24" cy="3" r="6" fill="#111"/>
        <circle cx="-22" cy="0" r="2" fill="#fff"/><circle cx="26" cy="0" r="2" fill="#fff"/></g>
      <g class="lids"><rect x="-37" y="-11" width="26" height="0" fill="${fur}"/><rect x="11" y="-11" width="26" height="0" fill="${fur}"/></g>
    </g>
    <g class="brows"><path d="M -36 -14 Q -24 -20 -12 -14" stroke="#111" stroke-width="4" fill="none" stroke-linecap="round"/>
      <path d="M 12 -14 Q 24 -20 36 -14" stroke="#111" stroke-width="4" fill="none" stroke-linecap="round"/></g>
    <ellipse class="mouth" cx="0" cy="44" rx="10" ry="1.5" fill="#3a0d0d" stroke="#111" stroke-width="2"/>
    ${extra}`;

  const KITS = {
    // Zip the Scout: light and quick. Goggles up, a long red scarf.
    zip: {
      scale: 0.92,
      fur: '#9a9a9a',
      body: `
        <path d="M -70 140 Q -66 40 0 30 Q 66 40 70 140 Z" fill="#2d6fa3"/>
        <path d="M -40 34 Q 0 58 40 34 L 46 52 Q 0 76 -46 52 Z" fill="#d23b2f"/>
        <path d="M 30 50 Q 58 86 44 128 L 30 126 Q 40 92 20 60 Z" fill="#b8302a"/>
        <circle cx="-26" cy="96" r="5" fill="#f2c14e"/><circle cx="-26" cy="114" r="5" fill="#f2c14e"/>`,
      hat: `<g transform="translate(0,-40)"><rect x="-50" y="-8" width="100" height="10" rx="5" fill="#5a3d1e"/>
        <circle cx="-20" cy="-3" r="14" fill="#9fd3ef" stroke="#5a3d1e" stroke-width="5"/>
        <circle cx="20" cy="-3" r="14" fill="#9fd3ef" stroke="#5a3d1e" stroke-width="5"/></g>`,
    },
    // Sarge the Soldier: big. Steel helmet, armoured shoulders, shield at the side.
    sarge: {
      scale: 1.12,
      fur: '#7d7d7d',
      body: `
        <path d="M -84 140 Q -80 36 0 26 Q 80 36 84 140 Z" fill="#4c5a3a"/>
        <ellipse cx="-62" cy="46" rx="30" ry="18" fill="#8a949c"/><ellipse cx="62" cy="46" rx="30" ry="18" fill="#8a949c"/>
        <rect x="-14" y="60" width="28" height="60" rx="4" fill="#3a4630"/>
        <g transform="translate(-78,70) rotate(-10) scale(0.75)"><path d="M -26 -40 L 26 -40 L 26 6 Q 26 36 0 48 Q -26 36 -26 6 Z" fill="#8b1e1e" stroke="#c9ccce" stroke-width="5"/>
          <path d="M 0 -30 L 0 38 M -18 -6 L 18 -6" stroke="#f2c14e" stroke-width="6"/></g>`,
      hat: `<g transform="translate(0,-34)"><path d="M -70 6 Q -66 -46 0 -48 Q 66 -46 70 6 Z" fill="#5b6650"/>
        <rect x="-76" y="0" width="152" height="12" rx="6" fill="#48523e"/></g>`,
    },
    // Quill the Archer: hood, round spectacles, a quiver over the shoulder.
    quill: {
      scale: 0.98,
      fur: '#a3a3a3',
      body: `
        <g transform="translate(-52,40) rotate(-24)"><rect x="-10" y="-40" width="22" height="90" rx="6" fill="#6b4423"/>
          <path d="M -6 -40 L -12 -62 L 0 -50 Z M 4 -40 L 2 -64 L 12 -50 Z" fill="#e8e1cf"/></g>
        <path d="M -72 140 Q -68 38 0 28 Q 68 38 72 140 Z" fill="#2f6b3f"/>
        <path d="M -54 40 L 54 120" stroke="#6b4423" stroke-width="8"/>
        <path d="M -20 40 Q 0 56 20 40 L 16 64 Q 0 72 -16 64 Z" fill="#245532"/>`,
      hat: `<path d="M -74 30 Q -80 -60 0 -78 Q 80 -60 74 30 Q 66 -40 0 -54 Q -66 -40 -74 30 Z" fill="#2f6b3f"/>`,
      glasses: `<g class="specs"><circle cx="-24" cy="2" r="15" fill="none" stroke="#c9a227" stroke-width="3"/>
        <circle cx="24" cy="2" r="15" fill="none" stroke="#c9a227" stroke-width="3"/><path d="M -9 2 L 9 2" stroke="#c9a227" stroke-width="3"/></g>`,
    },
  };

  /** Builds a puppet into the container element; returns its animatable parts. */
  function create(key, container) {
    const kit = KITS[key];
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('width', '220');
    svg.setAttribute('height', '300');
    svg.setAttribute('viewBox', '-110 -150 220 300');
    svg.innerHTML = `
      <g class="body">${kit.body}</g>
      <g class="head" transform="translate(0,-36)">${head(kit.fur, (kit.glasses ?? '') + kit.hat)}</g>`;
    container.appendChild(svg);
    return {
      key,
      scale: kit.scale,
      svg,
      body: svg.querySelector('.body'),
      head: svg.querySelector('.head'),
      mouth: svg.querySelector('.mouth'),
      pupils: svg.querySelector('.pupils'),
      lids: [...svg.querySelectorAll('.lids rect')],
      brows: svg.querySelector('.brows'),
    };
  }

  // Mouth shapes from Azure viseme ids (0 = silence, 21 = p/b/m, 18 = f/v, ...).
  const SHAPES = {
    closed: [10, 1.5], narrow: [11, 4], mid: [12, 8], open: [14, 14], round: [8, 10],
  };
  function shapeOf(id) {
    if (id === 0 || id === 21) return 'closed';
    if (id === 18 || id === 19 || id === 15) return 'narrow';
    if ([1, 2, 9, 11, 4].includes(id)) return 'open';
    if ([3, 7, 8, 10].includes(id)) return 'round';
    return 'mid';
  }

  /** Poses a puppet for time t. talk: null, or { local seconds into its line, visemes, mood }. look: -1..1 toward the speaker. */
  function pose(p, t, talk, look, seed) {
    let shape = 'closed';
    if (talk) {
      let id = 0;
      for (const v of talk.visemes) { if (v.t <= talk.local) id = v.id; else break; }
      shape = shapeOf(id);
    }
    const [rx, ry] = SHAPES[shape];
    p.mouth.setAttribute('rx', rx);
    p.mouth.setAttribute('ry', ry);
    p.mouth.setAttribute('cy', 44 + ry * 0.3);

    // Blink every few seconds, each host on its own rhythm.
    const cycle = 3.1 + seed * 0.9;
    const phase = ((t + seed * 1.7) % cycle) / cycle;
    const shut = phase > 0.96 ? 25 : 0;
    p.lids.forEach((l) => l.setAttribute('height', shut));
    p.pupils.setAttribute('transform', `translate(${(look * 4).toFixed(2)},0)`);

    const lively = talk ? (talk.mood === 'excited' || talk.mood === 'shouting' ? 1.8 : talk.mood === 'amused' ? 1.2 : 0.8) : 0.25;
    const tilt = Math.sin(t * (talk ? 7.3 : 1.3) + seed * 4) * 2.4 * lively;
    const bob = Math.abs(Math.sin(t * (talk ? 9.1 : 1.1) + seed)) * 3 * lively;
    p.head.setAttribute('transform', `translate(0,${(-36 - bob).toFixed(2)}) rotate(${tilt.toFixed(2)})`);
    p.brows.setAttribute('transform', `translate(0,${talk && lively > 1 ? -3 : 0})`);
    p.body.setAttribute('transform', `translate(0,${(Math.sin(t * 1.4 + seed) * 1.2).toFixed(2)})`);
  }

  return { create, pose };
})();

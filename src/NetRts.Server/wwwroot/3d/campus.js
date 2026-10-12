// 3D match view: the Snow College theme's HQs and housing, one model per player slot, each built to
// look like the real building in lore.js (HQ_NAME, HOUSING_NAME). Modelled from photos of the campus
// and of Ephraim's apartments; where no photo turned up, the model follows what's known about it.
//
// Flat roofs are drawn in the team's colour so teams stay easy to tell apart from above; buildings
// with pitched roofs keep their real roof colour and fly a team flag instead.
import * as THREE from 'three';
import { col, K, Builder, box, cyl, ball, cone, dome, prism, foundation, flag } from './kit.js';

const C = {
  brick: col('#a8573f'), brickRed: col('#9e4a35'), brickOrange: col('#b5603f'), brickDark: col('#6b3a2c'),
  brickBuff: col('#c8b48c'), brickPink: col('#b5786a'), limestone: col('#cfc6b5'), concrete: col('#bdb8ad'),
  fascia: col('#cfc7b8'), whiteTrim: col('#eeeee8'), charcoal: col('#2e3236'), shingle: col('#5a4636'),
  shingleGrey: col('#6a625a'), shingleBrown: col('#6e6358'), stucco: col('#c9b49a'), stuccoLight: col('#cdbba3'),
  siding: col('#e8e2d2'), sidingGrey: col('#d4d6d4'), metalGrey: col('#8e959b'), woodStrip: col('#7a4e33'),
  stone: col('#4a4a4a'), glass: col('#4a6070'), glassTeal: col('#3e8c9a'), glassNavy: col('#2e3f5c'),
  unlit: col('#36414c'), lit: col('#ffcf7a'), orangePanel: col('#d9502a'), mint: col('#9fd3b0'),
  maroon: col('#7a2a2a'), railGreen: col('#3f6b45'), lawn: col('#5f8a3c'), roofMembrane: col('#c4c4c4'),
};

const Y0 = 0.085;   // top of the foundation slab

/** A box standing on y0 (default: the foundation), centred at (x, z). */
function block(b, colour, x, z, w, h, d, opts = {}) {
  const y0 = opts.y0 ?? Y0;
  b.add(box(w, h, d), colour, { ...opts, at: [x, y0 + h / 2, z] });
  return { x, z, w, h, d, y0, top: y0 + h };
}

/** A flat roof: a team-coloured membrane with a parapet or fascia band in `trim`. */
function flatRoof(b, blk, trim = C.whiteTrim, { overhang = 0.015, band = 0.03 } = {}) {
  b.add(box(blk.w + overhang * 2, band, blk.d + overhang * 2), trim, { at: [blk.x, blk.top + band / 2 - 0.004, blk.z] })
    .add(box(blk.w - 0.02, 0.012, blk.d - 0.02), C.roofMembrane, { team: 1, at: [blk.x, blk.top + band - 0.002, blk.z] });
}

/** A hipped roof over a block: a four-sided cone stretched to the block, with eaves. */
function hipRoof(b, blk, colour, h, eave = 0.04, team = 0) {
  const r = Math.SQRT1_2;
  b.add(cone(r, h, 4), colour, {
    team, at: [blk.x, blk.top + h / 2, blk.z], rot: [0, Math.PI / 4, 0],
    scale: [(blk.w + eave * 2) / 1, 1, (blk.d + eave * 2) / 1],
  });
}

/** A gable roof along x (ridge parallel to the block's width). */
function gableRoof(b, blk, colour, h, eave = 0.03, team = 0) {
  b.add(prism(blk.d + eave * 2, h, blk.w + eave * 2), colour, { team, at: [blk.x, blk.top + h / 2, blk.z], rot: [0, Math.PI / 2, 0] });
}

/**
 * Windows on a block's faces ('f' front +z, 'b' back, 'l' left -x, 'r' right +x): `rows` storeys from
 * y0 every dy, `cols` across. Some are lit; `spandrel` adds a panel under each.
 */
function windowsOn(b, blk, faces, rows, cols, { y0 = 0.07, dy = 0.15, ww = 0.05, wh = 0.07, seed = 1, spandrel = null, litShare = 0.6, colsSide = null } = {}) {
  const F = { f: [0, 1, 0], b: [0, -1, Math.PI], r: [1, 0, Math.PI / 2], l: [-1, 0, -Math.PI / 2] };
  for (const face of faces) {
    const [nx, nz, ry] = F[face];
    const across = nx ? blk.d : blk.w;
    const n = nx ? (colsSide ?? Math.max(1, Math.round(cols * blk.d / blk.w))) : cols;
    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < n; c++) {
        const off = (c - (n - 1) / 2) * (across / n);
        const px = blk.x + (nx ? nx * (blk.w / 2 + 0.005) : off);
        const pz = blk.z + (nz ? nz * (blk.d / 2 + 0.005) : off);
        const y = blk.y0 + y0 + r * dy;
        const h = ((r * 7 + c * 13 + face.charCodeAt(0) * 5 + seed * 3) % 10) / 10;
        const lit = h < litShare;
        b.add(box(ww, wh, 0.01), lit ? C.lit : C.unlit, { glow: lit ? 0.3 : 0, at: [px, y, pz], rot: [0, ry, 0] });
        if (spandrel) b.add(box(ww * 1.05, wh * 0.6, 0.008), spandrel, { at: [px, y - wh * 0.85, pz], rot: [0, ry, 0] });
      }
    }
  }
}

/** A continuous band of glass on a face (ribbon windows, storefronts, curtain walls). */
function glassBand(b, blk, face, y, h, colour = C.glass, glow = 0.25, inset = 0.9) {
  const along = face === 'f' || face === 'b';
  const w = (along ? blk.w : blk.d) * inset;
  const s = face === 'f' || face === 'r' ? 1 : -1;
  const at = along ? [blk.x, blk.y0 + y, blk.z + s * (blk.d / 2 + 0.004)] : [blk.x + s * (blk.w / 2 + 0.004), blk.y0 + y, blk.z];
  b.add(box(along ? w : 0.01, h, along ? 0.01 : w), colour, { glow, at });
}

function door(b, x, z, colour = K.woodDark, ry = 0, w = 0.06, h = 0.1, y0 = Y0) {
  b.add(box(w, h, 0.012), colour, { at: [x, y0 + h / 2, z], rot: [0, ry, 0] });
}

function lawn(b, w = 0.9, d = 0.9) {
  b.add(box(w, 0.006, d), C.lawn, { at: [0, Y0 + 0.004, 0] });
}

/** A team flag on a white pole, for buildings without a flat roof to colour. */
function teamFlag(b, x, z, h = 0.34) { flag(b, x, Y0, z, h); }

// ---------------------------------------------------------------- housing

/** Suites at Academy Square: brick wings with charcoal hipped caps around a four-storey glass atrium. */
function suites() {
  const b = new Builder();
  foundation(b);
  b.add(box(0.8, 0.07, 0.2), C.limestone, { at: [0, Y0 + 0.035, 0.32] });                  // stone podium
  const back = block(b, C.brickRed, 0, -0.27, 0.86, 0.58, 0.24);
  const left = block(b, C.brickRed, -0.3, 0.02, 0.26, 0.58, 0.4);
  const right = block(b, C.brickRed, 0.3, 0.02, 0.26, 0.58, 0.4);
  for (const blk of [back, left, right]) {
    for (let i = 1; i < 4; i++) b.add(box(blk.w + 0.004, 0.012, blk.d + 0.004), col('#7d3a2a'), { at: [blk.x, Y0 + i * 0.145, blk.z] });
  }
  windowsOn(b, left, 'flr', 4, 3, { y0: 0.08, dy: 0.135, seed: 2 });
  windowsOn(b, right, 'frl', 4, 3, { y0: 0.08, dy: 0.135, seed: 5 });
  windowsOn(b, back, 'b', 4, 8, { y0: 0.08, dy: 0.135, seed: 7 });
  hipRoof(b, back, C.charcoal, 0.09, 0.04, 0);
  hipRoof(b, left, C.charcoal, 0.09, 0.04, 0);
  hipRoof(b, right, C.charcoal, 0.09, 0.04, 0);
  // The glass atrium between the wings, with its mullion grid and a team-coloured canopy.
  const atrium = block(b, C.glass, 0, -0.02, 0.34, 0.6, 0.3, { glow: 0.3 });
  for (let i = -2; i <= 2; i++) b.add(box(0.008, 0.6, 0.008), C.charcoal, { at: [i * 0.068, Y0 + 0.3, 0.135] });
  for (let i = 1; i < 5; i++) b.add(box(0.34, 0.008, 0.008), C.charcoal, { at: [0, Y0 + i * 0.12, 0.135] });
  b.add(box(0.36, 0.015, 0.32), C.charcoal, { at: [0, atrium.top + 0.008, atrium.z] })
    .add(box(0.3, 0.02, 0.1), K.team, { team: 1, at: [0, Y0 + 0.15, 0.19] });
  // The brick tower over the entrance, and stone lantern piers on the plaza.
  block(b, col('#8c3f2d'), 0.13, 0.15, 0.08, 0.78, 0.08);
  b.add(box(0.1, 0.03, 0.1), C.limestone, { at: [0.13, Y0 + 0.79, 0.15] });
  for (const x of [-0.2, 0.2]) {
    b.add(box(0.05, 0.16, 0.05), C.limestone, { at: [x, Y0 + 0.08 + 0.07, 0.4] })
      .add(box(0.04, 0.04, 0.04), K.team, { team: 1, glow: 0.8, at: [x, Y0 + 0.25, 0.4] });
  }
  return b.build();
}

/** Anderson Hall: two flat-roofed brick wings linked by a recessed glass entry, a mansard-hooded office in front. */
function anderson() {
  const b = new Builder();
  foundation(b);
  const L = block(b, C.brick, -0.24, -0.04, 0.36, 0.3, 0.62);
  const R = block(b, C.brick, 0.24, -0.04, 0.36, 0.3, 0.62);
  for (const w of [L, R]) {
    flatRoof(b, w, C.fascia, { band: 0.045 });
    windowsOn(b, w, w === L ? 'fbl' : 'fbr', 2, 3, { y0: 0.09, dy: 0.13, ww: 0.035, wh: 0.08, spandrel: C.concrete, seed: w === L ? 1 : 4, colsSide: 5 });
  }
  const link = block(b, C.glass, 0, -0.1, 0.13, 0.26, 0.24, { glow: 0.3 });
  flatRoof(b, link, C.fascia);
  // The one-storey entry pavilion with its brown mansard hood.
  const pav = block(b, C.brick, 0, 0.25, 0.2, 0.12, 0.16);
  b.add(cyl(0.09, 0.15, 0.06, 4), C.shingle, { at: [0, pav.top + 0.03, 0.25], rot: [0, Math.PI / 4, 0], scale: [1, 1, 0.8] })
    .add(box(0.15, 0.012, 0.11), K.team, { team: 1, at: [0, pav.top + 0.062, 0.25] });
  door(b, 0, 0.332, C.whiteTrim, 0, 0.05, 0.09);
  b.add(box(0.14, 0.018, 0.005), C.whiteTrim, { at: [0, Y0 + 0.11, 0.333] });
  return b.build();
}

/** Mary Nielson Hall: a long, low brick bar with ribbon windows and a teal glass entry box at one end. */
function maryNielson() {
  const b = new Builder();
  foundation(b);
  const bar = block(b, col('#b0624a'), -0.06, -0.05, 0.72, 0.3, 0.34);
  flatRoof(b, bar, col('#e6e6e0'));
  for (const y of [0.07, 0.2]) {
    b.add(box(0.73, 0.012, 0.35), col('#d6b8a3'), { at: [bar.x, Y0 + y - 0.045, bar.z] });
    glassBand(b, bar, 'f', y, 0.05, C.unlit, 0, 0.92);
    glassBand(b, bar, 'b', y, 0.05, C.unlit, 0, 0.92);
  }
  windowsOn(b, bar, 'fb', 2, 10, { y0: 0.07, dy: 0.13, ww: 0.045, wh: 0.045, litShare: 0.55, seed: 3 });
  const entry = block(b, C.glassTeal, 0.36, 0.0, 0.14, 0.3, 0.3, { glow: 0.35 });
  flatRoof(b, entry, col('#e6e6e0'));
  b.add(box(0.16, 0.035, 0.12), C.concrete, { at: [0.36, Y0 + 0.017, 0.2] });
  for (const x of [0.3, 0.42]) b.add(box(0.006, 0.05, 0.11), K.steelDark, { at: [x, Y0 + 0.06, 0.2] });
  return b.build();
}

/** Snow Hall: the darkest brick of the halls, a one-storey wing in front, an orange band over the entry. */
function snowHall() {
  const b = new Builder();
  foundation(b);
  const main = block(b, C.brickDark, 0, -0.14, 0.7, 0.46, 0.46);
  flatRoof(b, main, C.whiteTrim);
  windowsOn(b, main, 'fblr', 3, 6, { y0: 0.1, dy: 0.13, seed: 6 });
  const wing = block(b, C.brickDark, -0.1, 0.24, 0.6, 0.15, 0.28);
  flatRoof(b, wing, C.whiteTrim);
  windowsOn(b, wing, 'f', 1, 5, { y0: 0.075, seed: 2 });
  // The entry: buff brick screen, glass doors and the orange-red panel band.
  block(b, C.brickBuff, 0.28, 0.24, 0.06, 0.2, 0.2);
  const ent = block(b, C.glass, 0.37, 0.2, 0.12, 0.13, 0.2, { glow: 0.35 });
  b.add(box(0.14, 0.05, 0.22), C.orangePanel, { at: [0.37, ent.top + 0.025, 0.2] })
    .add(box(0.15, 0.012, 0.23), K.team, { team: 1, at: [0.37, ent.top + 0.056, 0.2] });
  return b.build();
}

/** Nuttall Hall: a long brick bar with recessed panel bays and a navy glass stair box on the corner. */
function nuttall() {
  const b = new Builder();
  foundation(b);
  const bar = block(b, C.brickOrange, -0.05, -0.1, 0.76, 0.32, 0.4);
  flatRoof(b, bar, C.whiteTrim, { band: 0.04 });
  for (let i = -3; i <= 3; i++) {
    b.add(box(0.07, 0.26, 0.01), col('#9c4f33'), { at: [bar.x + i * 0.1, Y0 + 0.16, 0.103] });
  }
  windowsOn(b, bar, 'fb', 2, 7, { y0: 0.09, dy: 0.13, ww: 0.045, wh: 0.075, seed: 8 });
  windowsOn(b, bar, 'lr', 2, 3, { y0: 0.09, dy: 0.13, seed: 1 });
  const stair = block(b, C.glassNavy, 0.34, 0.16, 0.16, 0.36, 0.16, { glow: 0.3 });
  flatRoof(b, stair, C.whiteTrim);
  for (let i = 1; i < 4; i++) b.add(box(0.165, 0.008, 0.165), C.whiteTrim, { at: [0.34, Y0 + i * 0.09, 0.16] });
  // Wide concrete steps with green railings.
  for (let i = 0; i < 3; i++) b.add(box(0.2, 0.02, 0.05), C.concrete, { at: [0.12, Y0 + 0.01 + i * 0.02, 0.32 - i * 0.04] });
  for (const x of [0.02, 0.22]) b.add(box(0.008, 0.05, 0.14), C.railGreen, { at: [x, Y0 + 0.07, 0.28] });
  b.add(box(0.2, 0.02, 0.06), K.team, { team: 1, at: [0.12, Y0 + 0.16, 0.13] });
  return b.build();
}

/** Castilleja Hall: stepped flat-roofed brick blocks under deep white soffits, a recessed glass entry. */
function castilleja() {
  const b = new Builder();
  foundation(b);
  const a = block(b, col('#b8643f'), 0.04, -0.18, 0.8, 0.32, 0.34);
  const c = block(b, col('#b8643f'), -0.24, 0.18, 0.32, 0.26, 0.4);
  for (const blk of [a, c]) {
    b.add(box(blk.w + 0.06, 0.035, blk.d + 0.06), C.whiteTrim, { at: [blk.x, blk.top + 0.017, blk.z] })
      .add(box(blk.w, 0.012, blk.d), C.roofMembrane, { team: 1, at: [blk.x, blk.top + 0.04, blk.z] });
    // Stack-bond texture: a grid of pale joints.
    for (let i = -3; i <= 3; i++) b.add(box(0.006, blk.h - 0.02, 0.006), col('#d39a7a'), { at: [blk.x + i * blk.w / 7, blk.y0 + blk.h / 2, blk.z + blk.d / 2 + 0.003] });
  }
  b.add(box(0.8, 0.06, 0.012), C.concrete, { at: [a.x, Y0 + 0.03, a.z + a.d / 2 + 0.005] });
  windowsOn(b, a, 'fb', 2, 7, { y0: 0.1, dy: 0.13, seed: 4 });
  windowsOn(b, c, 'fl', 2, 3, { y0: 0.09, dy: 0.12, seed: 9 });
  // The recessed entry in the corner, with a clerestory above.
  const e = block(b, C.glass, 0.2, 0.06, 0.2, 0.18, 0.12, { glow: 0.35 });
  b.add(box(0.2, 0.06, 0.01), C.glass, { glow: 0.3, at: [0.2, e.top + 0.06, 0.0] })
    .add(box(0.26, 0.02, 0.18), C.whiteTrim, { at: [0.2, e.top + 0.01, 0.06] });
  return b.build();
}

/** Summit Valley Apartments: low brick rows of family units, each with its own outside door. */
function summitValley() {
  const b = new Builder();
  foundation(b);
  lawn(b, 0.86, 0.2);
  for (const z of [-0.24, 0.16]) {
    const row = block(b, C.brick, 0, z, 0.82, 0.17, 0.24);
    gableRoof(b, row, C.shingleGrey, 0.09, 0.03);
    for (let i = 0; i < 6; i++) {
      const x = -0.34 + i * 0.136;
      door(b, x - 0.03, z + 0.126, K.team, 0, 0.035, 0.09);
      b.add(box(0.04, 0.045, 0.01), C.lit, { glow: 0.4, at: [x + 0.025, Y0 + 0.1, z + 0.126] })
        .add(box(0.05, 0.015, 0.04), C.concrete, { at: [x - 0.03, Y0 + 0.007, z + 0.15] });
    }
  }
  teamFlag(b, 0.4, 0.38);
  return b.build();
}

/** Badger Studios: a small single-storey block of studio apartments. */
function badgerStudios() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  const s = block(b, C.brick, 0, -0.08, 0.5, 0.17, 0.34);
  flatRoof(b, s, C.fascia, { overhang: 0.03 });
  for (const x of [-0.16, 0, 0.16]) {
    door(b, x - 0.02, 0.092, K.woodDark, 0, 0.035, 0.09);
    b.add(box(0.035, 0.04, 0.01), C.lit, { glow: 0.45, at: [x + 0.03, Y0 + 0.1, 0.092] });
  }
  b.add(box(0.5, 0.015, 0.08), K.team, { team: 1, at: [0, Y0 + 0.14, 0.13], rot: [0.25, 0, 0] });
  // A picnic table on the lawn.
  b.add(box(0.12, 0.012, 0.06), K.wood, { at: [0.22, Y0 + 0.05, 0.3] })
    .add(box(0.12, 0.008, 0.02), K.wood, { at: [0.22, Y0 + 0.03, 0.26] })
    .add(box(0.12, 0.008, 0.02), K.wood, { at: [0.22, Y0 + 0.03, 0.34] });
  return b.build();
}

/** Nordic Pointe: four storeys of grey metal, white panels and wood strips over a glass storefront. */
function nordicPointe() {
  const b = new Builder();
  foundation(b);
  const bar = block(b, C.metalGrey, 0, -0.06, 0.82, 0.62, 0.42);
  glassBand(b, bar, 'f', 0.07, 0.12, C.glass, 0.4, 0.96);
  b.add(box(0.86, 0.025, 0.08), C.whiteTrim, { at: [0, Y0 + 0.15, bar.z + bar.d / 2 + 0.035] });
  // Cladding bands: white panels and wood-tone strips, with inset balconies.
  for (let f = 0; f < 3; f++) {
    const y = Y0 + 0.2 + f * 0.14;
    b.add(box(0.3, 0.13, 0.008), C.whiteTrim, { at: [-0.22, y + 0.05, 0.155] })
      .add(box(0.05, 0.13, 0.008), C.woodStrip, { at: [0.05, y + 0.05, 0.155] })
      .add(box(0.12, 0.012, 0.03), K.team, { team: 1, at: [0.24, y, 0.165] })
      .add(box(0.12, 0.04, 0.004), K.steelDark, { at: [0.24, y + 0.03, 0.18] });
  }
  windowsOn(b, bar, 'fb', 3, 6, { y0: 0.27, dy: 0.14, ww: 0.06, wh: 0.06, seed: 5 });
  windowsOn(b, bar, 'lr', 3, 3, { y0: 0.27, dy: 0.14, seed: 2 });
  block(b, C.stone, -0.39, -0.06, 0.06, 0.2, 0.44);
  // Corner tower caps over a stepped parapet.
  b.add(box(0.84, 0.03, 0.44), C.whiteTrim, { at: [0, bar.top + 0.015, bar.z] })
    .add(box(0.8, 0.012, 0.4), C.roofMembrane, { team: 1, at: [0, bar.top + 0.032, bar.z] });
  for (const x of [-0.36, 0.36]) b.add(box(0.12, 0.08, 0.44), C.metalGrey, { at: [x, bar.top + 0.04, bar.z] });
  return b.build();
}

/** Snow Garden: three storeys of tan stucco, balconies stacked on tall round columns. */
function snowGarden() {
  const b = new Builder();
  foundation(b);
  const s = block(b, C.stucco, 0, -0.1, 0.82, 0.42, 0.36);
  flatRoof(b, s, col('#6b5340'), { overhang: 0.025 });
  windowsOn(b, s, 'fblr', 3, 6, { y0: 0.08, dy: 0.13, seed: 3 });
  for (let i = 0; i < 4; i++) {
    const x = -0.3 + i * 0.2;
    b.add(cyl(0.018, 0.018, 0.42, 8), C.stuccoLight, { at: [x, Y0 + 0.21, 0.16] });
    for (let f = 1; f < 3; f++) {
      b.add(box(0.16, 0.014, 0.1), C.stuccoLight, { at: [x + 0.1, Y0 + f * 0.135, 0.12] })
        .add(box(0.16, 0.04, 0.005), col('#3a3330'), { at: [x + 0.1, Y0 + f * 0.135 + 0.025, 0.17] });
    }
  }
  b.add(box(0.8, 0.015, 0.12), K.team, { team: 1, at: [0, s.top - 0.01, 0.13] });
  // The concrete pergola.
  for (const x of [0.28, 0.42]) b.add(box(0.025, 0.14, 0.025), C.concrete, { at: [x, Y0 + 0.07, 0.36] });
  for (let i = 0; i < 4; i++) b.add(box(0.18, 0.012, 0.012), C.concrete, { at: [0.35, Y0 + 0.145, 0.32 + i * 0.03] });
  return b.build();
}

/** Pinetree Condominiums: long parallel stucco bars under low gables, steel stairs at the ends. */
function pinetree() {
  const b = new Builder();
  foundation(b);
  for (const z of [-0.23, 0.17]) {
    const bar = block(b, C.stuccoLight, 0, z, 0.84, 0.27, 0.22);
    gableRoof(b, bar, C.shingleBrown, 0.07, 0.025);
    for (let i = -3; i <= 3; i += 2) b.add(box(0.1, 0.24, 0.04), C.stucco, { at: [i * 0.1, Y0 + 0.12, z + 0.125] });
    windowsOn(b, bar, 'fb', 2, 8, { y0: 0.07, dy: 0.12, seed: z > 0 ? 2 : 6 });
    // External steel stairs at the end, and AC boxes on the roof.
    b.add(box(0.05, 0.012, 0.2), K.steelDark, { at: [0.46, Y0 + 0.12, z], rot: [0.5, 0, 0] })
      .add(box(0.05, 0.02, 0.05), K.steel, { at: [-0.2, bar.top + 0.07, z] })
      .add(box(0.05, 0.02, 0.05), K.steel, { at: [0.2, bar.top + 0.07, z] });
    door(b, -0.42, z + 0.05, K.team, -Math.PI / 2, 0.04, 0.09);
  }
  b.add(box(0.008, 0.03, 0.1), K.team, { team: 1, at: [-0.423, Y0 + 0.31, 0.17] });
  teamFlag(b, 0.42, 0.42, 0.3);
  return b.build();
}

/** Acorn & Oaktree: two three-storey buff brick buildings with projecting stair towers. */
function acornOaktree() {
  const b = new Builder();
  foundation(b);
  for (const x of [-0.22, 0.22]) {
    const bar = block(b, C.brickBuff, x, -0.03, 0.34, 0.42, 0.74);
    flatRoof(b, bar, col('#b5a17a'));
    windowsOn(b, bar, x < 0 ? 'fbl' : 'fbr', 3, 2, { y0: 0.08, dy: 0.13, seed: x < 0 ? 1 : 7, colsSide: 5 });
    const tower = block(b, col('#b9a37b'), x + (x < 0 ? 0.19 : -0.19), 0.0, 0.08, 0.46, 0.16);
    b.add(box(0.09, 0.015, 0.17), C.roofMembrane, { team: 1, at: [tower.x, tower.top + 0.008, tower.z] });
    for (let f = 0; f < 3; f++) door(b, x, bar.z + bar.d / 2 + 0.006, C.maroon, 0, 0.05, 0.09, Y0 + f * 0.13);
  }
  return b.build();
}

/** Badger House: a single-storey red brick ranch house with a hipped roof, chimney and carport. */
function badgerHouse() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  const h = block(b, C.brickRed, -0.08, -0.08, 0.56, 0.16, 0.36);
  hipRoof(b, h, col('#4e4844'), 0.11, 0.05);
  block(b, C.brickRed, -0.26, -0.18, 0.06, 0.32, 0.06);
  door(b, -0.06, 0.106, C.whiteTrim, 0, 0.05, 0.1);
  for (const x of [-0.26, 0.1]) b.add(box(0.09, 0.06, 0.01), C.lit, { glow: 0.45, at: [x, Y0 + 0.1, 0.106] });
  // The flat-roofed carport.
  for (const [x, z] of [[0.26, -0.24], [0.42, -0.24], [0.26, 0.12], [0.42, 0.12]]) b.add(box(0.015, 0.15, 0.015), C.whiteTrim, { at: [x, Y0 + 0.075, z] });
  b.add(box(0.2, 0.015, 0.4), col('#4e4844'), { at: [0.34, Y0 + 0.155, -0.06] });
  teamFlag(b, 0.3, 0.35);
  b.add(box(0.03, 0.03, 0.02), K.team, { team: 1, at: [0.12, Y0 + 0.07, 0.38] })   // mailbox
    .add(box(0.006, 0.05, 0.006), K.woodDark, { at: [0.12, Y0 + 0.025, 0.38] });
  return b.build();
}

/** Badger Loft: a single-storey house in grey-white siding with twin front gables. */
function badgerLoft() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  const h = block(b, C.sidingGrey, 0, -0.06, 0.6, 0.17, 0.38);
  gableRoof(b, h, col('#5d6266'), 0.12, 0.03);
  for (const x of [-0.16, 0.16]) {
    const g = block(b, C.sidingGrey, x, 0.15, 0.2, 0.17, 0.1);
    b.add(prism(0.24, 0.1, 0.14), col('#5d6266'), { at: [x, g.top + 0.05, 0.16] })
      .add(box(0.08, 0.07, 0.01), C.lit, { glow: 0.45, at: [x, Y0 + 0.1, 0.206] });
  }
  door(b, 0, 0.13, K.team, 0, 0.05, 0.1);
  for (let i = 1; i < 6; i++) b.add(box(0.604, 0.004, 0.384), col('#bfc2c0'), { at: [0, Y0 + i * 0.028, -0.06] });
  teamFlag(b, 0.38, 0.38);
  return b.build();
}

/** The Up House: a pink brick Victorian cottage with steep cross gables, mint shingles and a gazebo. */
function upHouse() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  const h = block(b, C.brickPink, -0.1, -0.1, 0.4, 0.22, 0.38);
  b.add(prism(0.44, 0.24, 0.42), C.shingle, { at: [h.x, h.top + 0.12, h.z] })                       // ridge front-to-back
    .add(prism(0.42, 0.24, 0.44), C.shingle, { at: [h.x, h.top + 0.12, h.z], rot: [0, Math.PI / 2, 0] })
    .add(prism(0.34, 0.18, 0.01), C.mint, { at: [h.x, h.top + 0.09, h.z + 0.2] })                    // fish-scale gable
    .add(box(0.16, 0.12, 0.01), C.lit, { glow: 0.45, at: [h.x - 0.06, Y0 + 0.11, h.z + 0.195] })      // the big front window
    .add(cyl(0.08, 0.08, 0.01, 12, 1), C.lit, { glow: 0.45, at: [h.x - 0.06, Y0 + 0.17, h.z + 0.195], rot: [Math.PI / 2, 0, 0], scale: [1, 1, 0.5] });
  // The arched porch on white columns.
  b.add(box(0.16, 0.02, 0.1), C.whiteTrim, { at: [h.x + 0.1, Y0 + 0.01, h.z + 0.24] })
    .add(box(0.17, 0.02, 0.11), C.mint, { at: [h.x + 0.1, Y0 + 0.16, h.z + 0.24] });
  for (const x of [0.03, 0.17]) b.add(cyl(0.008, 0.008, 0.15, 6), C.whiteTrim, { at: [h.x + x, Y0 + 0.08, h.z + 0.28] });
  door(b, h.x + 0.1, h.z + 0.192, K.team, 0, 0.045, 0.1);
  // The white gazebo and a green outbuilding.
  b.add(cyl(0.1, 0.1, 0.02, 8), C.whiteTrim, { at: [0.3, Y0 + 0.01, 0.28] })
    .add(cone(0.12, 0.08, 8), C.whiteTrim, { at: [0.3, Y0 + 0.17, 0.28] })
    .add(ball(0.012, 0), K.team, { team: 1, at: [0.3, Y0 + 0.215, 0.28] });
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    b.add(box(0.008, 0.12, 0.008), C.whiteTrim, { at: [0.3 + Math.cos(a) * 0.09, Y0 + 0.07, 0.28 + Math.sin(a) * 0.09] });
  }
  const shed = block(b, col('#4f8a5b'), 0.3, -0.28, 0.16, 0.12, 0.14);
  gableRoof(b, shed, C.shingle, 0.06, 0.015);
  teamFlag(b, -0.38, 0.36, 0.3);
  return b.build();
}

/** Alpine Canyon Apartments: two storeys of cream lap siding on a raised basement, under a plain gable. */
function alpineCanyon() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  b.add(box(0.6, 0.07, 0.36), C.concrete, { at: [0, Y0 + 0.035, -0.06] });
  const h = block(b, C.siding, 0, -0.06, 0.58, 0.3, 0.34, { y0: Y0 + 0.07 });
  for (let i = 1; i < 10; i++) b.add(box(0.584, 0.004, 0.344), col('#d4ccb8'), { at: [0, h.y0 + i * 0.03, -0.06] });
  gableRoof(b, h, C.shingleGrey, 0.13, 0.035);
  b.add(box(0.66, 0.012, 0.012), C.whiteTrim, { at: [0, h.top, 0.14] })
    .add(box(0.66, 0.012, 0.012), C.whiteTrim, { at: [0, h.top, -0.26] });
  windowsOn(b, h, 'fblr', 2, 4, { y0: 0.08, dy: 0.14, seed: 4 });
  b.add(box(0.12, 0.012, 0.1), C.concrete, { at: [0, Y0 + 0.075, 0.16] });
  door(b, 0, 0.113, K.team, 0, 0.05, 0.1, Y0 + 0.07);
  teamFlag(b, 0.36, 0.36);
  return b.build();
}

// ---------------------------------------------------------------- HQs

/** A round-arched window: a pane with a half-disc on top, facing +z unless turned by ry. */
function archWindow(b, x, y, z, w, h, { ry = 0, lit = true, trim = null } = {}) {
  const c = lit ? C.lit : C.unlit, glow = lit ? 0.3 : 0;
  b.add(box(w, h, 0.01), c, { glow, at: [x, y, z], rot: [0, ry, 0] })
    .add(cyl(w / 2, w / 2, 0.01, 10, 1, false, -Math.PI / 2, Math.PI), c, { glow, at: [x, y + h / 2, z], rot: [-Math.PI / 2, ry, 0] });
  if (trim) b.add(cyl(w / 2 + 0.01, w / 2 + 0.01, 0.006, 10, 1, false, -Math.PI / 2, Math.PI), trim, { at: [x, y + h / 2 + 0.002, z - 0.003], rot: [-Math.PI / 2, ry, 0] });
}

function columns(b, xs, z, y0, h, r = 0.014, colour = C.whiteTrim) {
  for (const x of xs) b.add(cyl(r, r * 1.15, h, 8), colour, { at: [x, y0 + h / 2, z] });
}

function steps(b, x, z, w, n = 3, colour = C.concrete) {
  for (let i = 0; i < n; i++) b.add(box(w - i * 0.02, 0.02, 0.05 + (n - i) * 0.03), colour, { at: [x, Y0 + 0.01 + i * 0.02, z + (n - i) * 0.015] });
}

function tree(b, x, z, s = 1) {
  b.add(cyl(0.008 * s, 0.01 * s, 0.06 * s, 5), K.woodDark, { at: [x, Y0 + 0.03 * s, z] })
    .add(cone(0.05 * s, 0.16 * s, 7), col('#2f5a34'), { at: [x, Y0 + 0.13 * s, z] });
}

/** Noyes Building (1899-1909): red brick on cream stone, a pedimented centre bay, Ionic portico and wide steps. */
function noyes() {
  const b = new Builder();
  foundation(b);
  const cream = col('#e3dac0'), trim = col('#f0ebdd'), brick = col('#a4553c');
  const base = block(b, cream, 0, -0.06, 0.84, 0.1, 0.52);
  const main = block(b, brick, 0, -0.06, 0.82, 0.3, 0.5, { y0: base.top });
  b.add(box(0.83, 0.018, 0.51), cream, { at: [0, base.top + 0.15, -0.06] })
    .add(box(0.88, 0.03, 0.56), trim, { at: [0, main.top + 0.015, -0.06] });
  hipRoof(b, { ...main, top: main.top + 0.03 }, col('#7a6a5e'), 0.14, 0.03);
  // The projecting centre bay, crowned by a pediment rising above the eaves.
  b.add(box(0.3, 0.1, 0.07), cream, { at: [0, Y0 + 0.05, 0.225] })
    .add(box(0.3, 0.3, 0.07), brick, { at: [0, base.top + 0.15, 0.225] })
    .add(box(0.32, 0.03, 0.08), trim, { at: [0, main.top + 0.015, 0.225] })
    .add(prism(0.34, 0.15, 0.1), trim, { at: [0, main.top + 0.105, 0.21] })
    .add(prism(0.26, 0.09, 0.01), K.team, { team: 1, at: [0, main.top + 0.085, 0.262] });       // tympanum
  // The portico: four Ionic columns, a frieze, and a balustraded balcony above.
  columns(b, [-0.1, -0.05, 0.05, 0.1], 0.31, Y0 + 0.04, 0.2, 0.013, trim);
  b.add(box(0.26, 0.035, 0.08), trim, { at: [0, Y0 + 0.26, 0.3] })
    .add(box(0.24, 0.03, 0.06), trim, { at: [0, Y0 + 0.29, 0.3] });
  for (let i = -5; i <= 5; i++) b.add(box(0.006, 0.025, 0.006), trim, { at: [i * 0.02, Y0 + 0.32, 0.33] });
  // Teal double doors under an arched fanlight, and the stone steps.
  b.add(box(0.07, 0.11, 0.01), col('#3f7f80'), { at: [0, Y0 + 0.095, 0.262] });
  archWindow(b, 0, Y0 + 0.17, 0.262, 0.07, 0.02, { trim });
  steps(b, 0, 0.36, 0.5, 4, col('#bcb6aa'));
  // Paired tall windows, round-arched pairs at the ends of the upper floor, little basement windows.
  for (const s of [-1, 1]) {
    for (let i = 0; i < 4; i++) {
      const x = s * (0.2 + i * 0.055);
      for (const y of [base.top + 0.07, base.top + 0.21]) {
        if (i === 3 && y > base.top + 0.1) archWindow(b, x, y - 0.01, 0.192, 0.03, 0.06, { trim });
        else b.add(box(0.026, 0.085, 0.01), (i + (y > 0.3 ? 1 : 0)) % 3 ? C.lit : C.unlit, { glow: (i + (y > 0.3 ? 1 : 0)) % 3 ? 0.5 : 0, at: [x, y, 0.192] });
      }
      b.add(box(0.026, 0.025, 0.01), C.unlit, { at: [x, Y0 + 0.05, 0.202] });
    }
  }
  windowsOn(b, main, 'blr', 2, 9, { y0: 0.07, dy: 0.14, ww: 0.026, wh: 0.085, seed: 3 });
  flag(b, 0, main.top + 0.18, 0.21, 0.18);
  return b.build();
}

/** Greenwood Student Center: a broad buff brick block, a raised atrium box, recessed corner entrances. */
function greenwood() {
  const b = new Builder();
  foundation(b);
  const brick = col('#d3a676'), band = col('#dcc9a0'), glass = col('#3a2e28');
  const main = block(b, brick, 0, 0, 0.8, 0.26, 0.8);
  for (const y of [0.012, 0.13]) b.add(box(0.805, 0.022, 0.805), band, { at: [0, Y0 + y, 0] });
  flatRoof(b, main, col('#b8b8b0'), { band: 0.02 });
  // The atrium rises a storey above the roof, ringed with clerestory glass.
  const atrium = block(b, brick, 0, 0, 0.34, 0.14, 0.34, { y0: main.top + 0.02 });
  b.add(box(0.345, 0.05, 0.345), C.glass, { glow: 0.35, at: [0, atrium.y0 + 0.07, 0] });
  flatRoof(b, atrium, col('#b8b8b0'));
  // Recessed entrances at the corners, the name on a stone band.
  for (const [sx, sz] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    b.add(box(0.16, 0.13, 0.03), glass, { glow: 0.3, at: [sx * 0.34, Y0 + 0.065, sz * 0.34], rot: [0, sx * sz > 0 ? -Math.PI / 4 : Math.PI / 4, 0] })
      .add(box(0.2, 0.03, 0.04), band, { at: [sx * 0.345, Y0 + 0.15, sz * 0.345], rot: [0, sx * sz > 0 ? -Math.PI / 4 : Math.PI / 4, 0] });
  }
  b.add(box(0.36, 0.035, 0.012), band, { at: [0, Y0 + 0.2, 0.402] })
    .add(box(0.3, 0.015, 0.012), K.team, { team: 1, at: [0, Y0 + 0.2, 0.408] });
  // Slit windows upstairs in groups, a few wide ones below.
  for (const face of ['f', 'b', 'l', 'r']) {
    for (const g of [-0.2, 0.2]) {
      for (let i = -1; i <= 1; i++) {
        const off = g + i * 0.03;
        const at = face === 'f' ? [off, Y0 + 0.2, 0.402] : face === 'b' ? [off, Y0 + 0.2, -0.402] : [face === 'r' ? 0.402 : -0.402, Y0 + 0.2, off];
        b.add(box(face === 'f' || face === 'b' ? 0.014 : 0.01, 0.07, face === 'f' || face === 'b' ? 0.01 : 0.014), C.unlit, { at });
      }
    }
  }
  glassBand(b, main, 'f', 0.07, 0.04, glass, 0.2, 0.4);
  return b.build();
}

/** Eccles Center: a brick base under a skyline of dark stage boxes, a glass lobby beneath a silver canopy. */
function eccles() {
  const b = new Builder();
  foundation(b);
  const brick = col('#8c4a3a'), dark = col('#3b302e'), redConcrete = col('#b8735c');
  const base = block(b, brick, 0, -0.04, 0.88, 0.14, 0.6);
  flatRoof(b, base, col('#a9a59c'), { band: 0.015 });
  glassBand(b, base, 'f', 0.07, 0.025, C.unlit, 0, 0.5);
  // The stepped stage houses: the tallest is the fly tower.
  const boxes = [[-0.28, -0.1, 0.26, 0.56, 0.32], [0.06, -0.1, 0.3, 0.34, 0.36], [0.32, -0.12, 0.2, 0.24, 0.3]];
  for (const [x, z, w, h, d] of boxes) {
    const blk = block(b, dark, x, z, w, h, d, { y0: base.top });
    b.add(box(w - 0.02, 0.012, d - 0.02), C.roofMembrane, { team: 1, at: [x, blk.top + 0.006, z] });
    for (let i = 1; i < 4; i++) b.add(box(w + 0.004, 0.006, d + 0.004), col('#2e2624'), { at: [x, blk.y0 + i * h / 4, z] });
  }
  // The two-storey glass lobby with its big flat silver canopy, a third of the way along.
  const lobby = block(b, C.glass, 0.12, 0.2, 0.26, 0.26, 0.14, { glow: 0.4 });
  for (let i = -3; i <= 3; i++) b.add(box(0.006, 0.26, 0.006), col('#9aa0a6'), { at: [0.12 + i * 0.037, Y0 + 0.13, 0.272] });
  b.add(box(0.27, 0.006, 0.006), col('#9aa0a6'), { at: [0.12, Y0 + 0.13, 0.272] })
    .add(box(0.36, 0.02, 0.22), col('#d0d3d6'), { at: [0.12, lobby.top + 0.01, 0.24] })
    .add(box(0.3, 0.012, 0.02), K.team, { team: 1, glow: 0.3, at: [0.12, lobby.top + 0.01, 0.352] });
  // Red board-formed concrete walls with twin stairs up to the entry.
  for (const x of [-0.06, 0.3]) b.add(box(0.03, 0.1, 0.16), redConcrete, { at: [x, Y0 + 0.05, 0.36] });
  b.add(box(0.12, 0.1, 0.03), redConcrete, { at: [0.12, Y0 + 0.05, 0.43] });
  for (const x of [0.0, 0.24]) steps(b, x, 0.33, 0.1, 3, col('#c9c4ba'));
  return b.build();
}

/** Karen H. Huntsman Library: red brick on a stone base, a pedimented entry pavilion, arched window triplets. */
function huntsman() {
  const b = new Builder();
  foundation(b);
  const brick = col('#b5603f'), cream = col('#ede3cc');
  const base = block(b, cream, 0.03, -0.06, 0.78, 0.05, 0.48);
  const main = block(b, brick, 0.03, -0.06, 0.76, 0.3, 0.46, { y0: base.top });
  b.add(box(0.762, 0.016, 0.462), cream, { at: [0.03, base.top + 0.14, -0.06] })
    .add(box(0.8, 0.035, 0.5), cream, { at: [0.03, main.top + 0.017, -0.06] });
  hipRoof(b, { ...main, top: main.top + 0.035 }, col('#9c8466'), 0.06, 0.02);
  // The entry pavilion: taller, pedimented, with a paired-column portico and a balcony.
  const pav = block(b, brick, -0.18, 0.2, 0.22, 0.4, 0.08);
  b.add(box(0.24, 0.03, 0.1), cream, { at: [-0.18, pav.top + 0.015, 0.2] })
    .add(prism(0.26, 0.1, 0.1), cream, { at: [-0.18, pav.top + 0.08, 0.2] })
    .add(prism(0.18, 0.06, 0.01), K.team, { team: 1, at: [-0.18, pav.top + 0.07, 0.252] });
  columns(b, [-0.27, -0.24, -0.12, -0.09], 0.3, Y0, 0.17, 0.011);
  b.add(box(0.24, 0.035, 0.1), cream, { at: [-0.18, Y0 + 0.19, 0.28] })
    .add(box(0.24, 0.025, 0.012), cream, { at: [-0.18, Y0 + 0.22, 0.33] });
  b.add(box(0.06, 0.12, 0.01), K.woodDark, { at: [-0.18, Y0 + 0.06, 0.242] });
  archWindow(b, -0.18, Y0 + 0.27, 0.242, 0.04, 0.05, { trim: cream });
  steps(b, -0.18, 0.36, 0.28, 3);
  // Upper floor: groups of three arched windows over cream ledges; below, wide windows with lintels.
  for (const g of [0.06, 0.28]) {
    b.add(box(0.16, 0.025, 0.03), cream, { at: [g, base.top + 0.15, 0.18] });
    for (let i = -1; i <= 1; i++) {
      archWindow(b, g + i * 0.045, base.top + 0.2, 0.172, 0.034, 0.06, { trim: cream });
      b.add(box(0.036, 0.075, 0.01), C.lit, { glow: 0.5, at: [g + i * 0.045, base.top + 0.06, 0.172] });
    }
    b.add(box(0.15, 0.015, 0.012), cream, { at: [g, base.top + 0.105, 0.174] });
  }
  windowsOn(b, main, 'blr', 2, 8, { y0: 0.07, dy: 0.14, ww: 0.035, wh: 0.075, seed: 5 });
  // The rounded bay at the corner and the white colonnade on the west end.
  b.add(cyl(0.08, 0.08, 0.28, 10), cream, { at: [0.4, base.top + 0.14, -0.26] })
    .add(cyl(0.083, 0.083, 0.12, 10), C.glass, { glow: 0.35, at: [0.4, base.top + 0.2, -0.26] });
  for (const z of [-0.2, -0.06, 0.08]) columns(b, [-0.42], z, Y0, 0.15, 0.01);
  b.add(box(0.05, 0.02, 0.36), C.whiteTrim, { at: [-0.42, Y0 + 0.16, -0.06] });
  return b.build();
}

/** Humanities Building: a broad, notched two-storey block round a light well (modelled from the site plan). */
function humanities() {
  const b = new Builder();
  foundation(b);
  const brick = col('#cfa577');
  const parts = [block(b, brick, -0.1, -0.2, 0.62, 0.28, 0.42), block(b, brick, 0.24, 0.04, 0.38, 0.26, 0.6), block(b, brick, -0.22, 0.2, 0.38, 0.24, 0.32)];
  for (const p of parts) {
    flatRoof(b, p, col('#e8e8e4'));
    windowsOn(b, p, 'fblr', 2, 5, { y0: 0.08, dy: 0.12, seed: Math.round(p.x * 10) + 5 });
  }
  b.add(box(0.12, 0.02, 0.12), col('#5f8a3c'), { at: [-0.02, parts[0].top + 0.03, -0.18] });   // the light well
  const ent = block(b, C.glass, 0.02, 0.33, 0.16, 0.14, 0.08, { glow: 0.4 });
  b.add(box(0.22, 0.02, 0.14), C.whiteTrim, { at: [0.02, ent.top + 0.01, 0.33] });
  return b.build();
}

/** Social Science Building: a small pale brick box with a precast band and a square entry canopy. */
function socialScience() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  b.add(box(0.5, 0.008, 0.3), C.concrete, { at: [0, Y0 + 0.006, 0.25] });
  const s = block(b, col('#d9b877'), 0, -0.1, 0.62, 0.3, 0.4);
  b.add(box(0.624, 0.06, 0.404), col('#e6c3a6'), { at: [0, Y0 + 0.17, -0.1] })
    .add(box(0.64, 0.03, 0.42), col('#4a3428'), { at: [0, s.top + 0.015, -0.1] })
    .add(box(0.6, 0.012, 0.38), C.roofMembrane, { team: 1, at: [0, s.top + 0.032, -0.1] });
  for (const x of [-0.18, 0.15]) b.add(box(0.07, 0.04, 0.06), K.steel, { at: [x, s.top + 0.05, -0.15] });
  windowsOn(b, s, 'fblr', 2, 5, { y0: 0.08, dy: 0.15, ww: 0.06, wh: 0.06, seed: 2 });
  // The square entry canopy on two piers.
  for (const x of [-0.08, 0.08]) b.add(box(0.04, 0.15, 0.04), col('#e6c3a6'), { at: [x, Y0 + 0.075, 0.2] });
  b.add(box(0.22, 0.03, 0.14), col('#e6c3a6'), { at: [0, Y0 + 0.165, 0.16] })
    .add(box(0.2, 0.012, 0.12), K.team, { team: 1, at: [0, Y0 + 0.186, 0.16] });
  door(b, 0, 0.102, C.glass, 0, 0.08, 0.12);
  tree(b, -0.36, 0.3); tree(b, 0.36, 0.3); tree(b, 0.38, -0.36, 0.8);
  return b.build();
}

/** Business Building: two halves built at different times, a white roof and a dark one with a skylight. */
function business() {
  const b = new Builder();
  foundation(b);
  const west = block(b, col('#c9a27a'), -0.2, 0.0, 0.42, 0.24, 0.66);
  b.add(box(0.44, 0.03, 0.68), col('#e6e4de'), { at: [-0.2, west.top + 0.015, 0] });
  const wing = block(b, col('#c9a27a'), -0.2, 0.38, 0.2, 0.15, 0.1);
  b.add(box(0.22, 0.02, 0.12), col('#e6e4de'), { at: [-0.2, wing.top + 0.01, 0.38] });
  const east = block(b, col('#9b5a43'), 0.22, -0.04, 0.42, 0.26, 0.74);
  b.add(box(0.44, 0.03, 0.76), col('#2a2a2e'), { at: [0.22, east.top + 0.015, -0.04] })
    .add(cone(0.11, 0.08, 4), C.glass, { glow: 0.4, at: [0.22, east.top + 0.07, -0.04], rot: [0, Math.PI / 4, 0] });
  for (const blk of [west, east]) {
    b.add(box(blk.w + 0.006, 0.025, blk.d + 0.006), K.team, { team: 1, at: [blk.x, blk.top - 0.03, blk.z] });
    windowsOn(b, blk, 'fb', 2, 4, { y0: 0.07, dy: 0.12, seed: Math.round(blk.x * 10) + 3 });
  }
  door(b, -0.2, 0.432, C.glass, 0, 0.08, 0.11);
  return b.build();
}

/** Health Science Center: a long block fronted by a diamond-turned pavilion under a raised lantern. */
function healthScience() {
  const b = new Builder();
  foundation(b);
  lawn(b);
  for (const s of [-1, 1]) b.add(box(0.05, 0.014, 0.5), C.concrete, { at: [s * 0.2, Y0 + 0.002, 0.25], rot: [0, s * 0.7, 0] });
  const main = block(b, col('#c49a74'), 0, -0.22, 0.84, 0.22, 0.42);
  flatRoof(b, main, C.whiteTrim);
  windowsOn(b, main, 'fblr', 1, 8, { y0: 0.1, seed: 6 });
  block(b, col('#c49a74'), 0, 0.03, 0.12, 0.16, 0.12);
  // The pavilion, turned 45 degrees, with its octagonal lantern.
  b.add(box(0.28, 0.2, 0.28), col('#c49a74'), { at: [0, Y0 + 0.1, 0.2], rot: [0, Math.PI / 4, 0] })
    .add(box(0.3, 0.025, 0.3), C.whiteTrim, { at: [0, Y0 + 0.212, 0.2], rot: [0, Math.PI / 4, 0] })
    .add(box(0.28, 0.012, 0.28), C.roofMembrane, { team: 1, at: [0, Y0 + 0.228, 0.2], rot: [0, Math.PI / 4, 0] })
    .add(cyl(0.08, 0.08, 0.08, 8), C.glass, { glow: 0.45, at: [0, Y0 + 0.27, 0.2] })
    .add(cone(0.1, 0.07, 8), C.whiteTrim, { at: [0, Y0 + 0.345, 0.2] });
  for (const a of [Math.PI / 4, -Math.PI / 4]) {
    b.add(box(0.1, 0.12, 0.01), C.glass, { glow: 0.4, at: [Math.sin(a) * 0.1, Y0 + 0.06, 0.2 + Math.cos(a) * 0.1], rot: [0, a, 0] });
  }
  return b.build();
}

/** Horne Activity Center: a big tan brick gym box, grey metal panels, tall corner window strips. */
function horne() {
  const b = new Builder();
  foundation(b);
  const gym = block(b, col('#c49a6c'), 0, -0.06, 0.84, 0.5, 0.56);
  b.add(box(0.86, 0.04, 0.58), col('#bfc3c6'), { at: [0, gym.top + 0.02, gym.z] })
    .add(box(0.8, 0.012, 0.52), C.roofMembrane, { team: 1, at: [0, gym.top + 0.042, gym.z] });
  b.add(box(0.84, 0.12, 0.012), col('#8e9196'), { at: [0, Y0 + 0.4, 0.225] })
    .add(box(0.012, 0.12, 0.56), col('#8e9196'), { at: [-0.425, Y0 + 0.4, gym.z] });
  // The corner bay: three tall window strips, each capped with grey.
  for (let i = 0; i < 3; i++) {
    b.add(box(0.035, 0.26, 0.01), C.glass, { glow: 0.35, at: [0.26 + i * 0.055, Y0 + 0.2, 0.226] })
      .add(box(0.04, 0.05, 0.012), col('#8e9196'), { at: [0.26 + i * 0.055, Y0 + 0.36, 0.226] });
  }
  // The low glass entrance link with blue panels above the doors.
  const link = block(b, C.glass, -0.16, 0.3, 0.32, 0.12, 0.14, { glow: 0.4 });
  b.add(box(0.33, 0.04, 0.15), col('#3f6fa3'), { at: [-0.16, link.top + 0.02, 0.3] })
    .add(box(0.34, 0.012, 0.16), K.team, { team: 1, at: [-0.16, link.top + 0.046, 0.3] });
  // The sign pylon.
  b.add(box(0.05, 0.24, 0.1), col('#8e9196'), { at: [0.38, Y0 + 0.12, 0.38] })
    .add(box(0.052, 0.05, 0.08), K.team, { team: 1, glow: 0.4, at: [0.38, Y0 + 0.2, 0.38] });
  return b.build();
}

/** Bergeson Athletic Center: a long low bar beside the field, ribbon windows and orange and navy bands. */
function bergeson() {
  const b = new Builder();
  foundation(b);
  const turf = col('#3e7a3a');
  b.add(box(0.86, 0.012, 0.36), turf, { at: [0, Y0 + 0.006, 0.24] });
  for (let i = -3; i <= 3; i++) b.add(box(0.006, 0.013, 0.34), C.whiteTrim, { at: [i * 0.11, Y0 + 0.007, 0.24] });
  const bar = block(b, col('#e4e4e0'), 0, -0.2, 0.86, 0.3, 0.4);
  flatRoof(b, bar, col('#c8c8c4'));
  b.add(box(0.862, 0.03, 0.402), col('#e8762b'), { at: [0, Y0 + 0.12, -0.2] })
    .add(box(0.862, 0.03, 0.402), col('#1f2a44'), { at: [0, Y0 + 0.155, -0.2] });
  glassBand(b, bar, 'f', 0.07, 0.07, C.glass, 0.35, 0.94);
  glassBand(b, bar, 'f', 0.23, 0.06, C.glass, 0.35, 0.94);
  b.add(box(0.14, 0.13, 0.08), C.glass, { glow: 0.4, at: [0.3, Y0 + 0.065, 0.03] });
  return b.build();
}

/** Badger Stadium (Terry Foote Stadium): the field with navy end zones, a west grandstand and light poles. */
function stadium() {
  const b = new Builder();
  foundation(b);
  b.add(box(0.9, 0.012, 0.9), col('#d8d6d0'), { at: [0, Y0 + 0.006, 0] })                      // apron
    .add(box(0.5, 0.014, 0.8), col('#3e7a3a'), { at: [0.1, Y0 + 0.008, 0] });                   // turf
  for (const z of [-0.34, 0.34]) {
    b.add(box(0.5, 0.016, 0.1), col('#1f2a44'), { at: [0.1, Y0 + 0.009, z] })
      .add(box(0.52, 0.015, 0.012), col('#e8762b'), { at: [0.1, Y0 + 0.009, z + (z > 0 ? 0.05 : -0.05)] });
    // Goal posts.
    b.add(box(0.006, 0.08, 0.006), col('#f2c94c'), { at: [0.1, Y0 + 0.04, z + (z > 0 ? 0.06 : -0.06)] })
      .add(box(0.1, 0.006, 0.006), col('#f2c94c'), { at: [0.1, Y0 + 0.08, z + (z > 0 ? 0.06 : -0.06)] });
  }
  for (let i = -5; i <= 5; i++) b.add(box(0.5, 0.0155, 0.004), C.whiteTrim, { at: [0.1, Y0 + 0.009, i * 0.05] });
  b.add(cyl(0.05, 0.05, 0.016, 12), K.team, { team: 1, at: [0.1, Y0 + 0.01, 0] });            // the midfield S
  // The west grandstand: stepped bleachers under a flat white canopy, press box on top.
  for (let i = 0; i < 5; i++) b.add(box(0.06, 0.03, 0.42), col('#e8e8e8'), { at: [-0.2 - i * 0.04, Y0 + 0.015 + i * 0.03, 0] });
  b.add(box(0.06, 0.16, 0.42), col('#bdbdbd'), { at: [-0.38, Y0 + 0.08, 0] });
  for (const z of [-0.2, 0, 0.2]) b.add(box(0.012, 0.3, 0.012), C.whiteTrim, { at: [-0.4, Y0 + 0.15, z] });
  b.add(box(0.26, 0.015, 0.46), C.whiteTrim, { at: [-0.3, Y0 + 0.3, 0] })
    .add(box(0.1, 0.07, 0.2), C.glass, { glow: 0.4, at: [-0.36, Y0 + 0.345, 0] })
    .add(box(0.11, 0.012, 0.22), K.team, { team: 1, at: [-0.36, Y0 + 0.385, 0] });
  // Four slim light poles.
  for (const [x, z] of [[-0.42, -0.42], [-0.42, 0.42], [0.42, -0.42], [0.42, 0.42]]) {
    b.add(box(0.012, 0.55, 0.012), K.steel, { at: [x, Y0 + 0.275, z] })
      .add(box(0.07, 0.04, 0.02), C.lit, { glow: 1.2, at: [x, Y0 + 0.56, z], rot: [0, Math.atan2(x, z), 0] });
  }
  return b.build();
}

/** Lucy Phillips Building: the 1968 library, red brick under a white fascia, with a many-sided glass tower. */
function lucyPhillips() {
  const b = new Builder();
  foundation(b);
  const lib = block(b, C.brickRed, -0.06, -0.06, 0.72, 0.4, 0.56);
  flatRoof(b, lib, C.whiteTrim, { band: 0.04 });
  for (const y of [0.08, 0.22]) {
    glassBand(b, lib, 'f', y, 0.06, C.unlit, 0.1, 0.85);
    glassBand(b, lib, 'l', y, 0.06, C.unlit, 0.1, 0.85);
  }
  windowsOn(b, lib, 'fl', 2, 9, { y0: 0.08, dy: 0.14, ww: 0.045, wh: 0.055, seed: 2 });
  // The glass tower: bronze mullions and a ring of arched dormers at the top.
  b.add(cyl(0.13, 0.13, 0.52, 8), C.glass, { glow: 0.35, at: [0.3, Y0 + 0.26, 0.22] });
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2 + Math.PI / 8;
    b.add(box(0.012, 0.52, 0.012), col('#2b2a28'), { at: [0.3 + Math.cos(a) * 0.12, Y0 + 0.26, 0.22 + Math.sin(a) * 0.12] })
      .add(cyl(0.025, 0.025, 0.012, 8, 1, false, 0, Math.PI), C.lit, { glow: 0.6, at: [0.3 + Math.cos(a + Math.PI / 8) * 0.125, Y0 + 0.5, 0.22 + Math.sin(a + Math.PI / 8) * 0.125], rot: [Math.PI / 2, 0, -(a + Math.PI / 8) + Math.PI / 2] });
  }
  b.add(cone(0.15, 0.1, 8), K.team, { team: 1, at: [0.3, Y0 + 0.57, 0.22], rot: [0, Math.PI / 8, 0] });
  return b.build();
}

/** Career Center: a small brick shop building with a roll-up door. */
function careerCenter() {
  const b = new Builder();
  foundation(b);
  lawn(b, 0.9, 0.3);
  const shop = block(b, C.brick, 0, -0.1, 0.7, 0.26, 0.5);
  flatRoof(b, shop, C.fascia, { overhang: 0.02 });
  b.add(box(0.22, 0.18, 0.012), K.steel, { at: [-0.15, Y0 + 0.09, 0.152] });
  for (let i = 1; i < 6; i++) b.add(box(0.22, 0.004, 0.014), K.steelDark, { at: [-0.15, Y0 + i * 0.03, 0.153] });
  door(b, 0.12, 0.152, C.glass, 0, 0.06, 0.12);
  b.add(box(0.24, 0.05, 0.012), K.team, { team: 1, glow: 0.3, at: [0.12, Y0 + 0.2, 0.153] });
  windowsOn(b, shop, 'lr', 1, 3, { y0: 0.12, seed: 3 });
  return b.build();
}

/** Planetarium, in the Graham Science Center: brick wings with stone bands under a flaring white roof. */
function grahamScience() {
  const b = new Builder();
  foundation(b);
  const stone = col('#d9cbb0'), brick = col('#a0482f'), roof = col('#ededed');
  const a = block(b, brick, -0.12, -0.22, 0.66, 0.56, 0.32);
  const c = block(b, brick, -0.3, 0.14, 0.3, 0.5, 0.42);
  for (const blk of [a, c]) {
    for (const y of [0.15, 0.3, 0.45]) b.add(box(blk.w + 0.006, 0.02, blk.d + 0.006), stone, { at: [blk.x, Y0 + y, blk.z] });
    b.add(box(blk.w + 0.1, 0.02, blk.d + 0.1), roof, { at: [blk.x, blk.top + 0.01, blk.z] })
      .add(box(blk.w - 0.02, 0.012, blk.d - 0.02), C.roofMembrane, { team: 1, at: [blk.x, blk.top + 0.026, blk.z] });
  }
  // The roof flares up at the corner like a wing.
  b.add(box(0.34, 0.015, 0.26), roof, { at: [0.28, a.top + 0.06, -0.14], rot: [0.0, 0, 0.32] });
  windowsOn(b, a, 'fb', 3, 7, { y0: 0.08, dy: 0.15, seed: 6 });
  for (let i = 0; i < 4; i++) {
    b.add(cyl(0.022, 0.022, 0.01, 8, 1, false, 0, Math.PI), C.lit, { glow: 0.5, at: [-0.4 + i * 0.13 + 0.02, Y0 + 0.27, a.z + 0.168], rot: [Math.PI / 2, 0, Math.PI / 2] });
  }
  windowsOn(b, c, 'fl', 3, 3, { y0: 0.08, dy: 0.15, seed: 2 });
  // The curved white-and-glass wing, and the full-height glass entry tower.
  b.add(cyl(0.2, 0.2, 0.38, 12, 1, false, 0, Math.PI / 2), C.whiteTrim, { at: [0.12, Y0 + 0.19, 0.0] })
    .add(cyl(0.205, 0.205, 0.2, 12, 1, false, 0.1, Math.PI / 2 - 0.2), C.glass, { glow: 0.35, at: [0.12, Y0 + 0.2, 0.0] });
  const tower = block(b, C.glass, -0.05, 0.12, 0.12, 0.62, 0.12, { glow: 0.4 });
  b.add(box(0.14, 0.02, 0.14), roof, { at: [-0.05, tower.top + 0.01, 0.12] });
  // Sun-shade louvres.
  for (let i = 0; i < 4; i++) b.add(box(0.3, 0.008, 0.04), col('#c8c8c8'), { at: [-0.25 + i * 0.0, Y0 + 0.17 + i * 0.1, a.z + 0.18] });
  return b.build();
}

/** Sevier Valley Center (Richfield): a brick drum wrapped in a curved glass lobby, a dark fly tower with an S. */
function sevierValley() {
  const b = new Builder();
  foundation(b);
  b.add(cyl(0.3, 0.3, 0.5, 16), col('#a2462f'), { at: [0, Y0 + 0.25, -0.08] })
    .add(cyl(0.29, 0.29, 0.012, 16), C.roofMembrane, { team: 1, at: [0, Y0 + 0.506, -0.08] });
  // The curved lobby band: glass with silver mullions.
  b.add(cyl(0.4, 0.4, 0.24, 20, 1, false, -Math.PI / 2, Math.PI), C.glass, { glow: 0.4, at: [0, Y0 + 0.12, -0.08] })
    .add(cyl(0.41, 0.41, 0.02, 20, 1, false, -Math.PI / 2, Math.PI), col('#c7ccd0'), { at: [0, Y0 + 0.245, -0.08] });
  for (let i = 0; i <= 8; i++) {
    const t = -Math.PI / 2 + (i / 8) * Math.PI;
    b.add(box(0.008, 0.24, 0.008), col('#c7ccd0'), { at: [Math.sin(t) * 0.401, Y0 + 0.12, -0.08 + Math.cos(t) * 0.401] });
  }
  // The dark brown fly tower with its sloped white top and the orange S.
  const fly = block(b, col('#4e3a2e'), -0.28, -0.3, 0.22, 0.72, 0.22);
  b.add(box(0.24, 0.03, 0.24), C.whiteTrim, { at: [-0.28, fly.top + 0.03, -0.3], rot: [0.3, 0, 0] })
    .add(box(0.12, 0.16, 0.01), col('#e8a02b'), { glow: 0.5, at: [-0.28, Y0 + 0.55, -0.188] })
    .add(box(0.13, 0.03, 0.012), K.team, { team: 1, glow: 0.4, at: [-0.28, Y0 + 0.44, -0.188] });
  // The leaning glass entry box.
  b.add(box(0.14, 0.2, 0.12), C.glass, { glow: 0.45, at: [0.22, Y0 + 0.1, 0.3], rot: [0.12, 0.3, 0.1] });
  return b.build();
}

/** Sorensen Administration Building (Richfield): low brick boxes round a taller centre, a columned canopy. */
function sorensen() {
  const b = new Builder();
  foundation(b);
  const brick = col('#8e4b3a'), belt = col('#5a3328'), coping = col('#4a2e25');
  const centre = block(b, brick, 0, -0.08, 0.4, 0.36, 0.4);
  const wl = block(b, brick, -0.3, -0.02, 0.24, 0.2, 0.62);
  const wr = block(b, brick, 0.3, -0.02, 0.24, 0.2, 0.62);
  for (const blk of [centre, wl, wr]) {
    b.add(box(blk.w + 0.004, 0.02, blk.d + 0.004), belt, { at: [blk.x, Y0 + 0.12, blk.z] })
      .add(box(blk.w + 0.02, 0.025, blk.d + 0.02), coping, { at: [blk.x, blk.top + 0.012, blk.z] })
      .add(box(blk.w - 0.02, 0.012, blk.d - 0.02), C.roofMembrane, { team: 1, at: [blk.x, blk.top + 0.028, blk.z] });
  }
  glassBand(b, centre, 'f', 0.3, 0.04, C.unlit, 0.15, 0.85);   // clerestory
  glassBand(b, centre, 'b', 0.3, 0.04, C.unlit, 0.15, 0.85);
  // The glass storefront entry under a flat canopy on round white columns.
  b.add(box(0.24, 0.13, 0.01), C.glass, { glow: 0.4, at: [0, Y0 + 0.065, 0.125] })
    .add(box(0.3, 0.02, 0.16), C.whiteTrim, { at: [0, Y0 + 0.16, 0.2] })
    .add(box(0.28, 0.012, 0.14), K.team, { team: 1, at: [0, Y0 + 0.174, 0.2] });
  for (const x of [-0.12, 0.12]) b.add(cyl(0.012, 0.012, 0.15, 8), C.whiteTrim, { at: [x, Y0 + 0.075, 0.27] });
  windowsOn(b, wl, 'fl', 1, 2, { y0: 0.07, seed: 3 });
  windowsOn(b, wr, 'fr', 1, 2, { y0: 0.07, seed: 8 });
  return b.build();
}

export const HQ_MODELS = [
  noyes, greenwood, eccles, huntsman, humanities, socialScience, business, healthScience,
  horne, bergeson, stadium, lucyPhillips, careerCenter, grahamScience, sevierValley, sorensen,
];

export const HOUSING_MODELS = [
  suites, anderson, maryNielson, snowHall, nuttall, castilleja, summitValley, badgerStudios,
  nordicPointe, snowGarden, pinetree, acornOaktree, badgerHouse, badgerLoft, upHouse, alpineCanyon,
];

// Renders every launcher, splash, notification and store image from assets/brand/icon-master.svg.
// Run with `npm run icons` after changing the master; the PNGs are committed, so builds never need
// this script or its devDependency (@resvg/resvg-js).
import { Resvg } from '@resvg/resvg-js';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const brand = join(dirname(fileURLToPath(import.meta.url)), '..', 'assets', 'brand');
const master = readFileSync(join(brand, 'icon-master.svg'), 'utf8');
// Gradients and the blur filter come from the master, so a colour change there reaches every layer.
const defs = master.match(/<defs>[\s\S]*?<\/defs>/)[0];

// The master's layers, by name. Geometry: ring centred at 512 with r 290, the arc running clockwise
// from 12 to 9 o'clock, the orb in the gap at 45 degrees.
const ARC = 'M512 222 A290 290 0 1 1 222 512';
const layer = {
  background: '<rect width="1024" height="1024" fill="url(#bg)"/><rect width="1024" height="1024" fill="url(#glow)"/>',
  track: '<circle cx="512" cy="512" r="290" fill="none" stroke="#FFFFFF" stroke-opacity="0.08" stroke-width="84"/>',
  arcGlow: `<path d="${ARC}" fill="none" stroke="url(#mark)" stroke-width="84" stroke-linecap="round" opacity="0.55" filter="url(#blur)"/>`,
  arc: `<path d="${ARC}" fill="none" stroke="url(#mark)" stroke-width="84" stroke-linecap="round"/>`,
  orbGlow: '<circle cx="307" cy="307" r="62" fill="#FF8A4C" opacity="0.7" filter="url(#blur)"/>',
  orb: '<circle cx="307" cy="307" r="54" fill="url(#orb)"/>',
  disc: '<circle cx="512" cy="512" r="168" fill="#FFFFFF" fill-opacity="0.06" stroke="#FFFFFF" stroke-opacity="0.16" stroke-width="3"/>',
  v: '<polyline points="448,474 512,570 576,474" fill="none" stroke="#FFFFFF" stroke-width="54" stroke-linecap="round" stroke-linejoin="round"/>',
};
// Single-colour silhouette for themed icons and the status bar: no track, glow, blur or gradient.
const silhouette = [
  `<path d="${ARC}" fill="none" stroke="#FFFFFF" stroke-width="84" stroke-linecap="round"/>`,
  '<circle cx="307" cy="307" r="54" fill="#FFFFFF"/>',
  layer.v,
].join('');
const mark = [layer.track, layer.arcGlow, layer.arc, layer.orbGlow, layer.orb, layer.disc, layer.v].join('');

// The mark for light backgrounds (the light-theme splash): the master's white V, track and disc
// would vanish, and its mint-cyan arc washes out, so those take the background's violet and
// midnight instead. RingMark's `brandOnLight` tone draws the same.
const stop = (id) => master.match(new RegExp(`id="${id}"[\\s\\S]*?stop-color="(#[0-9A-Fa-f]{6})"[\\s\\S]*?stop-color="(#[0-9A-Fa-f]{6})"`)).slice(1);
const [violet, night] = stop('bg');
const [key] = stop('glow');
const onLight = [
  `<linearGradient id="markLight" x1="0.15" y1="0.1" x2="0.85" y2="0.9"><stop offset="0" stop-color="${key}"/><stop offset="1" stop-color="${violet}"/></linearGradient>`,
  `<circle cx="512" cy="512" r="290" fill="none" stroke="${night}" stroke-opacity="0.08" stroke-width="84"/>`,
  `<path d="${ARC}" fill="none" stroke="url(#markLight)" stroke-width="84" stroke-linecap="round"/>`,
  layer.orbGlow, layer.orb,
  `<circle cx="512" cy="512" r="168" fill="${night}" fill-opacity="0.04" stroke="${night}" stroke-opacity="0.12" stroke-width="3"/>`,
  layer.v.replace('#FFFFFF', night),
].join('');
// Status-bar icon: the ring and orb only (at 24dp the V is about 3dp and unreadable), inside a 2dp
// margin so it sits in the 20dp live area next to the system's icons.
const notification = [
  `<path d="${ARC}" fill="none" stroke="#FFFFFF" stroke-width="84" stroke-linecap="round"/>`,
  '<circle cx="307" cy="307" r="62" fill="#FFFFFF"/>',
].join('');

/** Scales content about the centre: 0.88 keeps the ring (r 332 with stroke) inside the 66dp safe zone. */
const scaled = (content, factor) => `<g transform="translate(512 512) scale(${factor}) translate(-512 -512)">${content}</g>`;
const svg = (content, viewBox = '0 0 1024 1024') => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}">${defs}${content}</svg>`;

function render(file, source, size) {
  const png = new Resvg(source, { fitTo: { mode: 'width', value: size }, font: { loadSystemFonts: false } }).render().asPng();
  mkdirSync(dirname(join(brand, file)), { recursive: true });
  writeFileSync(join(brand, file), png);
  console.log(`${file} (${size}px)`);
}

render('icon.png', master, 1024);
render('store/play-store-512.png', master, 512);
render('adaptive-background.png', svg(layer.background), 1024);
render('adaptive-foreground.png', svg(scaled(mark, 0.88)), 1024);
render('adaptive-monochrome.png', svg(scaled(silhouette, 0.88)), 1024);
// Android 12+ splash: an icon without a background is masked to a circle two thirds of its canvas,
// so the mark is scaled to fit that circle.
render('splash-icon.png', svg(scaled(mark, 0.82)), 1024);
render('splash-icon-light.png', svg(scaled(onLight, 0.82)), 1024);
// 664 units of ring in a 797-unit box: 20dp of 24dp.
render('notification-icon.png', svg(notification, '113 113 797 797'), 192);

// Generates src/theme/palette.generated.ts: the light and dark M3 schemes, the semantic roles and
// the fixed identity hues. Run with `npm run palette` after changing a key colour; the output is
// committed, so the app has no runtime dependency on @material/material-color-utilities.
import { readFileSync, writeFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// 0.4.0 ships a few extensionless relative imports that Node's ESM resolver refuses; retry them
// with ".js" rather than pinning the older spec.
registerHooks({
  resolve: (specifier, context, next) => {
    try { return next(specifier, context); } catch (e) {
      if (specifier.startsWith('.') && !specifier.endsWith('.js')) return next(`${specifier}.js`, context);
      throw e;
    }
  },
});
const {
  argbFromHex, Blend, DynamicScheme, Hct, hexFromArgb, MaterialDynamicColors, TonalPalette, Variant,
} = await import('@material/material-color-utilities');

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

// ---- Key colours -------------------------------------------------------------------------------
const KEY = {
  primary: '#7B5CFF', // violet
  secondary: '#38C6FF', // cyan
  tertiary: '#FF8A4C', // amber-orange: renewal warnings and overdue only
  neutral: '#0B0A1F', // midnight
};

// Semantic roles, harmonised towards primary so they sit in the same family.
const SEMANTIC = {
  renewalWarning: '#FF9A3C', // amber: overdue / due now
  renewalSoon: '#FFC94A', // due within the reminder window; also the warning banner
  success: '#2FD9A6', // mint
};

// Fixed identities: hue follows the category or payment type, never its rank. Red stays out of the
// set (it means error). Utilities (amber) and Food (orange) sit near renewalWarning; overdue never
// relies on colour alone (icon + "Overdue" text), so these keep their established hues.
const CATEGORY = {
  entertainment: ['#2a78d6', '#3987e5'],
  food: ['#eb6834', '#d95926'],
  fitness: ['#1baf7a', '#199e70'],
  utilities: ['#eda100', '#c98500'],
  travel: ['#e87ba4', '#d55181'],
  finance: ['#008300', '#008300'],
  productivity: ['#4a3aa7', '#9085e9'],
  other: ['#8A8A99', '#767686'],
  neutral: ['#8A8A99', '#767686'],
};
const PAYMENT = {
  Card: ['#1F6FEB', '#5AA0FF'],
  BankAccount: ['#0E8A80', '#3FD0C3'],
  Wallet: ['#7A3FD0', '#A98CF0'],
  Other: ['#8A8A99', '#9C9CAB'],
};

// ---- Schemes -----------------------------------------------------------------------------------
const neutralHue = Hct.fromInt(argbFromHex(KEY.neutral)).hue;
const palettes = {
  primaryPalette: TonalPalette.fromInt(argbFromHex(KEY.primary)),
  secondaryPalette: TonalPalette.fromInt(argbFromHex(KEY.secondary)),
  tertiaryPalette: TonalPalette.fromInt(argbFromHex(KEY.tertiary)),
  // A whisper of the midnight hue: surfaces read violet-tinted, never grey-blue.
  neutralPalette: TonalPalette.fromHueAndChroma(neutralHue, 6),
  neutralVariantPalette: TonalPalette.fromHueAndChroma(neutralHue, 12),
};

const ROLES = [
  'primary', 'onPrimary', 'primaryContainer', 'onPrimaryContainer', 'inversePrimary',
  'secondary', 'onSecondary', 'secondaryContainer', 'onSecondaryContainer',
  'tertiary', 'onTertiary', 'tertiaryContainer', 'onTertiaryContainer',
  'error', 'onError', 'errorContainer', 'onErrorContainer',
  'background', 'onBackground', 'surface', 'onSurface', 'surfaceVariant', 'onSurfaceVariant',
  'surfaceDim', 'surfaceBright', 'surfaceContainerLowest', 'surfaceContainerLow', 'surfaceContainer',
  'surfaceContainerHigh', 'surfaceContainerHighest',
  'outline', 'outlineVariant', 'inverseSurface', 'inverseOnSurface', 'shadow',
];

function scheme(isDark) {
  const s = new DynamicScheme({
    sourceColorHct: Hct.fromInt(argbFromHex(KEY.primary)),
    variant: Variant.EXPRESSIVE,
    contrastLevel: 0,
    isDark,
    specVersion: '2025',
    ...palettes,
  });
  const out = {};
  for (const role of ROLES) out[role] = hexFromArgb(MaterialDynamicColors[role].getArgb(s)).toUpperCase();
  // Translucent by design; a separate token rather than alpha appended at runtime.
  out.scrim = isDark ? 'rgba(0,0,0,0.6)' : 'rgba(0,0,0,0.4)';

  const primary = argbFromHex(KEY.primary);
  for (const [name, hex] of Object.entries(SEMANTIC)) {
    const tones = TonalPalette.fromInt(Blend.harmonize(argbFromHex(hex), primary));
    const t = (tone) => hexFromArgb(tones.tone(tone)).toUpperCase();
    const cap = name[0].toUpperCase() + name.slice(1);
    out[name] = t(isDark ? 80 : 40);
    out[`on${cap}`] = t(isDark ? 20 : 100);
    out[`${name}Container`] = t(isDark ? 30 : 90);
    out[`on${cap}Container`] = t(isDark ? 90 : 10);
  }
  return out;
}

// WCAG contrast, for picking glyph tones below (tests/contrast.test.ts checks the result).
const rgb = (argb) => [16, 8, 0].map((shift) => ((argb >> shift) & 255) / 255);
const luminance = (argb) => {
  const [r, g, b] = rgb(argb).map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const contrast = (a, b) => { const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x); return (hi + 0.05) / (lo + 0.05); };
const blend = (fg, alpha, bg) => {
  const [f, b] = [rgb(fg), rgb(bg)];
  const [r, g, bl] = f.map((c, i) => Math.round((c * alpha + b[i] * (1 - alpha)) * 255));
  return (0xff << 24) | (r << 16) | (g << 8) | bl;
};

/**
 * Per identity: `color` (the fixed hue, for bars and accents), `soft` (a wash of it, as its own
 * 8-digit token - dark surfaces need more of the hue or it reads muddy) and `ink`, the same hue and
 * chroma at the nearest tone that reads at 4.5:1 on that wash over a list row, for tile glyphs.
 */
const identities = (table, isDark, row) => Object.fromEntries(Object.entries(table).map(([key, [l, d]]) => {
  const color = (isDark ? d : l).toUpperCase();
  const alpha = isDark ? 0x52 / 255 : 0x33 / 255;
  const wash = blend(argbFromHex(color), alpha, argbFromHex(row));
  const hct = Hct.fromInt(argbFromHex(color));
  let tone = hct.tone;
  let ink = argbFromHex(color);
  while (contrast(ink, wash) < 4.5 && tone > 0 && tone < 100) {
    tone += isDark ? 1 : -1;
    ink = Hct.from(hct.hue, hct.chroma, tone).toInt();
  }
  const soft = `${color}${(isDark ? 0x52 : 0x33).toString(16).toUpperCase()}`;
  return [key, { color, soft, ink: hexFromArgb(ink).toUpperCase() }];
}));

// ---- Brand: the mark's own colours, read from the icon master so the two cannot drift ----------
const master = readFileSync(join(root, 'assets', 'brand', 'icon-master.svg'), 'utf8');
const stops = (id) => [...master.match(new RegExp(`id="${id}"[^>]*>([\\s\\S]*?)</(linear|radial)Gradient>`))[1]
  .matchAll(/stop-color="(#[0-9A-Fa-f]{6})"/g)].map((m) => m[1].toUpperCase());
const [markStart, markEnd] = stops('mark');
const [orbLight, orbDark] = stops('orb');
const [night0, night1] = stops('bg');
const brand = { markStart, markEnd, orbLight, orbDark, violet: night0, night: night1, white: '#FFFFFF', key: KEY.primary };

const light = scheme(false);
const dark = scheme(true);
const body = {
  light,
  dark,
  categoryHues: { light: identities(CATEGORY, false, light.surfaceContainer), dark: identities(CATEGORY, true, dark.surfaceContainer) },
  paymentHues: { light: identities(PAYMENT, false, light.surfaceContainer), dark: identities(PAYMENT, true, dark.surfaceContainer) },
  brand,
};

const file = join(root, 'src', 'theme', 'palette.generated.ts');
writeFileSync(file, `// Generated by scripts/generate-palette.mjs (npm run palette). Do not edit by hand.
// Key colours: primary ${KEY.primary}, secondary ${KEY.secondary}, tertiary ${KEY.tertiary}, neutral ${KEY.neutral}.
// Expressive variant, 2025 spec. No imports, so node --test can read it (tests/contrast.test.ts).

${Object.entries(body).map(([name, value]) => `export const ${name} = ${JSON.stringify(value, null, 2)} as const;`).join('\n\n')}
`);
console.log(`wrote ${file}`);

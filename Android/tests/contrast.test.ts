import assert from 'node:assert/strict';
import { test } from 'node:test';
import { categoryHues, dark, light, paymentHues } from '../src/theme/palette.generated.ts';

// WCAG 2.x relative luminance and contrast ratio. Colours are "#RRGGBB" or "#RRGGBBAA"; a
// translucent one is composited over the background it sits on first.
const channels = (hex: string) => [1, 3, 5, 7].map((i) => parseInt(hex.slice(i, i + 2) || 'ff', 16) / 255);
const over = (fg: string, bg: string) => {
  const [r, g, b, a] = channels(fg);
  const [br, bgG, bb] = channels(bg);
  return [r * a + br * (1 - a), g * a + bgG * (1 - a), b * a + bb * (1 - a)];
};
const luminance = (rgb: number[]) => {
  const [r, g, b] = rgb.map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const ratio = (fg: number[], bg: number[]) => {
  const [hi, lo] = [luminance(fg), luminance(bg)].sort((a, b) => b - a);
  return (hi + 0.05) / (lo + 0.05);
};
const contrast = (fg: string, bg: string) => ratio(over(fg, bg), over(bg, '#FFFFFF'));

const BODY = 4.5;
const LARGE = 3; // large text (24px+, or 18.66px+ bold) and meaningful graphics

const SURFACES = ['surface', 'surfaceContainerLowest', 'surfaceContainerLow', 'surfaceContainer', 'surfaceContainerHigh', 'surfaceContainerHighest'] as const;

/** [text role, background role, minimum]. Every pairing the UI actually draws text with. */
const pairs: [string, string, number][] = [
  ['onPrimary', 'primary', BODY], ['onPrimaryContainer', 'primaryContainer', BODY],
  ['onSecondary', 'secondary', BODY], ['onSecondaryContainer', 'secondaryContainer', BODY],
  ['onTertiary', 'tertiary', BODY], ['onTertiaryContainer', 'tertiaryContainer', BODY],
  ['onError', 'error', BODY], ['onErrorContainer', 'errorContainer', BODY],
  ['onBackground', 'background', BODY], ['inverseOnSurface', 'inverseSurface', BODY],
  ['onRenewalWarning', 'renewalWarning', BODY], ['onRenewalWarningContainer', 'renewalWarningContainer', BODY],
  ['onRenewalSoon', 'renewalSoon', BODY], ['onRenewalSoonContainer', 'renewalSoonContainer', BODY],
  ['onSuccess', 'success', BODY], ['onSuccessContainer', 'successContainer', BODY],
  // Snackbar action on the inverse surface.
  ['inversePrimary', 'inverseSurface', BODY],
  // Dashboard hero period switcher on its primaryContainer card: the selected segment inverts to
  // onPrimaryContainer (a boundary, 3:1) carrying primaryContainer text (4.5:1).
  ['onPrimaryContainer', 'primaryContainer', LARGE], ['primaryContainer', 'onPrimaryContainer', BODY],
  // Unselected segments on that card: their fill against the card, and their label on the fill.
  ['onSurfaceVariant', 'surfaceContainerHighest', BODY],
  ...SURFACES.flatMap((s): [string, string, number][] => [
    ['onSurface', s, BODY], ['onSurfaceVariant', s, BODY],
    // Accent text on plain surfaces: section headers, links, field errors, overdue copy.
    ['primary', s, BODY], ['error', s, BODY], ['renewalWarning', s, BODY],
    // Non-text: focus rings, the outlined button border, progress tracks.
    ['outline', s, LARGE],
  ]),
];

for (const [name, scheme] of [['light', light], ['dark', dark]] as const) {
  test(`${name} scheme: text roles meet WCAG AA on their backgrounds`, () => {
    const roles = scheme as Record<string, string>;
    const failures = pairs
      .map(([fg, bg, min]) => ({ fg, bg, min, got: contrast(roles[fg], roles[bg]) }))
      .filter(({ got, min }) => got < min)
      .map(({ fg, bg, min, got }) => `${fg} on ${bg}: ${got.toFixed(2)} < ${min}`);
    assert.deepEqual(failures, []);
  });

  test(`${name} scheme: breakdown bars stand out from their track`, () => {
    // Dashboard bars draw each identity's ink (its hue at a readable tone) on the
    // surfaceContainerHighest track (3:1, non-text). The raw light-theme hues fail this.
    const track = (scheme as Record<string, string>).surfaceContainerHighest;
    const failures = Object.entries({ ...categoryHues[name], ...paymentHues[name] })
      .map(([key, { ink }]) => ({ key, got: contrast(ink, track) }))
      .filter(({ got }) => got < LARGE)
      .map(({ key, got }) => `${key}: ${got.toFixed(2)} < ${LARGE}`);
    assert.deepEqual(failures, []);
  });

  test(`${name} scheme: hero period switcher's unselected labels read on their wash`, () => {
    // Unselected segments on the primaryContainer card (ButtonGroup onPrimaryContainer) are a wash
    // over the card - onPrimaryContainer at 12% in light, black at 20% in dark - labelled in
    // onPrimaryContainer.
    const s = scheme as Record<string, string>;
    const wash = over(name === 'dark' ? `${s.shadow}33` : `${s.onPrimaryContainer}1F`, s.primaryContainer);
    const got = ratio(over(s.onPrimaryContainer, '#FFFFFF'), wash);
    assert.ok(got >= BODY, `${got.toFixed(2)} < ${BODY}`);
  });

  test(`${name} scheme: category and payment identities read on their own wash`, () => {
    // Tiles draw a letter or icon in the identity's ink on its translucent wash over a list row.
    const row = (scheme as Record<string, string>).surfaceContainer;
    const failures = Object.entries({ ...categoryHues[name], ...paymentHues[name] })
      .map(([key, { ink, soft }]) => ({ key, got: ratio(over(ink, row), over(soft, row)) }))
      .filter(({ got }) => got < BODY)
      .map(({ key, got }) => `${key}: ${got.toFixed(2)} < ${BODY}`);
    assert.deepEqual(failures, []);
  });
}

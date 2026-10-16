import Storage from 'expo-sqlite/kv-store';
import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { Platform, PlatformColor, useColorScheme, type ColorValue, type TextStyle } from 'react-native';
import type { PaymentSourceType } from '@/api';
import type { IconName } from '@/ui';
import * as generated from './palette.generated';

/**
 * Material 3 Expressive tonal palette, generated from the SubVora key colours (violet #7B5CFF,
 * cyan #38C6FF, amber-orange #FF8A4C, midnight #0B0A1F) by scripts/generate-palette.mjs. Surfaces,
 * status colours and semantic roles are fixed; with wallpaper colours on (Android 12+, off by
 * default) the accent roles follow the wallpaper instead (see ThemeProvider).
 */
type Role = keyof typeof generated.light;
export type Palette = { [K in Role | Alias]: ColorValue };

/** Older names kept working, and roles that share a family rather than inventing a hue. */
type Alias = 'warningContainer' | 'onWarningContainer' | 'spendUp' | 'spendUpContainer' | 'onSpendUpContainer'
  | 'spendDown' | 'spendDownContainer' | 'onSpendDownContainer';
const withAliases = (p: typeof generated.light | typeof generated.dark): Record<Role | Alias, string> => ({
  ...p,
  // The warning banner (offline, cached data) shares the "renewal soon" amber.
  warningContainer: p.renewalSoonContainer, onWarningContainer: p.onRenewalSoonContainer,
  // Spend going up is a warning, never an error: red stays reserved for errors.
  spendUp: p.renewalWarning, spendUpContainer: p.renewalWarningContainer, onSpendUpContainer: p.onRenewalWarningContainer,
  spendDown: p.success, spendDownContainer: p.successContainer, onSpendDownContainer: p.onSuccessContainer,
});
const light = withAliases(generated.light);
const dark = withAliases(generated.dark);

/** The mark's own colours (from the icon master), for SubVoraMark, the splash and system bars. */
export const brand = generated.brand;

/** Android 12+ wallpaper tones for the accent roles. Native colour objects: never concatenate them. */
const dynamicAccents = (isDark: boolean): Partial<Palette> => {
  const tone = (palette: string, l: number, d: number) => PlatformColor(`@android:color/system_${palette}_${isDark ? d : l}`);
  return {
    primary: tone('accent1', 600, 200), onPrimary: tone('accent1', 0, 800),
    primaryContainer: tone('accent1', 100, 700), onPrimaryContainer: tone('accent1', 900, 100),
    secondaryContainer: tone('accent2', 100, 700), onSecondaryContainer: tone('accent2', 900, 100),
    tertiaryContainer: tone('accent3', 100, 700), onTertiaryContainer: tone('accent3', 900, 100),
  };
};

export const supportsDynamicColor = Platform.OS === 'android' && Number(Platform.Version) >= 31;

/**
 * Plus Jakarta Sans (OFL, assets/fonts), embedded at build time by the expo-font config plugin.
 * Android registers each file as its own family, so every style names the weight's family and
 * never sets fontWeight (which would make Android synthesise bold on top).
 */
const font = {
  regular: 'PlusJakartaSans-Regular',
  medium: 'PlusJakartaSans-Medium',
  semiBold: 'PlusJakartaSans-SemiBold',
  bold: 'PlusJakartaSans-Bold',
  extraBold: 'PlusJakartaSans-ExtraBold',
};
const style = (fontSize: number, lineHeight: number, fontFamily: string, letterSpacing = 0) => ({ fontSize, lineHeight, fontFamily, letterSpacing });

/**
 * Full M3 type scale. Display and headline are the Expressive emphasised weights (700-800, slightly
 * tight); title 500, body 400, labels 600. Sizes are in sp, so the system font scale applies.
 */
const base = {
  displayLarge: style(57, 64, font.extraBold, -1),
  displayMedium: style(45, 52, font.extraBold, -0.75),
  displaySmall: style(36, 44, font.bold, -0.5),
  headlineLarge: style(32, 40, font.bold, -0.5),
  headlineMedium: style(28, 36, font.bold, -0.25),
  headlineSmall: style(24, 32, font.bold, -0.25),
  titleLarge: style(22, 28, font.medium),
  titleMedium: style(16, 24, font.medium, 0.15),
  titleSmall: style(14, 20, font.medium, 0.1),
  bodyLarge: style(16, 24, font.regular, 0.3),
  bodyMedium: style(14, 20, font.regular, 0.25),
  bodySmall: style(12, 16, font.regular, 0.4),
  labelLarge: style(14, 20, font.semiBold, 0.1),
  labelMedium: style(12, 16, font.semiBold, 0.5),
  labelSmall: style(11, 16, font.semiBold, 0.5),
};

/** M3 Expressive emphasised styles: the same size one weight heavier, for the line that matters. */
const HEAVIER: Record<string, string> = {
  [font.regular]: font.medium, [font.medium]: font.bold, [font.semiBold]: font.bold, [font.bold]: font.extraBold, [font.extraBold]: font.extraBold,
};
type Emphasized = { [K in keyof typeof base as `${K}Emphasized`]: TextStyle };
const emphasized = Object.fromEntries(
  Object.entries(base).map(([name, s]) => [`${name}Emphasized`, { ...s, fontFamily: HEAVIER[s.fontFamily] }]),
) as Emphasized;

/** Money: tabular figures (Plus Jakarta Sans has `tnum`), so amounts line up and don't jitter. */
const tabular: TextStyle['fontVariant'] = ['tabular-nums'];
const amounts = {
  amountDisplay: { ...base.displayLarge, fontVariant: tabular },
  amountHeadline: { ...base.headlineSmall, fontVariant: tabular },
  amountTitle: { ...base.titleMedium, fontFamily: font.semiBold, fontVariant: tabular },
  amountLabel: { ...base.labelLarge, fontVariant: tabular },
};

export const typescale = { ...base, ...emphasized, ...amounts };
export type TypeVariant = keyof typeof typescale;

/**
 * Expressive shape scale. Replaces the old xs..xxl names (migrated in one go): cards use `large`,
 * sheets, dialogs and the dashboard hero `extraLarge`. `listInner`/`listGap` are the segmented
 * list's inner corners and the gap between its rows.
 */
export const shape = { extraSmall: 8, small: 12, medium: 16, large: 24, extraLarge: 32, full: 999, listInner: 6, listGap: 2 };

/** Spacing on a 4dp grid (2dp for hairline gaps). */
export const space = { xxs: 2, xs: 4, sm: 8, md: 12, lg: 16, xl: 20, xxl: 24, xxxl: 32, huge: 48 };

/**
 * M3 Expressive spring tokens (MotionScheme.expressive: damping ratio + stiffness), converted to
 * React Native Animated.spring's physical damping for a unit mass: c = 2ζ√k. Spatial springs move
 * and resize things and may overshoot; effects springs (colour, opacity) never do.
 */
const spring = (dampingRatio: number, stiffness: number) => ({ stiffness, damping: 2 * dampingRatio * Math.sqrt(stiffness), mass: 1 });
export const motion = {
  spatialFast: spring(0.6, 800),
  spatialDefault: spring(0.8, 380),
  spatialSlow: spring(0.8, 200),
  effectsFast: spring(1, 3800),
  effectsDefault: spring(1, 1600),
  effectsSlow: spring(1, 800),
  /** Plain fade used instead of any spring when the user asks for reduced motion. */
  fadeMs: 150,
};

// ---- Category and payment-source identity ----------------------------------------------------
// Colour and glyph follow the category, never its rank. User-created and unknown categories fall
// to neutral - an invented hue or icon would be wrong more often than blank. Red stays out of the
// set: it is the danger colour.

const CATEGORY_ICON: Record<string, IconName> = {
  entertainment: 'movie-open-outline', food: 'silverware-fork-knife', fitness: 'dumbbell',
  utilities: 'lightning-bolt-outline', travel: 'airplane', finance: 'cash-multiple',
  productivity: 'briefcase-outline', other: 'inbox-outline',
};
const PAYMENT_ICON: Record<PaymentSourceType, IconName> = {
  Card: 'credit-card-outline', BankAccount: 'bank-outline', Wallet: 'wallet-outline', Other: 'cash',
};

export interface Identity { color: string; soft: string; ink: string; icon: IconName }

// ---- Provider --------------------------------------------------------------------------------

export type ThemeMode = 'system' | 'light' | 'dark';
const PREFS_KEY = 'prefs:appearance';

/**
 * Device-local, synchronous read so the first frame is already in the chosen appearance.
 * Wallpaper colours are stored as `wallpaper`, off unless chosen: the old `dynamic` flag defaulted
 * on and was saved with every theme change, so it cannot tell a choice from the old default.
 */
function readPrefs(): { mode: ThemeMode; wallpaper: boolean } {
  try {
    const { mode = 'system', wallpaper = false } = JSON.parse(Storage.getItemSync(PREFS_KEY) ?? '{}');
    return { mode, wallpaper };
  } catch {
    return { mode: 'system', wallpaper: false };
  }
}

interface Theme {
  colors: Palette;
  /** Plain-string surfaces for APIs that cannot take a native colour (system UI, status bar). */
  surfaceHex: string;
  isDark: boolean;
  mode: ThemeMode;
  dynamic: boolean;
  setMode: (mode: ThemeMode) => void;
  setDynamic: (dynamic: boolean) => void;
  category: (name?: string | null) => Identity;
  payment: (type: PaymentSourceType) => Identity;
}

const ThemeContext = createContext<Theme | null>(null);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const system = useColorScheme();
  const [prefs, setPrefs] = useState(readPrefs);

  const update = useCallback((next: Partial<ReturnType<typeof readPrefs>>) => setPrefs((current) => {
    const merged = { ...current, ...next };
    Storage.setItemAsync(PREFS_KEY, JSON.stringify(merged)).catch(() => undefined);
    return merged;
  }), []);

  const isDark = prefs.mode === 'system' ? system === 'dark' : prefs.mode === 'dark';

  const value = useMemo<Theme>(() => {
    const base = isDark ? dark : light;
    const scheme = isDark ? 'dark' : 'light';
    const categories = generated.categoryHues[scheme];
    const payments = generated.paymentHues[scheme];
    return {
      colors: { ...base, ...(prefs.wallpaper && supportsDynamicColor ? dynamicAccents(isDark) : {}) },
      surfaceHex: base.surface,
      isDark,
      mode: prefs.mode,
      dynamic: prefs.wallpaper,
      setMode: (mode) => update({ mode }),
      setDynamic: (wallpaper) => update({ wallpaper }),
      category: (name) => {
        const key = (name ?? '').trim().toLowerCase();
        const known = key in CATEGORY_ICON;
        return { ...categories[known ? key as keyof typeof categories : 'neutral'], icon: known ? CATEGORY_ICON[key] : 'shape-outline' };
      },
      payment: (t) => {
        const key = t in PAYMENT_ICON ? t : 'Other';
        return { ...payments[key], icon: PAYMENT_ICON[key] };
      },
    };
  }, [isDark, prefs, update]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const theme = useContext(ThemeContext);
  if (!theme) throw new Error('useTheme outside ThemeProvider');
  return theme;
}

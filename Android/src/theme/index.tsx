import Storage from 'expo-sqlite/kv-store';
import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { Platform, PlatformColor, useColorScheme, type ColorValue, type TextStyle } from 'react-native';
import type { PaymentSourceType } from '@/api';
import type { IconName } from '@/ui';

/**
 * Material 3 Expressive tonal palette, seeded from the MAUI app's violet (#512BD4). Surfaces and
 * status colours are fixed; the accent roles follow the wallpaper on Android 12+ unless the user
 * turns dynamic colour off (see ThemeProvider).
 */
const light = {
  primary: '#5A3FC0', onPrimary: '#FFFFFF', primaryContainer: '#E6DEFF', onPrimaryContainer: '#1B0062',
  secondary: '#615B71', secondaryContainer: '#E7DEF8', onSecondaryContainer: '#1D192B',
  tertiary: '#7E525E', tertiaryContainer: '#FFD9E1', onTertiaryContainer: '#31101B',
  error: '#BA1A1A', onError: '#FFFFFF', errorContainer: '#FFDAD6', onErrorContainer: '#410002',
  success: '#1B6C3F', successContainer: '#A3F5BC', onSuccessContainer: '#00210E',
  warningContainer: '#FFDEA6', onWarningContainer: '#271900',
  surface: '#FDF8FF', onSurface: '#1C1B20', onSurfaceVariant: '#48454F',
  surfaceContainerLowest: '#FFFFFF', surfaceContainerLow: '#F7F2FA', surfaceContainer: '#F1ECF4',
  surfaceContainerHigh: '#EBE6EE', surfaceContainerHighest: '#E6E1E9',
  outline: '#79757F', outlineVariant: '#CAC4D0',
  inverseSurface: '#313036', inverseOnSurface: '#F4EFF7', scrim: 'rgba(0,0,0,0.4)',
};

export type Palette = { [K in keyof typeof light]: ColorValue };

const dark: typeof light = {
  primary: '#C9BFFF', onPrimary: '#2E128F', primaryContainer: '#4526A7', onPrimaryContainer: '#E6DEFF',
  secondary: '#CBC2DB', secondaryContainer: '#4A4458', onSecondaryContainer: '#E7DEF8',
  tertiary: '#EFB8C6', tertiaryContainer: '#643B47', onTertiaryContainer: '#FFD9E1',
  error: '#FFB4AB', onError: '#690005', errorContainer: '#93000A', onErrorContainer: '#FFDAD6',
  success: '#88D9A1', successContainer: '#00522B', onSuccessContainer: '#A3F5BC',
  warningContainer: '#5C4200', onWarningContainer: '#FFDEA6',
  surface: '#141318', onSurface: '#E6E1E9', onSurfaceVariant: '#CAC4D0',
  surfaceContainerLowest: '#0F0D13', surfaceContainerLow: '#1C1B20', surfaceContainer: '#201F24',
  surfaceContainerHigh: '#2B292F', surfaceContainerHighest: '#36343A',
  outline: '#938F99', outlineVariant: '#48454F',
  inverseSurface: '#E6E1E9', inverseOnSurface: '#313036', scrim: 'rgba(0,0,0,0.6)',
};

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

/** M3 type scale, emphasised weights for the Expressive display/headline styles. */
export const typescale = {
  displayLarge: { fontSize: 52, lineHeight: 60, fontWeight: '800', letterSpacing: -1 },
  displaySmall: { fontSize: 36, lineHeight: 44, fontWeight: '700', letterSpacing: -0.5 },
  headlineLarge: { fontSize: 32, lineHeight: 40, fontWeight: '700' },
  headlineSmall: { fontSize: 24, lineHeight: 32, fontWeight: '600' },
  titleLarge: { fontSize: 22, lineHeight: 28, fontWeight: '600' },
  titleMedium: { fontSize: 16, lineHeight: 24, fontWeight: '600', letterSpacing: 0.15 },
  bodyLarge: { fontSize: 16, lineHeight: 24, fontWeight: '400', letterSpacing: 0.3 },
  bodyMedium: { fontSize: 14, lineHeight: 20, fontWeight: '400', letterSpacing: 0.25 },
  labelLarge: { fontSize: 14, lineHeight: 20, fontWeight: '600', letterSpacing: 0.1 },
  labelMedium: { fontSize: 12, lineHeight: 16, fontWeight: '600', letterSpacing: 0.5 },
} satisfies Record<string, TextStyle>;
export type TypeVariant = keyof typeof typescale;

/** Expressive shape scale: big outer radii, small inner ones where list items meet. */
export const shape = { xs: 4, sm: 8, md: 12, lg: 16, xl: 28, xxl: 36, full: 999 };

// ---- Category and payment-source identity ----------------------------------------------------
// Colour and glyph follow the category, never its rank. User-created and unknown categories fall
// to neutral - an invented hue or icon would be wrong more often than blank. Red stays out of the
// set: it is the danger colour.

const CATEGORY: Record<string, { light: string; dark: string; icon: IconName }> = {
  entertainment: { light: '#2a78d6', dark: '#3987e5', icon: 'movie-open-outline' },
  food: { light: '#eb6834', dark: '#d95926', icon: 'silverware-fork-knife' },
  fitness: { light: '#1baf7a', dark: '#199e70', icon: 'dumbbell' },
  utilities: { light: '#eda100', dark: '#c98500', icon: 'lightning-bolt-outline' },
  travel: { light: '#e87ba4', dark: '#d55181', icon: 'airplane' },
  finance: { light: '#008300', dark: '#008300', icon: 'cash-multiple' },
  productivity: { light: '#4a3aa7', dark: '#9085e9', icon: 'briefcase-outline' },
  other: { light: '#8A8A99', dark: '#767686', icon: 'inbox-outline' },
};
const NEUTRAL = { light: '#8A8A99', dark: '#767686', icon: 'shape-outline' as IconName };

const PAYMENT: Record<PaymentSourceType, { light: string; dark: string; icon: IconName }> = {
  Card: { light: '#1F6FEB', dark: '#5AA0FF', icon: 'credit-card-outline' },
  BankAccount: { light: '#0E8A80', dark: '#3FD0C3', icon: 'bank-outline' },
  Wallet: { light: '#7A3FD0', dark: '#A98CF0', icon: 'wallet-outline' },
  Other: { light: '#8A8A99', dark: '#9C9CAB', icon: 'cash' },
};

/** A wash behind dark ink; dark surfaces need more of the hue or it reads muddy. */
const soft = (hex: string, isDark: boolean) => `${hex}${isDark ? '52' : '33'}`;

export interface Identity { color: string; soft: string; icon: IconName }

const identity = (entry: { light: string; dark: string; icon: IconName }, isDark: boolean): Identity => {
  const color = isDark ? entry.dark : entry.light;
  return { color, soft: soft(color, isDark), icon: entry.icon };
};

// ---- Provider --------------------------------------------------------------------------------

export type ThemeMode = 'system' | 'light' | 'dark';
const PREFS_KEY = 'prefs:appearance';

/** Device-local, synchronous read so the first frame is already in the chosen appearance. */
function readPrefs(): { mode: ThemeMode; dynamic: boolean } {
  try {
    return { mode: 'system', dynamic: true, ...JSON.parse(Storage.getItemSync(PREFS_KEY) ?? '{}') };
  } catch {
    return { mode: 'system', dynamic: true };
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
    return {
      colors: { ...base, ...(prefs.dynamic && supportsDynamicColor ? dynamicAccents(isDark) : {}) },
      surfaceHex: base.surface,
      isDark,
      mode: prefs.mode,
      dynamic: prefs.dynamic,
      setMode: (mode) => update({ mode }),
      setDynamic: (dynamic) => update({ dynamic }),
      category: (name) => identity(CATEGORY[(name ?? '').trim().toLowerCase()] ?? NEUTRAL, isDark),
      payment: (t) => identity(PAYMENT[t] ?? PAYMENT.Other, isDark),
    };
  }, [isDark, prefs, update]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const theme = useContext(ThemeContext);
  if (!theme) throw new Error('useTheme outside ThemeProvider');
  return theme;
}

import { useEffect, useId, useState } from 'react';
import { Animated, Easing, StyleSheet, useColorScheme, View, type ColorValue, type StyleProp, type ViewStyle } from 'react-native';
import Svg, { Circle, Defs, LinearGradient, Path, Polyline, RadialGradient, Stop } from 'react-native-svg';
import { useReducedMotion } from '@/lib/motion';
import { brand, lightSurface, motion, useTheme } from '@/theme';

// The Renewal Ring's geometry, in the icon master's 1024 units (assets/brand/icon-master.svg): ring
// centred at 512, r 290, stroke 84; the arc runs clockwise from 12 o'clock; the orb sits in the gap.
const C = 512;
const R = 290;
const STROKE = 84;
const CIRCUMFERENCE = 2 * Math.PI * R;
/** Cropped to the ring plus its stroke - no background, no glow room. */
const VIEW_BOX = '172 172 680 680';

const point = (turns: number) => ({ x: C + R * Math.sin(turns * 2 * Math.PI), y: C - R * Math.cos(turns * 2 * Math.PI) });

/** Arc from 12 o'clock, clockwise, covering `progress` of the circle. */
function arcPath(progress: number) {
  const end = point(progress);
  return `M${C} ${C - R} A${R} ${R} 0 ${progress > 0.5 ? 1 : 0} 1 ${end.x.toFixed(2)} ${end.y.toFixed(2)}`;
}

type Tone = 'brand' | 'brandOnLight' | 'themed' | 'mono';

interface RingProps {
  size: number;
  /** Share of the circle the arc covers; the icon's 0.75 by default. */
  progress?: number;
  /**
   * brand: the icon's own colours, for midnight backgrounds. brandOnLight: the same mark for light
   * backgrounds (violet arc, midnight V), as on the light splash. themed: primary/secondary on
   * surfaces. mono: one colour.
   */
  tone?: Tone;
  color?: ColorValue;
  /** Replaces the V, e.g. a figure or an icon. */
  center?: React.ReactNode;
  orb?: boolean;
  /** Animated styles for the moving layers: the arc with the orb riding on it, and the orb alone. */
  arcStyle?: StyleProp<ViewStyle>;
  orbStyle?: StyleProp<ViewStyle>;
}

/** The ring of the SubVora mark, as a component: the base of the mark, the loader, the dashboard ring and empty states. */
export function RingMark({ size, progress = 0.75, tone = 'themed', color, center, orb = true, arcStyle, orbStyle }: RingProps) {
  const { hex, colors, wallpaperActive } = useTheme();
  const id = useId().replace(/[^\w-]/g, '');
  const p = Math.max(0, Math.min(1, progress));
  const ink = tone === 'brand' ? brand.white : tone === 'brandOnLight' ? brand.night : tone === 'mono' ? (color ?? hex.onSurface) : hex.onSurface;
  const [from, to] = tone === 'brand' ? [brand.markStart, brand.markEnd] : tone === 'brandOnLight' ? [brand.key, brand.violet] : [hex.secondary, hex.primary];
  // Gradient stops only take plain colours, so with wallpaper colours on a themed ring is drawn in
  // the wallpaper's solid primary (strokes and fills do take native colours) rather than in brand
  // violet next to wallpaper-tinted buttons.
  const live = tone === 'themed' && wallpaperActive;
  const stroke = tone === 'mono' ? ink : live ? colors.primary : `url(#arc${id})`;
  const orbFill = tone === 'mono' ? ink : live ? colors.tertiaryContainer : `url(#orb${id})`;
  const orbAt = point(p + (1 - p) / 2);
  const layer = StyleSheet.absoluteFill;

  return (
    <View style={{ width: size, height: size }} accessible={false}>
      <Svg viewBox={VIEW_BOX} width={size} height={size} style={layer}>
        {tone !== 'mono' ? (
          <Circle cx={C} cy={C} r={R} fill="none" stroke={ink} strokeOpacity={0.1} strokeWidth={STROKE} />
        ) : null}
        {center === undefined ? (
          <>
            {tone !== 'mono' ? <Circle cx={C} cy={C} r={168} fill={ink} fillOpacity={0.06} stroke={ink} strokeOpacity={0.16} strokeWidth={3} /> : null}
            <Polyline points="448,474 512,570 576,474" fill="none" stroke={ink} strokeWidth={54} strokeLinecap="round" strokeLinejoin="round" />
          </>
        ) : null}
      </Svg>
      <Animated.View style={[layer, arcStyle]}>
        <Svg viewBox={VIEW_BOX} width={size} height={size} style={layer}>
          <Defs>
            {/* User space, matching the master's gradient on the full ring, so a short arc keeps the same colours. */}
            <LinearGradient id={`arc${id}`} gradientUnits="userSpaceOnUse" x1="309" y1="280" x2="715" y2="744">
              <Stop offset="0" stopColor={from} />
              <Stop offset="1" stopColor={to} />
            </LinearGradient>
          </Defs>
          {p >= 0.999 ? (
            <Circle cx={C} cy={C} r={R} fill="none" stroke={stroke} strokeWidth={STROKE} />
          ) : p > 0 ? (
            <Path d={arcPath(p)} fill="none" stroke={stroke} strokeWidth={STROKE} strokeLinecap="round" />
          ) : null}
        </Svg>
        {orb && p < 0.999 ? (
          <Animated.View style={[layer, orbStyle]}>
            <Svg viewBox={VIEW_BOX} width={size} height={size} style={layer}>
              <Defs>
                <RadialGradient id={`orb${id}`} cx="0.35" cy="0.3" r="0.75">
                  <Stop offset="0" stopColor={brand.orbLight} />
                  <Stop offset="1" stopColor={brand.orbDark} />
                </RadialGradient>
              </Defs>
              <Circle cx={orbAt.x} cy={orbAt.y} r={54} fill={orbFill} />
            </Svg>
          </Animated.View>
        ) : null}
      </Animated.View>
      {center !== undefined ? <View style={[layer, { alignItems: 'center', justifyContent: 'center' }]}>{center}</View> : null}
    </View>
  );
}

/** The SubVora mark without its background. `monochrome` draws ring, orb and V in one colour. */
export function SubVoraMark({ size = 48, monochrome, color }: { size?: number; monochrome?: boolean; color?: ColorValue }) {
  return <RingMark size={size} tone={monochrome ? 'mono' : 'brand'} color={color} />;
}

// ---- Loaders ---------------------------------------------------------------------------------

/** A value that loops 0 -> 1 at constant speed: spinning is the one motion springs cannot express. */
function useSpin(periodMs: number, enabled: boolean) {
  const [value] = useState(() => new Animated.Value(0));
  useEffect(() => {
    if (!enabled) return;
    const loop = Animated.loop(Animated.timing(value, { toValue: 1, duration: periodMs, easing: Easing.linear, useNativeDriver: true }));
    loop.start();
    return () => loop.stop();
  }, [value, periodMs, enabled]);
  return value.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] });
}

/** Opacity breathing between 1 and `low` on the slow effects spring (no overshoot). */
function usePulse(low: number) {
  const [value] = useState(() => new Animated.Value(1));
  useEffect(() => {
    const to = (toValue: number) => Animated.spring(value, { toValue, ...motion.effectsSlow, useNativeDriver: true });
    const loop = Animated.loop(Animated.sequence([to(low), Animated.delay(200), to(1), Animated.delay(200)]));
    loop.start();
    return () => loop.stop();
  }, [value, low]);
  return value;
}

/**
 * Brand loader for full-screen and first-load waits: the arc sweeps round, the orb pulses, the V
 * stays still. With reduced motion it is a still ring with a gentle opacity pulse.
 */
export function SubVoraLoader({ size = 72, label = 'Loading', tone = 'themed' }: { size?: number; label?: string; tone?: Tone }) {
  const reduced = useReducedMotion();
  const rotate = useSpin(1400, !reduced);
  const pulse = usePulse(reduced ? 0.6 : 0.45);
  return (
    <Animated.View accessible accessibilityRole="progressbar" accessibilityLabel={label} style={reduced ? { opacity: pulse } : undefined}>
      <RingMark size={size} tone={tone} arcStyle={reduced ? undefined : { transform: [{ rotate }] }} orbStyle={reduced ? undefined : { opacity: pulse }} />
    </Animated.View>
  );
}

/**
 * Local stand-in for Compose's LoadingIndicator, for short or indeterminate waits (inline in
 * buttons too). Compose morphs between polygon shapes; this morphs a rounded square into a circle
 * and back while it turns. Reduced motion: a still circle with an opacity pulse.
 */
export function ExpressiveLoadingIndicator({ size = 40, color, label = 'Loading' }: { size?: number; color?: ColorValue; label?: string }) {
  const { colors } = useTheme();
  const reduced = useReducedMotion();
  const rotate = useSpin(1600, !reduced);
  const pulse = usePulse(0.5);
  const [morph] = useState(() => new Animated.Value(0));
  useEffect(() => {
    if (reduced) return;
    const to = (toValue: number) => Animated.spring(morph, { toValue, ...motion.spatialDefault, useNativeDriver: false });
    const loop = Animated.loop(Animated.sequence([to(1), Animated.delay(120), to(0), Animated.delay(120)]));
    loop.start();
    return () => loop.stop();
  }, [morph, reduced]);

  const shapeSize = size * 0.72;
  return (
    <Animated.View
      accessible accessibilityRole="progressbar" accessibilityLabel={label}
      style={{ width: size, height: size, alignItems: 'center', justifyContent: 'center', opacity: reduced ? pulse : 1, transform: reduced ? [] : [{ rotate }] }}
    >
      <Animated.View style={{
        width: shapeSize, height: shapeSize, backgroundColor: color ?? colors.primary,
        borderRadius: reduced ? shapeSize / 2 : morph.interpolate({ inputRange: [0, 1], outputRange: [shapeSize * 0.24, shapeSize / 2] }),
        transform: [{ scale: reduced ? 1 : morph.interpolate({ inputRange: [0, 1], outputRange: [1, 0.82] }) }],
      }} />
    </Animated.View>
  );
}

// ---- Launch hand-off -------------------------------------------------------------------------

/**
 * What the native splash looks like right now. It follows the system appearance (not the in-app
 * override), midnight in dark and the light surface in light, so a light-theme cold start no longer
 * flips from midnight to light.
 */
export function useSplashLook() {
  const dark = useColorScheme() === 'dark';
  return dark
    ? { background: brand.night, tone: 'brand' as const, statusBar: 'light' as const }
    : { background: lightSurface, tone: 'brandOnLight' as const, statusBar: 'dark' as const };
}

const AnimatedPath = Animated.createAnimatedComponent(Path);
const GAP = CIRCUMFERENCE / 4;
/** Ring diameter in the Android 12 splash: the 288dp icon with the mark scaled 0.82 (generate-icons.mjs). */
export const SPLASH_MARK_SIZE = Math.round(288 * 0.82 * (680 / 1024));

/**
 * Picks up where the native splash leaves off: same midnight background, same mark in the same
 * place. The arc sweeps the gap closed over the orb, then the whole layer fades into the first
 * route. Reduced motion: just the fade.
 */
export function LaunchHandoff({ onDone }: { onDone: () => void }) {
  const reduced = useReducedMotion();
  const splash = useSplashLook();
  const [sweep] = useState(() => new Animated.Value(0));
  const [fade] = useState(() => new Animated.Value(1));
  useEffect(() => {
    const out = Animated.timing(fade, { toValue: 0, duration: reduced ? motion.fadeMs : 260, useNativeDriver: true });
    const run = reduced
      ? out
      : Animated.sequence([Animated.spring(sweep, { toValue: 1, ...motion.effectsSlow, useNativeDriver: false }), out]);
    run.start(() => onDone());
    return () => run.stop();
  }, [sweep, fade, reduced, onDone]);

  const size = SPLASH_MARK_SIZE;
  return (
    <Animated.View pointerEvents="none" style={[StyleSheet.absoluteFill, { backgroundColor: splash.background, alignItems: 'center', justifyContent: 'center', opacity: fade }]}>
      <View style={{ width: size, height: size }}>
        <RingMark size={size} tone={splash.tone} orbStyle={{ opacity: sweep.interpolate({ inputRange: [0, 0.7, 1], outputRange: [1, 1, 0] }) }} />
        <Svg viewBox={VIEW_BOX} width={size} height={size} style={StyleSheet.absoluteFill}>
          <Defs>
            <LinearGradient id="handoff" gradientUnits="userSpaceOnUse" x1="309" y1="280" x2="715" y2="744">
              <Stop offset="0" stopColor={splash.tone === 'brand' ? brand.markStart : brand.key} />
              <Stop offset="1" stopColor={splash.tone === 'brand' ? brand.markEnd : brand.violet} />
            </LinearGradient>
          </Defs>
          {/* The gap, 9 to 12 o'clock, drawn in as its dash offset runs out. */}
          <AnimatedPath
            d={`M${C - R} ${C} A${R} ${R} 0 0 1 ${C} ${C - R}`} fill="none" stroke="url(#handoff)" strokeWidth={STROKE} strokeLinecap="round"
            strokeDasharray={`${GAP} ${GAP}`} strokeDashoffset={sweep.interpolate({ inputRange: [0, 1], outputRange: [GAP, 0] })}
            opacity={sweep.interpolate({ inputRange: [0, 0.05, 1], outputRange: [0, 1, 1] })}
          />
        </Svg>
      </View>
    </Animated.View>
  );
}

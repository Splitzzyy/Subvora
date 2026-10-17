import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useEffect, useImperativeHandle, useRef, useState } from 'react';
import {
  Animated, Easing, Keyboard, Pressable, Switch, Text as RNText, TextInput, useWindowDimensions, View,
  type ColorValue, type PressableProps, type StyleProp, type TextInputProps, type TextProps, type ViewStyle,
} from 'react-native';
import Svg, { Path } from 'react-native-svg';
import { reduceMotionEnabled, useReducedMotion } from '@/lib/motion';
import { motion, shape, space, typescale, useTheme, type TypeVariant } from '@/theme';
import * as haptics from '@/lib/haptics';
import { useIsFocused } from 'expo-router';
import { ExpressiveLoadingIndicator, RingMark } from './brand';
import { useObstruction } from './obstructions';

export type IconName = React.ComponentProps<typeof MaterialCommunityIcons>['name'];
export const Icon = MaterialCommunityIcons;

/** Minimum touch target (dp). Smaller visuals reach it with hitSlop. */
export const TOUCH = 48;
/** From this window width (tablets, unfolded foldables) the tab bar becomes a navigation rail. */
export const RAIL_MIN_WIDTH = 600;
/** The bottom navigation bar above the system inset: top padding plus one destination. */
export const NAV_BAR_HEIGHT = 12 + 64;

// ---- Text ------------------------------------------------------------------------------------

/**
 * Display and headline sizes are already large, so they scale with the system font size only up to
 * 1.3x (as Android 14's non-linear scaling does): at 200% a 32sp "Subscriptions" split mid-word.
 */
const LARGE_TEXT_CAP = 1.3;

export function Text({ variant = 'bodyLarge', color, style, ...props }: TextProps & { variant?: TypeVariant; color?: ColorValue }) {
  const { colors } = useTheme();
  const { fontScale } = useWindowDimensions();
  const base = typescale[variant];
  // Scaled here rather than by maxFontSizeMultiplier, which caps the glyphs but not the line height.
  const capped = (variant.startsWith('display') || variant.startsWith('headline')) && fontScale > LARGE_TEXT_CAP
    ? { fontSize: (base.fontSize ?? 0) * LARGE_TEXT_CAP, lineHeight: (base.lineHeight ?? 0) * LARGE_TEXT_CAP }
    : null;
  return (
    <RNText
      {...props}
      allowFontScaling={capped ? false : props.allowFontScaling}
      style={[base, capped, { color: color ?? colors.onSurface }, style]}
    />
  );
}

// ---- Press feedback --------------------------------------------------------------------------

interface TapProps extends Omit<PressableProps, 'style' | 'children'> {
  style?: StyleProp<ViewStyle>;
  children: React.ReactNode;
  /** Resting corner radius. With pressedRadius set, the shape morphs on press (M3 Expressive). */
  radius?: number;
  pressedRadius?: number;
  /** Shrink slightly on press. Off for list rows, where touch-down starts every scroll. */
  scaleOnPress?: boolean;
}

const AnimatedPressable = Animated.createAnimatedComponent(Pressable);

/**
 * Native ripple plus a spring on the fast spatial token. With a radius pair the shape morphs on
 * press (pill -> rounded square), which buttons and the FAB use as their feedback; the corner
 * radius cannot run on the native driver, so morphing taps do not also scale. Everything else
 * scales on the native driver, off the JS thread. Reduced motion: ripple only.
 */
export function Tap({ style, children, radius = 0, pressedRadius, scaleOnPress = true, onPressIn, onPressOut, ...props }: TapProps) {
  const { colors } = useTheme();
  const morph = pressedRadius !== undefined;
  const scales = scaleOnPress && !morph;
  const [scale] = useState(() => new Animated.Value(1));
  const [corner] = useState(() => new Animated.Value(radius));
  const press = (down: boolean) => {
    if (reduceMotionEnabled()) return;
    if (morph) Animated.spring(corner, { toValue: down ? pressedRadius : radius, ...motion.spatialFast, useNativeDriver: false }).start();
    if (scales) Animated.spring(scale, { toValue: down ? 0.97 : 1, ...motion.spatialFast, useNativeDriver: true }).start();
  };
  return (
    <AnimatedPressable
      {...props}
      android_ripple={{ color: colors.outlineVariant, foreground: true }}
      onPressIn={(e) => { press(true); onPressIn?.(e); }}
      onPressOut={(e) => { press(false); onPressOut?.(e); }}
      style={[{ overflow: 'hidden', borderRadius: morph ? corner : radius, transform: scales ? [{ scale }] : undefined }, style]}
    >
      {children}
    </AnimatedPressable>
  );
}

// ---- Buttons ---------------------------------------------------------------------------------

type ButtonVariant = 'filled' | 'tonal' | 'outlined' | 'text' | 'textDanger' | 'danger';

/**
 * `loading` disables the button and shows an inline indicator while keeping the label, so the
 * button does not change size or meaning mid-write. Wire it to the screen's busy flag.
 */
export function Button({ label, onPress, variant = 'filled', icon, loading, disabled, size = 'md', style }: {
  label: string; onPress?: () => void; variant?: ButtonVariant; icon?: IconName; loading?: boolean;
  disabled?: boolean; size?: 'md' | 'lg'; style?: StyleProp<ViewStyle>;
}) {
  const { colors } = useTheme();
  const height = size === 'lg' ? 56 : TOUCH;
  const palette: Record<ButtonVariant, { bg: ColorValue; fg: ColorValue; border?: ColorValue }> = {
    filled: { bg: colors.primary, fg: colors.onPrimary },
    tonal: { bg: colors.secondaryContainer, fg: colors.onSecondaryContainer },
    outlined: { bg: 'transparent', fg: colors.primary, border: colors.outline },
    text: { bg: 'transparent', fg: colors.primary },
    textDanger: { bg: 'transparent', fg: colors.error },
    danger: { bg: colors.errorContainer, fg: colors.onErrorContainer },
  };
  const inactive = disabled || loading;
  const textual = variant === 'text' || variant === 'textDanger';
  // M3 disabled: a quiet container with the content at 38%, so it cannot pass for a live tonal
  // button; outlined keeps its shape as a faint outline. Loading keeps the label readable instead.
  const off = !!disabled && !loading && !textual;
  const { bg, fg, border } = off
    ? variant === 'outlined'
      ? { bg: 'transparent', fg: colors.onSurface, border: colors.outlineVariant }
      : { bg: colors.surfaceContainerHighest, fg: colors.onSurface, border: undefined }
    : inactive && !textual
      ? { bg: colors.surfaceContainerHighest, fg: colors.onSurfaceVariant, border: undefined }
      : palette[variant];
  const dim = off ? { opacity: 0.38 } : null;
  return (
    <Tap
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: !!inactive, busy: !!loading }}
      disabled={inactive}
      onPress={() => { haptics.tick(); onPress?.(); }}
      radius={height / 2}
      pressedRadius={shape.small}
      style={[{
        minHeight: height, paddingHorizontal: size === 'lg' ? space.xxl + space.xs : space.xl, paddingVertical: space.sm, backgroundColor: bg,
        borderWidth: border ? 1 : 0, borderColor: border, opacity: inactive && textual ? 0.5 : 1,
        flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: space.sm,
      }, style]}
    >
      {loading ? <ExpressiveLoadingIndicator size={22} color={fg} label={`${label}, in progress`} />
        : icon ? <Icon name={icon} size={20} color={fg} style={dim} /> : null}
      <Text variant={size === 'lg' ? 'titleMediumEmphasized' : 'labelLarge'} color={fg} style={[{ textAlign: 'center', flexShrink: 1 }, dim]}>{label}</Text>
    </Tap>
  );
}

export function IconButton({ icon, onPress, label, tone = 'standard', disabled }: {
  icon: IconName; onPress?: () => void; label: string; tone?: 'standard' | 'tonal' | 'inverse'; disabled?: boolean;
}) {
  const { colors } = useTheme();
  const ink = tone === 'tonal' ? colors.onSecondaryContainer : tone === 'inverse' ? colors.inverseOnSurface : colors.onSurfaceVariant;
  return (
    <Tap
      accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled: !!disabled }} disabled={disabled} onPress={onPress}
      radius={20} pressedRadius={shape.small} hitSlop={(TOUCH - 40) / 2}
      style={{
        width: 40, height: 40, alignItems: 'center', justifyContent: 'center', opacity: disabled ? 0.4 : 1,
        backgroundColor: tone === 'tonal' ? colors.secondaryContainer : 'transparent',
      }}
    >
      <Icon name={icon} size={22} color={ink} />
    </Tap>
  );
}

/** Extended FAB, bottom-right. Squarer when pressed, through Tap's morph. */
export function Fab({ icon, label, onPress, disabled }: { icon: IconName; label: string; onPress: () => void; disabled?: boolean }) {
  const { colors } = useTheme();
  // Tab screens stay mounted, so "on screen" is focus, not mount.
  useObstruction('fab', useIsFocused());
  return (
    <View style={{ position: 'absolute', right: space.lg, bottom: space.lg }}>
      <Tap
        accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled: !!disabled }} onPress={onPress} disabled={disabled}
        radius={shape.medium} pressedRadius={shape.large}
        style={{
          minHeight: 56, paddingHorizontal: space.xl, flexDirection: 'row', alignItems: 'center', gap: space.md,
          backgroundColor: colors.primaryContainer, elevation: 3, opacity: disabled ? 0.6 : 1,
        }}
      >
        <Icon name={icon} size={24} color={colors.onPrimaryContainer} />
        <Text variant="titleMediumEmphasized" color={colors.onPrimaryContainer}>{label}</Text>
      </Tap>
    </View>
  );
}

// ---- Containers ------------------------------------------------------------------------------

type CardTone = 'low' | 'default' | 'high' | 'primary' | 'tertiary';

export function Card({ children, tone = 'low', style }: { children: React.ReactNode; tone?: CardTone; style?: StyleProp<ViewStyle> }) {
  const { colors } = useTheme();
  const bg: Record<CardTone, ColorValue> = {
    low: colors.surfaceContainerLow, default: colors.surfaceContainer, high: colors.surfaceContainerHigh,
    primary: colors.primaryContainer, tertiary: colors.tertiaryContainer,
  };
  return <View style={[{ backgroundColor: bg[tone], borderRadius: shape.large, padding: space.xl, gap: space.md }, style]}>{children}</View>;
}

export function ScreenHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'flex-end', paddingTop: space.xxl, paddingBottom: space.lg, gap: space.sm }}>
      <View style={{ flex: 1 }}>
        {subtitle ? <Text variant="labelLarge" color={colors.primary}>{subtitle}</Text> : null}
        <Text variant="headlineLarge" accessibilityRole="header">{title}</Text>
      </View>
      {actions}
    </View>
  );
}

export function SectionHeader({ title, summary }: { title: string; summary?: string }) {
  const { colors } = useTheme();
  return (
    // Wraps rather than squeezing: with large text the summary drops below the title instead of
    // breaking mid-word in a sliver of width.
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'baseline', justifyContent: 'space-between', paddingTop: space.xl, paddingBottom: space.sm, paddingHorizontal: space.xs, columnGap: space.sm }}>
      <Text variant="titleMediumEmphasized" color={colors.primary} accessibilityRole="header">{title}</Text>
      {summary ? <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ marginLeft: 'auto', textAlign: 'right' }}>{summary}</Text> : null}
    </View>
  );
}

export type ListPosition = 'single' | 'first' | 'middle' | 'last';

/** Position within a run of rows, for the Expressive segmented-list shape. */
export const positionOf = (index: number, count: number): ListPosition =>
  count === 1 ? 'single' : index === 0 ? 'first' : index === count - 1 ? 'last' : 'middle';

/** Segmented list row: large outer corners on the run, small inner ones where rows meet. */
/**
 * A pressable row is one TalkBack focus stop, so the buttons inside it cannot be reached by focus.
 * `actions` exposes them as accessibility actions (TalkBack's actions menu) instead.
 */
export function ListItem({ leading, title, subtitle, trailing, onPress, onLongPress, position = 'single', children, accessibilityLabel, actions }: {
  leading?: React.ReactNode; title: React.ReactNode; subtitle?: React.ReactNode; trailing?: React.ReactNode;
  onPress?: () => void; onLongPress?: () => void; position?: ListPosition; children?: React.ReactNode; accessibilityLabel?: string;
  actions?: { label: string; run: () => void }[];
}) {
  const { colors } = useTheme();
  const top = position === 'single' || position === 'first' ? shape.large : shape.listInner;
  const bottom = position === 'single' || position === 'last' ? shape.large : shape.listInner;
  const body = (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.lg, paddingHorizontal: space.lg, paddingVertical: space.md + space.xxs, minHeight: 64 }}>
      {leading}
      <View style={{ flex: 1, gap: space.xxs }}>
        {typeof title === 'string' ? <Text variant="titleMedium" numberOfLines={1}>{title}</Text> : title}
        {typeof subtitle === 'string' ? <Text variant="bodyMedium" color={colors.onSurfaceVariant} numberOfLines={2}>{subtitle}</Text> : subtitle}
      </View>
      {trailing}
    </View>
  );
  const style: ViewStyle = {
    backgroundColor: colors.surfaceContainer, marginBottom: shape.listGap,
    borderTopLeftRadius: top, borderTopRightRadius: top, borderBottomLeftRadius: bottom, borderBottomRightRadius: bottom,
  };
  if (!onPress && !onLongPress) return <View style={[style, { overflow: 'hidden' }]}>{body}{children}</View>;
  return (
    <Tap
      onPress={onPress} onLongPress={onLongPress && (() => { haptics.longPress(); onLongPress(); })} accessibilityRole="button" accessibilityLabel={accessibilityLabel} style={style} scaleOnPress={false}
      accessibilityActions={actions?.map((a, i) => ({ name: `action${i}`, label: a.label }))}
      onAccessibilityAction={(e) => actions?.[Number(e.nativeEvent.actionName.slice('action'.length))]?.run()}
    >
      {body}
      {children}
    </Tap>
  );
}

/**
 * Squircle tile behind an icon or letter - the monogram that stands in for a provider logo. Logos
 * are not loaded (the catalog's are remote SVGs); if they ever are, fade them in over this.
 */
export const TILE_CORNER = 0.34;
export function Tile({ icon, letter, color, background, size = 44 }: { icon?: IconName; letter?: string; color: ColorValue; background: ColorValue; size?: number }) {
  return (
    <View accessible={false} style={{ width: size, height: size, borderRadius: size * TILE_CORNER, backgroundColor: background, alignItems: 'center', justifyContent: 'center' }}>
      {icon ? <Icon name={icon} size={size * 0.5} color={color} /> : <Text variant="titleLargeEmphasized" color={color} allowFontScaling={false} style={{ fontSize: size * 0.45, lineHeight: size * 0.6 }}>{letter}</Text>}
    </View>
  );
}

// ---- Feedback --------------------------------------------------------------------------------

type BannerTone = 'error' | 'warning' | 'info' | 'success';

export function Banner({ tone, text, actionLabel, onAction }: { tone: BannerTone; text: string; actionLabel?: string; onAction?: () => void }) {
  const { colors } = useTheme();
  const map: Record<BannerTone, { bg: ColorValue; fg: ColorValue; icon: IconName }> = {
    error: { bg: colors.errorContainer, fg: colors.onErrorContainer, icon: 'alert-circle-outline' },
    warning: { bg: colors.warningContainer, fg: colors.onWarningContainer, icon: 'alert-outline' },
    info: { bg: colors.secondaryContainer, fg: colors.onSecondaryContainer, icon: 'information-outline' },
    success: { bg: colors.successContainer, fg: colors.onSuccessContainer, icon: 'check-circle-outline' },
  };
  const { bg, fg, icon } = map[tone];
  return (
    <View accessibilityLiveRegion="polite" style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, backgroundColor: bg, borderRadius: shape.medium, paddingVertical: space.md, paddingHorizontal: space.lg, marginBottom: space.md }}>
      <Icon name={icon} size={22} color={fg} accessibilityElementsHidden importantForAccessibility="no" />
      <Text variant="bodyMedium" color={fg} style={{ flex: 1 }}>{text}</Text>
      {actionLabel ? (
        <Tap onPress={onAction} radius={shape.medium} accessibilityRole="button" accessibilityLabel={actionLabel}
          style={{ paddingHorizontal: space.md, minHeight: TOUCH, justifyContent: 'center' }}>
          <Text variant="labelLarge" color={fg}>{actionLabel}</Text>
        </Tap>
      ) : null}
    </View>
  );
}

/** Shown wherever writes are closed because the phone has no network. */
export const OfflineBanner = () => <Banner tone="warning" text="You're offline. Changes are disabled until you reconnect." />;

export function Pill({ label, tone = 'neutral', icon }: { label: string; tone?: 'neutral' | 'error' | 'warning' | 'tertiary' | 'success'; icon?: IconName }) {
  const { colors } = useTheme();
  const map = {
    neutral: [colors.surfaceContainerHighest, colors.onSurfaceVariant],
    error: [colors.errorContainer, colors.onErrorContainer],
    warning: [colors.renewalWarningContainer, colors.onRenewalWarningContainer],
    tertiary: [colors.tertiaryContainer, colors.onTertiaryContainer],
    success: [colors.successContainer, colors.onSuccessContainer],
  } as const;
  const [bg, fg] = map[tone];
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, backgroundColor: bg, borderRadius: shape.full, paddingHorizontal: space.sm, paddingVertical: space.xxs }}>
      {icon ? <Icon name={icon} size={14} color={fg} accessibilityElementsHidden importantForAccessibility="no" /> : null}
      <Text variant="labelMedium" color={fg}>{label}</Text>
    </View>
  );
}

/** Empty screens: the ring motif around the screen's own icon, one sentence, and the next step. */
export function EmptyState({ icon, title, body, action }: { icon: IconName; title: string; body: string; action?: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <View style={{ alignItems: 'center', paddingVertical: space.huge, paddingHorizontal: space.xxl, gap: space.md }}>
      <RingMark size={120} progress={0.62} center={<Icon name={icon} size={40} color={colors.primary} />} />
      <Text variant="titleLargeEmphasized" style={{ textAlign: 'center' }} accessibilityRole="header">{title}</Text>
      <Text variant="bodyMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center' }}>{body}</Text>
      {action}
    </View>
  );
}

/**
 * Rounded bar with a gap before its track. Without a value it is the wavy indeterminate variant,
 * for short waits that have no figure to show (the wave stands still with reduced motion).
 */
export function ProgressBar({ value, color }: { value?: number; color?: ColorValue }) {
  const { colors } = useTheme();
  if (value === undefined) return <WavyProgress />;
  const pct = Math.max(0, Math.min(1, value));
  return (
    <View style={{ height: space.sm, flexDirection: 'row', gap: space.xs }}>
      <View style={{ flex: pct || 0.0001, backgroundColor: color ?? colors.primary, borderRadius: space.xs }} />
      <View style={{ flex: 1 - pct || 0.0001, backgroundColor: colors.surfaceContainerHighest, borderRadius: space.xs }} />
    </View>
  );
}

const WAVELENGTH = 24;
function WavyProgress() {
  const { colors } = useTheme();
  const reduced = useReducedMotion();
  const [width, setWidth] = useState(0);
  const [shift] = useState(() => new Animated.Value(0));
  useEffect(() => {
    if (reduced) return;
    const loop = Animated.loop(Animated.timing(shift, { toValue: -WAVELENGTH, duration: 700, easing: Easing.linear, useNativeDriver: true }));
    loop.start();
    return () => loop.stop();
  }, [shift, reduced]);
  const span = width + WAVELENGTH;
  let d = 'M2 5';
  for (let x = 0; x < span; x += WAVELENGTH) d += ` q${WAVELENGTH / 4} -4 ${WAVELENGTH / 2} 0 t${WAVELENGTH / 2} 0`;
  return (
    <View accessible accessibilityRole="progressbar" accessibilityLabel="Loading" style={{ height: 10, overflow: 'hidden' }}
      onLayout={(e) => setWidth(e.nativeEvent.layout.width)}>
      {width > 0 ? (
        <Animated.View style={{ width: span, transform: [{ translateX: shift }] }}>
          <Svg width={span} height={10}><Path d={d} fill="none" stroke={colors.primary} strokeWidth={4} strokeLinecap="round" /></Svg>
        </Animated.View>
      ) : null}
    </View>
  );
}

/**
 * Fades its content in once, on mount (key it to replay). `rise` adds a short upward settle, for a
 * value that has just been swapped. Native driver; appears at once with reduced motion.
 */
export function FadeIn({ children, rise, style }: { children: React.ReactNode; rise?: boolean; style?: StyleProp<ViewStyle> }) {
  const reduced = useReducedMotion();
  const [p] = useState(() => new Animated.Value(reduced ? 1 : 0));
  useEffect(() => {
    if (!reduced) Animated.spring(p, { toValue: 1, ...motion.effectsDefault, useNativeDriver: true }).start();
  }, [p, reduced]);
  return (
    <Animated.View style={[style, {
      opacity: p.interpolate({ inputRange: [0, 1], outputRange: [0, 1], extrapolate: 'clamp' }),
      transform: rise ? [{ translateY: p.interpolate({ inputRange: [0, 1], outputRange: [space.sm, 0] }) }] : [],
    }]}>
      {children}
    </Animated.View>
  );
}

// ---- Inputs ----------------------------------------------------------------------------------

export function TextField({ label, error, secure, style, ref, ...props }: TextInputProps & {
  label: string; error?: string | null; secure?: boolean;
  /** The inner input, so a form can move focus to the next field on the keyboard's Next. */
  ref?: React.Ref<TextInput>;
}) {
  const { colors } = useTheme();
  const [focused, setFocused] = useState(false);
  const [hidden, setHidden] = useState(true);
  const input = useRef<TextInput>(null);
  useImperativeHandle(ref, () => input.current as TextInput, []);
  // Read-only (e.g. the address a reset code went to) must not look like it is waiting for input.
  const locked = props.editable === false;
  // Android keeps the input focused when Back closes the keyboard; blur so the outline matches.
  useEffect(() => {
    if (!focused) return;
    const sub = Keyboard.addListener('keyboardDidHide', () => input.current?.blur());
    return () => sub.remove();
  }, [focused]);
  return (
    <View style={[{ gap: space.xs }, style]}>
      {/* The whole field focuses the input, label included, as an M3 text field does. */}
      <Pressable accessible={false} importantForAccessibility="no" onPress={() => input.current?.focus()} style={{
        backgroundColor: colors.surfaceContainerHighest, borderRadius: shape.medium, paddingHorizontal: space.lg, paddingTop: space.sm,
        borderWidth: 2, borderColor: error ? colors.error : focused ? colors.primary : 'transparent', flexDirection: 'row', alignItems: 'center',
        opacity: locked ? 0.6 : 1,
      }}>
        <View style={{ flex: 1 }}>
          <Text variant="labelMedium" color={error ? colors.error : focused ? colors.primary : colors.onSurfaceVariant}>{label}</Text>
          <TextInput
            {...props}
            ref={input}
            accessibilityLabel={error ? `${label}, error: ${error}` : label}
            // Password fields never get keyboard help: with the eye toggled to "show", Android would
            // otherwise capitalise and autocorrect them, silently storing a different password.
            {...(secure ? { autoCapitalize: 'none', autoCorrect: false, spellCheck: false } as const : {})}
            secureTextEntry={secure && hidden}
            placeholderTextColor={colors.onSurfaceVariant}
            cursorColor={colors.primary}
            selectionColor={colors.primary}
            onFocus={(e) => { setFocused(true); props.onFocus?.(e); }}
            onBlur={(e) => { setFocused(false); props.onBlur?.(e); }}
            style={[typescale.bodyLarge, { color: colors.onSurface, paddingVertical: space.xs + space.xxs, paddingHorizontal: 0 }]}
          />
        </View>
        {secure ? <IconButton icon={hidden ? 'eye-outline' : 'eye-off-outline'} label={hidden ? 'Show password' : 'Hide password'} onPress={() => setHidden((h) => !h)} /> : null}
      </Pressable>
      {error ? <Text variant="labelMedium" color={colors.error} accessibilityLiveRegion="polite" style={{ paddingHorizontal: space.lg }}>{error}</Text> : null}
    </View>
  );
}

/** A field that opens something (sheet, date picker) instead of taking text. */
export function SelectField({ label, value, placeholder, onPress, disabled, leading }: {
  label: string; value?: string | null; placeholder: string; onPress: () => void; disabled?: boolean; leading?: React.ReactNode;
}) {
  const { colors } = useTheme();
  return (
    <Tap
      // Dismissed first: Android hands focus back to the last input when a sheet or dialog closes,
      // which reopened the keyboard over the form after every pick.
      onPress={() => { Keyboard.dismiss(); onPress(); }} disabled={disabled} radius={shape.medium} accessibilityRole="button"
      accessibilityLabel={`${label}: ${value ?? placeholder}`} accessibilityState={{ disabled: !!disabled }}
      style={{ backgroundColor: colors.surfaceContainerHighest, paddingHorizontal: space.lg, paddingVertical: space.sm + space.xxs, minHeight: 56, flexDirection: 'row', alignItems: 'center', gap: space.md, opacity: disabled ? 0.5 : 1 }}
    >
      {leading}
      <View style={{ flex: 1 }}>
        <Text variant="labelMedium" color={colors.onSurfaceVariant}>{label}</Text>
        <Text variant="bodyLarge" color={value ? colors.onSurface : colors.onSurfaceVariant} numberOfLines={1}>{value ?? placeholder}</Text>
      </View>
      <Icon name="menu-down" size={24} color={colors.onSurfaceVariant} accessibilityElementsHidden importantForAccessibility="no" />
    </Tap>
  );
}

export function SwitchRow({ label, caption, value, onValueChange, disabled }: {
  label: string; caption?: string; value: boolean; onValueChange: (v: boolean) => void; disabled?: boolean;
}) {
  const { colors } = useTheme();
  const toggle = (v: boolean) => { haptics.tick(); onValueChange(v); };
  // The whole row is the target, as an M3 list item with a switch is; the switch is its indicator.
  return (
    <Tap
      onPress={() => toggle(!value)} disabled={disabled} radius={shape.medium} scaleOnPress={false}
      accessibilityRole="switch" accessibilityLabel={label} accessibilityHint={caption} accessibilityState={{ checked: value, disabled: !!disabled }}
      style={{
        flexDirection: 'row', alignItems: 'center', gap: space.md, minHeight: TOUCH, opacity: disabled ? 0.5 : 1,
        marginHorizontal: -space.sm, paddingHorizontal: space.sm, paddingVertical: space.xs,
      }}
    >
      <View style={{ flex: 1 }}>
        <Text variant="bodyLarge">{label}</Text>
        {caption ? <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{caption}</Text> : null}
      </View>
      <Switch
        value={value} disabled={disabled} onValueChange={toggle}
        accessibilityElementsHidden importantForAccessibility="no-hide-descendants"
        // Off, the track is outlineVariant rather than the highest surface, which all but vanished
        // on a card in dark mode (Android's switch takes no outline).
        trackColor={{ true: colors.primary, false: colors.outlineVariant }}
        thumbColor={value ? colors.onPrimary : colors.outline}
      />
    </Tap>
  );
}

/**
 * M3 Expressive connected button group. Segments share small inner corners; the selected one
 * rounds fully and fills, so the choice reads by shape as well as colour. The change morphs: the
 * corners spring between the two shapes while the fills and labels cross-fade.
 */
export function ButtonGroup<T extends string>({ options, value, onChange, disabled, onPrimaryContainer }: {
  options: { value: T; label: string; icon?: IconName }[]; value: T; onChange: (v: T) => void; disabled?: boolean;
  /**
   * On a primaryContainer card a `primary` fill all but vanishes in dark mode (1.87:1), so the
   * selected segment inverts to onPrimaryContainer with primaryContainer ink, and the others are a
   * translucent wash over the card rather than a dark surface block on a bright card.
   */
  onPrimaryContainer?: boolean;
}) {
  const { colors, isDark } = useTheme();
  // Light: the card's own dark ink at 12%. Dark: that ink is white, and lightening the card would
  // drop its labels below 4.5:1, so the wash is black at 20% instead.
  const look: SegmentLook = onPrimaryContainer
    ? {
      selectedBg: colors.onPrimaryContainer, selectedFg: colors.primaryContainer, restFg: colors.onPrimaryContainer,
      restBg: isDark ? colors.shadow : colors.onPrimaryContainer, restAlpha: isDark ? 0.2 : 0.12,
    }
    : { selectedBg: colors.primary, selectedFg: colors.onPrimary, restBg: colors.surfaceContainerHighest, restAlpha: 1, restFg: colors.onSurfaceVariant };
  return (
    <View accessibilityRole="radiogroup" style={{ flexDirection: 'row', gap: space.xxs, opacity: disabled ? 0.5 : 1 }}>
      {options.map((option, i) => (
        <Segment
          key={option.value} label={option.label} icon={option.icon} look={look} disabled={disabled}
          selected={option.value === value} first={i === 0} last={i === options.length - 1}
          onPress={() => { haptics.tick(); onChange(option.value); }}
        />
      ))}
    </View>
  );
}

interface SegmentLook { selectedBg: ColorValue; selectedFg: ColorValue; restBg: ColorValue; restAlpha: number; restFg: ColorValue }

function Segment({ label, icon, look, selected, first, last, disabled, onPress }: {
  label: string; icon?: IconName; look: SegmentLook; selected: boolean; first: boolean; last: boolean; disabled?: boolean; onPress: () => void;
}) {
  const reduced = useReducedMotion();
  const [p] = useState(() => new Animated.Value(selected ? 1 : 0));
  useEffect(() => {
    if (reduced) p.setValue(selected ? 1 : 0);
    // JS driver: corner radius has no native-driver support, and the fades share the one value.
    else Animated.spring(p, { toValue: selected ? 1 : 0, ...motion.spatialFast, useNativeDriver: false }).start();
  }, [selected, reduced, p]);

  // Fades use the clamped value; the spring's overshoot belongs to the shape alone.
  const t = p.interpolate({ inputRange: [0, 1], outputRange: [0, 1], extrapolate: 'clamp' });
  const outer = TOUCH / 2;
  const inner = p.interpolate({ inputRange: [0, 1], outputRange: [shape.extraSmall, outer], extrapolate: 'clamp' });
  const corners = {
    borderTopLeftRadius: first ? outer : inner, borderBottomLeftRadius: first ? outer : inner,
    borderTopRightRadius: last ? outer : inner, borderBottomRightRadius: last ? outer : inner,
  };
  const fill = { position: 'absolute', top: 0, right: 0, bottom: 0, left: 0, ...corners } as const;
  const content = (on: boolean) => (
    <>
      {icon ? <Icon name={on ? 'check' : icon} size={18} color={on ? look.selectedFg : look.restFg} /> : null}
      {/* Two lines rather than an ellipsis at large font sizes. */}
      <Text variant="labelLarge" numberOfLines={2} style={{ textAlign: 'center', flexShrink: 1 }} color={on ? look.selectedFg : look.restFg}>{label}</Text>
    </>
  );
  const row = { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: space.xs + space.xxs } as const;
  return (
    <Tap
      disabled={disabled}
      accessibilityRole="radio"
      accessibilityState={{ selected, disabled: !!disabled }}
      accessibilityLabel={label}
      onPress={onPress}
      // The morph is the press feedback; a scale on top would fight it.
      scaleOnPress={false}
      style={{ flex: 1, minHeight: TOUCH, justifyContent: 'center', paddingHorizontal: space.xs, ...corners }}
    >
      {/* Both fills always carry their colour and only fade: Android drops a view's corner radius
          when a background first appears on it. */}
      <Animated.View style={[fill, { backgroundColor: look.restBg, opacity: t.interpolate({ inputRange: [0, 1], outputRange: [look.restAlpha, 0] }) }]} />
      <Animated.View style={[fill, { backgroundColor: look.selectedBg, opacity: t }]} />
      <Animated.View style={[row, { opacity: t.interpolate({ inputRange: [0, 1], outputRange: [1, 0] }) }]}>{content(false)}</Animated.View>
      <Animated.View style={[row, { position: 'absolute', left: space.xs, right: space.xs, top: 0, bottom: 0, opacity: t }]}>{content(true)}</Animated.View>
    </Tap>
  );
}

export function Chip({ label, selected, onPress, icon, disabled }: { label: string; selected: boolean; onPress: () => void; icon?: IconName; disabled?: boolean }) {
  const { colors } = useTheme();
  return (
    <Tap
      onPress={() => { haptics.tick(); onPress(); }} disabled={disabled} radius={shape.extraSmall} pressedRadius={shape.medium}
      accessibilityRole="radio" accessibilityState={{ selected, disabled: !!disabled }} accessibilityLabel={label}
      hitSlop={{ top: (TOUCH - 36) / 2, bottom: (TOUCH - 36) / 2 }}
      style={{
        minHeight: 36, paddingHorizontal: space.md + space.xxs, flexDirection: 'row', alignItems: 'center', gap: space.xs + space.xxs,
        // Unselected sits on the card colour rather than 'transparent': Android drops the corner radius
        // when a background first appears on a view, which squared the chip on selection.
        backgroundColor: selected ? colors.secondaryContainer : colors.surfaceContainerLow, borderWidth: selected ? 0 : 1, borderColor: colors.outline,
      }}
    >
      {selected ? <Icon name="check" size={18} color={colors.onSecondaryContainer} /> : icon ? <Icon name={icon} size={18} color={colors.onSurfaceVariant} /> : null}
      <Text variant="labelLarge" color={selected ? colors.onSecondaryContainer : colors.onSurfaceVariant}>{label}</Text>
    </Tap>
  );
}

import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useEffect, useRef, useState } from 'react';
import {
  AccessibilityInfo, ActivityIndicator, Animated, Keyboard, Pressable, Switch, Text as RNText, TextInput, View,
  type ColorValue, type PressableProps, type StyleProp, type TextInputProps, type TextProps, type ViewStyle,
} from 'react-native';
import { shape, typescale, useTheme, type TypeVariant } from '@/theme';
import * as haptics from '@/lib/haptics';

export type IconName = React.ComponentProps<typeof MaterialCommunityIcons>['name'];
export const Icon = MaterialCommunityIcons;

let reduceMotion = false;
AccessibilityInfo.isReduceMotionEnabled().then((v) => { reduceMotion = v; }).catch(() => undefined);
AccessibilityInfo.addEventListener('reduceMotionChanged', (v) => { reduceMotion = v; });

// ---- Text ------------------------------------------------------------------------------------

export function Text({ variant = 'bodyLarge', color, style, ...props }: TextProps & { variant?: TypeVariant; color?: ColorValue }) {
  const { colors } = useTheme();
  return <RNText {...props} style={[typescale[variant], { color: color ?? colors.onSurface }, style]} />;
}

// ---- Press feedback --------------------------------------------------------------------------

interface TapProps extends Omit<PressableProps, 'style' | 'children'> {
  style?: StyleProp<ViewStyle>;
  children: React.ReactNode;
  /** Resting corner radius. With pressedRadius set, the shape morphs on press (M3 Expressive). */
  radius?: number;
  pressedRadius?: number;
}

const AnimatedPressable = Animated.createAnimatedComponent(Pressable);

/** Native ripple plus a small spring; shape morph where a radius pair is given. Honours reduced motion. */
export function Tap({ style, children, radius = 0, pressedRadius, onPressIn, onPressOut, ...props }: TapProps) {
  const { colors } = useTheme();
  const [scale] = useState(() => new Animated.Value(1));
  const [corner] = useState(() => new Animated.Value(radius));
  const spring = (to: number, r: number) => {
    if (reduceMotion) return;
    Animated.parallel([
      Animated.spring(scale, { toValue: to, useNativeDriver: false, speed: 40, bounciness: 6 }),
      Animated.spring(corner, { toValue: r, useNativeDriver: false, speed: 30, bounciness: 8 }),
    ]).start();
  };
  return (
    <AnimatedPressable
      {...props}
      android_ripple={{ color: colors.outlineVariant, foreground: true }}
      onPressIn={(e) => { spring(0.97, pressedRadius ?? radius); onPressIn?.(e); }}
      onPressOut={(e) => { spring(1, radius); onPressOut?.(e); }}
      style={[{ overflow: 'hidden', borderRadius: corner, transform: [{ scale }] }, style]}
    >
      {children}
    </AnimatedPressable>
  );
}

// ---- Buttons ---------------------------------------------------------------------------------

type ButtonVariant = 'filled' | 'tonal' | 'outlined' | 'text' | 'danger';

export function Button({ label, onPress, variant = 'filled', icon, loading, disabled, size = 'md', style }: {
  label: string; onPress?: () => void; variant?: ButtonVariant; icon?: IconName; loading?: boolean;
  disabled?: boolean; size?: 'md' | 'lg'; style?: StyleProp<ViewStyle>;
}) {
  const { colors } = useTheme();
  const height = size === 'lg' ? 56 : 44;
  const palette: Record<ButtonVariant, { bg: ColorValue; fg: ColorValue; border?: ColorValue }> = {
    filled: { bg: colors.primary, fg: colors.onPrimary },
    tonal: { bg: colors.secondaryContainer, fg: colors.onSecondaryContainer },
    outlined: { bg: 'transparent', fg: colors.primary, border: colors.outlineVariant },
    text: { bg: 'transparent', fg: colors.primary },
    danger: { bg: colors.errorContainer, fg: colors.onErrorContainer },
  };
  const inactive = disabled || loading;
  const { bg, fg, border } = inactive && variant !== 'text'
    ? { bg: colors.surfaceContainerHighest, fg: colors.onSurfaceVariant, border: undefined }
    : palette[variant];
  return (
    <Tap
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: !!inactive, busy: !!loading }}
      disabled={inactive}
      onPress={() => { haptics.tick(); onPress?.(); }}
      radius={height / 2}
      pressedRadius={shape.md}
      style={[{
        height, paddingHorizontal: size === 'lg' ? 28 : 20, backgroundColor: bg,
        borderWidth: border ? 1 : 0, borderColor: border, opacity: inactive && variant === 'text' ? 0.5 : 1,
        flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8,
      }, style]}
    >
      {loading ? <ActivityIndicator color={fg} size="small" /> : icon ? <Icon name={icon} size={20} color={fg} /> : null}
      <Text variant={size === 'lg' ? 'titleMedium' : 'labelLarge'} color={fg}>{label}</Text>
    </Tap>
  );
}

export function IconButton({ icon, onPress, label, tone = 'standard', disabled }: {
  icon: IconName; onPress?: () => void; label: string; tone?: 'standard' | 'tonal'; disabled?: boolean;
}) {
  const { colors } = useTheme();
  return (
    <Tap
      accessibilityRole="button" accessibilityLabel={label} disabled={disabled} onPress={onPress}
      radius={20} pressedRadius={12} hitSlop={4}
      style={{
        width: 40, height: 40, alignItems: 'center', justifyContent: 'center', opacity: disabled ? 0.4 : 1,
        backgroundColor: tone === 'tonal' ? colors.secondaryContainer : 'transparent',
      }}
    >
      <Icon name={icon} size={22} color={tone === 'tonal' ? colors.onSecondaryContainer : colors.onSurfaceVariant} />
    </Tap>
  );
}

/** Extended FAB, bottom-right. */
export function Fab({ icon, label, onPress, disabled }: { icon: IconName; label: string; onPress: () => void; disabled?: boolean }) {
  const { colors } = useTheme();
  return (
    <View style={{ position: 'absolute', right: 16, bottom: 16 }}>
      <Tap
        accessibilityRole="button" accessibilityLabel={label} onPress={onPress} disabled={disabled}
        radius={shape.lg} pressedRadius={shape.xl}
        style={{
          height: 56, paddingHorizontal: 20, flexDirection: 'row', alignItems: 'center', gap: 12,
          backgroundColor: colors.primaryContainer, elevation: 3, opacity: disabled ? 0.6 : 1,
        }}
      >
        <Icon name={icon} size={24} color={colors.onPrimaryContainer} />
        <Text variant="titleMedium" color={colors.onPrimaryContainer}>{label}</Text>
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
  return <View style={[{ backgroundColor: bg[tone], borderRadius: shape.xl, padding: 20, gap: 12 }, style]}>{children}</View>;
}

export function ScreenHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'flex-end', paddingTop: 24, paddingBottom: 16, gap: 8 }}>
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
    <View style={{ flexDirection: 'row', alignItems: 'baseline', justifyContent: 'space-between', paddingTop: 20, paddingBottom: 8, paddingHorizontal: 4, gap: 8 }}>
      <Text variant="titleMedium" color={colors.primary} accessibilityRole="header">{title}</Text>
      {summary ? <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ flexShrink: 1, textAlign: 'right' }}>{summary}</Text> : null}
    </View>
  );
}

export type ListPosition = 'single' | 'first' | 'middle' | 'last';

/** Position within a run of rows, for the Expressive segmented-list shape. */
export const positionOf = (index: number, count: number): ListPosition =>
  count === 1 ? 'single' : index === 0 ? 'first' : index === count - 1 ? 'last' : 'middle';

/** Segmented list row: large outer corners on the run, small inner ones where rows meet. */
export function ListItem({ leading, title, subtitle, trailing, onPress, onLongPress, position = 'single', children, accessibilityLabel }: {
  leading?: React.ReactNode; title: React.ReactNode; subtitle?: React.ReactNode; trailing?: React.ReactNode;
  onPress?: () => void; onLongPress?: () => void; position?: ListPosition; children?: React.ReactNode; accessibilityLabel?: string;
}) {
  const { colors } = useTheme();
  const top = position === 'single' || position === 'first' ? 24 : 6;
  const bottom = position === 'single' || position === 'last' ? 24 : 6;
  const body = (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 16, paddingHorizontal: 16, paddingVertical: 14 }}>
      {leading}
      <View style={{ flex: 1, gap: 2 }}>
        {typeof title === 'string' ? <Text variant="titleMedium" numberOfLines={1}>{title}</Text> : title}
        {typeof subtitle === 'string' ? <Text variant="bodyMedium" color={colors.onSurfaceVariant} numberOfLines={2}>{subtitle}</Text> : subtitle}
      </View>
      {trailing}
    </View>
  );
  const style: ViewStyle = {
    backgroundColor: colors.surfaceContainer, marginBottom: 2,
    borderTopLeftRadius: top, borderTopRightRadius: top, borderBottomLeftRadius: bottom, borderBottomRightRadius: bottom,
  };
  if (!onPress && !onLongPress) return <View style={[style, { overflow: 'hidden' }]}>{body}{children}</View>;
  return (
    <Tap onPress={onPress} onLongPress={onLongPress} accessibilityRole="button" accessibilityLabel={accessibilityLabel} style={style}>
      {body}
      {children}
    </Tap>
  );
}

/** Squircle tile behind an icon or letter - the stand-in for a provider logo. */
export function Tile({ icon, letter, color, background, size = 44 }: { icon?: IconName; letter?: string; color: ColorValue; background: ColorValue; size?: number }) {
  return (
    <View style={{ width: size, height: size, borderRadius: size * 0.34, backgroundColor: background, alignItems: 'center', justifyContent: 'center' }}>
      {icon ? <Icon name={icon} size={size * 0.5} color={color} /> : <Text variant="titleLarge" color={color}>{letter}</Text>}
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
    <View accessibilityLiveRegion="polite" style={{ flexDirection: 'row', alignItems: 'center', gap: 12, backgroundColor: bg, borderRadius: shape.lg, paddingVertical: 12, paddingHorizontal: 16, marginBottom: 12 }}>
      <Icon name={icon} size={22} color={fg} />
      <Text variant="bodyMedium" color={fg} style={{ flex: 1 }}>{text}</Text>
      {actionLabel ? (
        <Tap onPress={onAction} radius={16} accessibilityRole="button" style={{ paddingHorizontal: 12, paddingVertical: 6 }}>
          <Text variant="labelLarge" color={fg}>{actionLabel}</Text>
        </Tap>
      ) : null}
    </View>
  );
}

/** Shown wherever writes are closed because the phone has no network. */
export const OfflineBanner = () => <Banner tone="warning" text="You're offline. Changes are disabled until you reconnect." />;

export function Pill({ label, tone = 'neutral' }: { label: string; tone?: 'neutral' | 'error' | 'tertiary' | 'success' }) {
  const { colors } = useTheme();
  const map = {
    neutral: [colors.surfaceContainerHighest, colors.onSurfaceVariant],
    error: [colors.errorContainer, colors.onErrorContainer],
    tertiary: [colors.tertiaryContainer, colors.onTertiaryContainer],
    success: [colors.successContainer, colors.onSuccessContainer],
  } as const;
  const [bg, fg] = map[tone];
  return (
    <View style={{ backgroundColor: bg, borderRadius: shape.full, paddingHorizontal: 8, paddingVertical: 2 }}>
      <Text variant="labelMedium" color={fg}>{label}</Text>
    </View>
  );
}

export function EmptyState({ icon, title, body, action }: { icon: IconName; title: string; body: string; action?: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <View style={{ alignItems: 'center', paddingVertical: 48, paddingHorizontal: 24, gap: 12 }}>
      <View style={{ width: 96, height: 96, borderRadius: 32, backgroundColor: colors.tertiaryContainer, alignItems: 'center', justifyContent: 'center', transform: [{ rotate: '-8deg' }] }}>
        <Icon name={icon} size={44} color={colors.onTertiaryContainer} style={{ transform: [{ rotate: '8deg' }] }} />
      </View>
      <Text variant="titleLarge" style={{ textAlign: 'center' }}>{title}</Text>
      <Text variant="bodyMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center' }}>{body}</Text>
      {action}
    </View>
  );
}

export function ProgressBar({ value, color }: { value: number; color: ColorValue }) {
  const { colors } = useTheme();
  const pct = Math.max(0, Math.min(1, value));
  return (
    <View style={{ height: 8, flexDirection: 'row', gap: 4 }}>
      <View style={{ flex: pct || 0.0001, backgroundColor: color, borderRadius: 4 }} />
      <View style={{ flex: 1 - pct || 0.0001, backgroundColor: colors.surfaceContainerHighest, borderRadius: 4 }} />
    </View>
  );
}

// ---- Inputs ----------------------------------------------------------------------------------

export function TextField({ label, error, secure, style, ...props }: TextInputProps & { label: string; error?: string | null; secure?: boolean }) {
  const { colors } = useTheme();
  const [focused, setFocused] = useState(false);
  const [hidden, setHidden] = useState(true);
  const input = useRef<TextInput>(null);
  // Android keeps the input focused when Back closes the keyboard; blur so the outline matches.
  useEffect(() => {
    if (!focused) return;
    const sub = Keyboard.addListener('keyboardDidHide', () => input.current?.blur());
    return () => sub.remove();
  }, [focused]);
  return (
    <View style={[{ gap: 4 }, style]}>
      <View style={{
        backgroundColor: colors.surfaceContainerHighest, borderRadius: shape.lg, paddingHorizontal: 16, paddingTop: 8,
        borderWidth: 2, borderColor: error ? colors.error : focused ? colors.primary : 'transparent', flexDirection: 'row', alignItems: 'center',
      }}>
        <View style={{ flex: 1 }}>
          <Text variant="labelMedium" color={error ? colors.error : focused ? colors.primary : colors.onSurfaceVariant}>{label}</Text>
          <TextInput
            {...props}
            ref={input}
            accessibilityLabel={label}
            secureTextEntry={secure && hidden}
            placeholderTextColor={colors.outline as string}
            cursorColor={colors.primary}
            selectionColor={colors.primary}
            onFocus={(e) => { setFocused(true); props.onFocus?.(e); }}
            onBlur={(e) => { setFocused(false); props.onBlur?.(e); }}
            style={[typescale.bodyLarge, { color: colors.onSurface, paddingVertical: 6, paddingHorizontal: 0 }]}
          />
        </View>
        {secure ? <IconButton icon={hidden ? 'eye-outline' : 'eye-off-outline'} label={hidden ? 'Show password' : 'Hide password'} onPress={() => setHidden((h) => !h)} /> : null}
      </View>
      {error ? <Text variant="labelMedium" color={colors.error} style={{ paddingHorizontal: 16 }}>{error}</Text> : null}
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
      onPress={() => { Keyboard.dismiss(); onPress(); }} disabled={disabled} radius={shape.lg} accessibilityRole="button" accessibilityLabel={`${label}: ${value ?? placeholder}`}
      style={{ backgroundColor: colors.surfaceContainerHighest, paddingHorizontal: 16, paddingVertical: 10, flexDirection: 'row', alignItems: 'center', gap: 12, opacity: disabled ? 0.5 : 1 }}
    >
      {leading}
      <View style={{ flex: 1 }}>
        <Text variant="labelMedium" color={colors.onSurfaceVariant}>{label}</Text>
        <Text variant="bodyLarge" color={value ? colors.onSurface : colors.outline} numberOfLines={1}>{value ?? placeholder}</Text>
      </View>
      <Icon name="menu-down" size={24} color={colors.onSurfaceVariant} />
    </Tap>
  );
}

export function SwitchRow({ label, caption, value, onValueChange, disabled }: {
  label: string; caption?: string; value: boolean; onValueChange: (v: boolean) => void; disabled?: boolean;
}) {
  const { colors } = useTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, opacity: disabled ? 0.5 : 1 }}>
      <View style={{ flex: 1 }}>
        <Text variant="bodyLarge">{label}</Text>
        {caption ? <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{caption}</Text> : null}
      </View>
      <Switch
        value={value} disabled={disabled} accessibilityLabel={label}
        onValueChange={(v) => { haptics.tick(); onValueChange(v); }}
        trackColor={{ true: colors.primary, false: colors.surfaceContainerHighest }}
        thumbColor={(value ? colors.onPrimary : colors.outline) as string}
      />
    </View>
  );
}

/**
 * M3 Expressive connected button group. Segments share small inner corners; the selected one
 * rounds fully and fills, so the choice reads by shape as well as colour.
 */
export function ButtonGroup<T extends string>({ options, value, onChange, disabled }: {
  options: { value: T; label: string; icon?: IconName }[]; value: T; onChange: (v: T) => void; disabled?: boolean;
}) {
  const { colors } = useTheme();
  return (
    <View accessibilityRole="radiogroup" style={{ flexDirection: 'row', gap: 2, opacity: disabled ? 0.5 : 1 }}>
      {options.map((option, i) => {
        const selected = option.value === value;
        const outer = 20;
        const inner = selected ? outer : shape.sm;
        return (
          <Tap
            key={option.value}
            disabled={disabled}
            accessibilityRole="radio"
            accessibilityState={{ selected }}
            accessibilityLabel={option.label}
            onPress={() => { haptics.tick(); onChange(option.value); }}
            style={{
              flex: 1, height: 44, alignItems: 'center', justifyContent: 'center', flexDirection: 'row', gap: 6, paddingHorizontal: 4,
              backgroundColor: selected ? colors.primary : colors.surfaceContainerHighest,
              borderTopLeftRadius: i === 0 ? outer : inner, borderBottomLeftRadius: i === 0 ? outer : inner,
              borderTopRightRadius: i === options.length - 1 ? outer : inner, borderBottomRightRadius: i === options.length - 1 ? outer : inner,
            }}
          >
            {option.icon ? <Icon name={selected ? 'check' : option.icon} size={18} color={selected ? colors.onPrimary : colors.onSurfaceVariant} /> : null}
            <Text variant="labelLarge" numberOfLines={1} color={selected ? colors.onPrimary : colors.onSurfaceVariant}>{option.label}</Text>
          </Tap>
        );
      })}
    </View>
  );
}

export function Chip({ label, selected, onPress, icon, disabled }: { label: string; selected: boolean; onPress: () => void; icon?: IconName; disabled?: boolean }) {
  const { colors } = useTheme();
  return (
    <Tap
      onPress={() => { haptics.tick(); onPress(); }} disabled={disabled} radius={shape.sm} pressedRadius={shape.lg}
      accessibilityRole="radio" accessibilityState={{ selected }} accessibilityLabel={label}
      style={{
        height: 36, paddingHorizontal: 14, flexDirection: 'row', alignItems: 'center', gap: 6,
        backgroundColor: selected ? colors.secondaryContainer : 'transparent', borderWidth: selected ? 0 : 1, borderColor: colors.outlineVariant,
      }}
    >
      {selected ? <Icon name="check" size={18} color={colors.onSecondaryContainer} /> : icon ? <Icon name={icon} size={18} color={colors.onSurfaceVariant} /> : null}
      <Text variant="labelLarge" color={selected ? colors.onSecondaryContainer : colors.onSurfaceVariant}>{label}</Text>
    </Tap>
  );
}

export function Spinner() {
  const { colors } = useTheme();
  return <ActivityIndicator color={colors.primary as string} size="large" style={{ paddingVertical: 32 }} />;
}

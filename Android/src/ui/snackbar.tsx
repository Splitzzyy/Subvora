import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { AccessibilityInfo, Animated, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useReducedMotion } from '@/lib/motion';
import { motion, shape, space, useTheme } from '@/theme';
import { Icon, IconButton, RAIL_MIN_WIDTH, Tap, Text, TOUCH } from '.';

export interface SnackbarMessage {
  text: string;
  /** Failures stay until dismissed: "this change wasn't saved" must not time out unread. */
  tone?: 'default' | 'error';
  /** Only for actions an existing API call reverses cleanly (e.g. undoing a rename). */
  actionLabel?: string;
  onAction?: () => void;
}

const SnackbarContext = createContext<(message: SnackbarMessage) => void>(() => undefined);

/** Write results, app-wide: one host at the root, so a message survives the screen that raised it closing. */
export const useSnackbar = () => useContext(SnackbarContext);

export function SnackbarProvider({ children }: { children: React.ReactNode }) {
  const [current, setCurrent] = useState<(SnackbarMessage & { id: number }) | null>(null);
  const show = useCallback((message: SnackbarMessage) => setCurrent({ ...message, id: Date.now() }), []);
  // Stable, so the shown snackbar's timer is not restarted by unrelated re-renders.
  const dismiss = useCallback((id: number) => setCurrent((c) => (c?.id === id ? null : c)), []);
  return (
    <SnackbarContext.Provider value={show}>
      {children}
      {current ? <Snackbar key={current.id} message={current} onDismiss={dismiss} /> : null}
    </SnackbarContext.Provider>
  );
}

const DURATION_MS = 4000;
const WITH_ACTION_MS = 8000;

function Snackbar({ message, onDismiss: dismissById }: { message: SnackbarMessage & { id: number }; onDismiss: (id: number) => void }) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const reduced = useReducedMotion();
  const [enter] = useState(() => new Animated.Value(0));
  const error = message.tone === 'error';
  const onDismiss = useCallback(() => dismissById(message.id), [dismissById, message.id]);

  useEffect(() => {
    AccessibilityInfo.announceForAccessibility(message.text);
    (reduced
      ? Animated.timing(enter, { toValue: 1, duration: motion.fadeMs, useNativeDriver: true })
      : Animated.spring(enter, { toValue: 1, ...motion.spatialDefault, useNativeDriver: true })).start();
    if (error) return;
    const timer = setTimeout(onDismiss, message.actionLabel ? WITH_ACTION_MS : DURATION_MS);
    return () => clearTimeout(timer);
  }, [enter, reduced, error, message, onDismiss]);

  // Clear of the navigation bar and of a FAB above it; with the side rail there is no bottom bar.
  const bottom = insets.bottom + (width >= RAIL_MIN_WIDTH ? space.lg : 88 + 56 + space.xxl);
  return (
    <Animated.View
      accessibilityLiveRegion={error ? 'assertive' : 'polite'}
      style={{
        position: 'absolute', left: space.lg, right: space.lg, bottom, alignItems: 'center',
        opacity: enter, transform: reduced ? [] : [{ translateY: enter.interpolate({ inputRange: [0, 1], outputRange: [space.xxl, 0] }) }],
      }}
    >
      <View style={{
        width: '100%', maxWidth: 560, minHeight: TOUCH, flexDirection: 'row', alignItems: 'center', gap: space.sm,
        backgroundColor: colors.inverseSurface, borderRadius: shape.extraSmall, paddingLeft: space.lg, paddingRight: space.xs, paddingVertical: space.xs, elevation: 6,
      }}>
        {error ? <Icon name="alert-circle-outline" size={20} color={colors.inverseOnSurface} accessibilityElementsHidden importantForAccessibility="no" /> : null}
        <Text variant="bodyMedium" color={colors.inverseOnSurface} style={{ flex: 1, paddingVertical: space.sm }}>{message.text}</Text>
        {message.actionLabel ? (
          <Tap radius={shape.medium} accessibilityRole="button" accessibilityLabel={message.actionLabel}
            onPress={() => { onDismiss(); message.onAction?.(); }}
            style={{ paddingHorizontal: space.md, minHeight: TOUCH, justifyContent: 'center' }}>
            <Text variant="labelLarge" color={colors.inversePrimary}>{message.actionLabel}</Text>
          </Tap>
        ) : null}
        {error ? <IconButton icon="close" label="Dismiss" onPress={onDismiss} tone="inverse" /> : null}
      </View>
    </Animated.View>
  );
}

import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { Animated, Keyboard, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useReducedMotion } from '@/lib/motion';
import { motion, shape, space, useTheme } from '@/theme';
import { Icon, IconButton, NAV_BAR_HEIGHT, Tap, Text, TOUCH } from '.';
import { useObstructions } from './obstructions';

export interface SnackbarMessage {
  text: string;
  /** Failures stay longer (ERROR_MS) and carry a close button. */
  tone?: 'default' | 'error';
  /** Only for actions an existing API call reverses cleanly (e.g. undoing a rename). */
  actionLabel?: string;
  onAction?: () => void;
}

const SnackbarContext = createContext<(message: SnackbarMessage) => void>(() => undefined);
const ClearContext = createContext<() => void>(() => undefined);

/** Write results, app-wide: one host at the root, so a message survives the screen that raised it closing. */
export const useSnackbar = () => useContext(SnackbarContext);

/**
 * Drops every queued and visible message. The session calls it whenever it signs in or out, so a
 * message about one session never shows over the next one (an old "wasn't saved" sat on top of
 * the sign-in form, and in front of "Account deleted.").
 */
export const useClearSnackbars = () => useContext(ClearContext);

let nextId = 0;

/**
 * One snackbar at a time, in order (M3). A new message supersedes any error still waiting or on
 * screen: an error has had its say once something newer happened, and queuing the newer message
 * behind it hid results like "Account deleted." behind a minutes-old failure.
 */
export function SnackbarProvider({ children }: { children: React.ReactNode }) {
  const [queue, setQueue] = useState<(SnackbarMessage & { id: number })[]>([]);
  const show = useCallback((message: SnackbarMessage) => setQueue((q) => {
    const rest = q.filter((m) => m.tone !== 'error');
    // The same message twice in a row says nothing new.
    return rest.some((m) => m.text === message.text) ? rest : [...rest, { ...message, id: ++nextId }];
  }), []);
  const clear = useCallback(() => setQueue([]), []);
  // Stable, so the shown snackbar's timer is not restarted by unrelated re-renders.
  const dismiss = useCallback((id: number) => setQueue((q) => q.filter((m) => m.id !== id)), []);
  const current = queue[0];
  return (
    <SnackbarContext.Provider value={show}>
      <ClearContext.Provider value={clear}>
        {children}
        {current ? <Snackbar key={current.id} message={current} onDismiss={dismiss} /> : null}
      </ClearContext.Provider>
    </SnackbarContext.Provider>
  );
}

const DURATION_MS = 4000;
const WITH_ACTION_MS = 8000;
/** Long enough to read a failure and reach Retry, short enough that it never outlives the moment. */
const ERROR_MS = 10000;

function Snackbar({ message, onDismiss: dismissById }: { message: SnackbarMessage & { id: number }; onDismiss: (id: number) => void }) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const covered = useObstructions();
  const reduced = useReducedMotion();
  const [enter] = useState(() => new Animated.Value(0));
  const [keyboard, setKeyboard] = useState(() => (Keyboard.isVisible() ? Keyboard.metrics()?.height ?? 0 : 0));
  const error = message.tone === 'error';

  // Above the keyboard when it is up, or a result raised from a form would land behind it.
  useEffect(() => {
    const show = Keyboard.addListener('keyboardDidShow', (e) => setKeyboard(e.endCoordinates.height));
    const hide = Keyboard.addListener('keyboardDidHide', () => setKeyboard(0));
    // A keyboard already closing when this mounted (a dialog's Save) can hide before the listener
    // exists; check again once it has settled.
    const settle = setTimeout(() => setKeyboard(Keyboard.isVisible() ? Keyboard.metrics()?.height ?? 0 : 0), 400);
    return () => { show.remove(); hide.remove(); clearTimeout(settle); };
  }, []);
  // Leaves the way it came (down and out) before the next one may show, rather than vanishing.
  const leaving = useRef(false);
  const onDismiss = useCallback(() => {
    if (leaving.current) return;
    leaving.current = true;
    Animated.timing(enter, { toValue: 0, duration: motion.fadeMs, useNativeDriver: true }).start(() => dismissById(message.id));
  }, [dismissById, message.id, enter]);

  // Announced once, by the live region below; an explicit announce as well read it twice.
  useEffect(() => {
    (reduced
      ? Animated.timing(enter, { toValue: 1, duration: motion.fadeMs, useNativeDriver: true })
      : Animated.spring(enter, { toValue: 1, ...motion.spatialDefault, useNativeDriver: true })).start();
    const timer = setTimeout(onDismiss, error ? ERROR_MS : message.actionLabel ? WITH_ACTION_MS : DURATION_MS);
    return () => clearTimeout(timer);
  }, [enter, reduced, error, message, onDismiss]);

  // Directly above whatever is at the bottom: the keyboard, else the navigation bar (none with the
  // side rail) and a FAB only when one is on screen.
  const navBar = covered.navBar ? NAV_BAR_HEIGHT + Math.max(insets.bottom, space.md) : insets.bottom;
  const clear = navBar + space.sm + (covered.fab ? 56 + space.lg : 0);
  const bottom = keyboard > 0 ? keyboard + space.lg : clear;
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

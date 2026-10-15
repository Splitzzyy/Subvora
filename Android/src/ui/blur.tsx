import { BlurTargetView, BlurView } from 'expo-blur';
import { useEffect, useRef, useState, type RefObject } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '@/theme';

// Android blurs a sibling, not "whatever is behind": a BlurView needs a ref to the BlurTargetView
// holding the content to blur, and must not sit inside it. Each BlurTarget registers here, so a
// backdrop blurs the innermost surface that was on screen when it opened.
const targets: RefObject<View | null>[] = [];

/** Content a Backdrop opened on top of it will blur. */
export function BlurTarget({ children }: { children: React.ReactNode }) {
  const ref = useRef<View>(null);
  useEffect(() => {
    targets.push(ref);
    return () => { targets.splice(targets.indexOf(ref), 1); };
  }, []);
  return <BlurTargetView ref={ref} style={{ flex: 1 }}>{children}</BlurTargetView>;
}

/** Blurred, lightly tinted backdrop for a modal surface. Pressing it dismisses, when it can. */
export function Backdrop({ onPress, label }: { onPress?: () => void; label?: string }) {
  const { isDark } = useTheme();
  // Captured once: a modal that registers its own BlurTarget afterwards must not start blurring itself.
  const [target] = useState(() => targets[targets.length - 1]);
  return (
    <Pressable style={StyleSheet.absoluteFill} onPress={onPress} disabled={!onPress} accessibilityLabel={label}>
      <BlurView
        style={StyleSheet.absoluteFill}
        blurTarget={target}
        // Real blur on Android 12+; older versions fall back to the tint alone.
        blurMethod="dimezisBlurViewSdk31Plus"
        intensity={60}
        tint={isDark ? 'dark' : 'light'}
      />
    </Pressable>
  );
}

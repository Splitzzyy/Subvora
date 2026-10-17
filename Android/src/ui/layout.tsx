import { useState } from 'react';
import { Animated, KeyboardAvoidingView, RefreshControl, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { brand, shape, space, useTheme } from '@/theme';
import { ScreenHeader, Text } from '.';
import { SubVoraMark } from './brand';

/**
 * Where the large title has scrolled away and the small top bar takes over: its container turns
 * opaque over the first few dp, then its title fades in. Fading both together left the bar
 * see-through wherever a scroll came to rest in between, the small title printed over the content.
 */
const COLLAPSE_FROM = 48;
const BAR_SOLID = 60;
const COLLAPSE_TO = 96;

/**
 * Scrolling tab/detail screen with pull-to-refresh. Leaves room at the bottom for a FAB. With a
 * `title` it gets the large, collapsing top app bar: a headline that scrolls with the content and a
 * small bar that fades in once it has gone.
 */
export function Screen({ children, refreshing, onRefresh, overlay, title, subtitle, actions, header }: {
  children: React.ReactNode; refreshing?: boolean; onRefresh?: () => void; overlay?: React.ReactNode;
  title?: string; subtitle?: string; actions?: React.ReactNode;
  /** A fixed bar above the scrolling content, e.g. a full-screen dialog's close and save. */
  header?: React.ReactNode;
}) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const [scrollY] = useState(() => new Animated.Value(0));
  // Where the scrolling area starts on screen (below the status bar and any fixed header). The
  // keyboard avoider compares its own frame, which is relative to its parent, with the keyboard's
  // screen position; without this it under-shrank by exactly that much, and a field focused low on
  // the screen (Settings' new password, the form's reminder days) sat behind the keyboard.
  const [top, setTop] = useState(insets.top);
  return (
    <View style={{ flex: 1, backgroundColor: colors.surface }}>
      {header ? (
        // M3 top app bar: flat on the surface at rest, surfaceContainer once content scrolls under
        // it, so the form no longer looks cut off at a bare edge. Runs to the top edge.
        <View style={{ paddingTop: insets.top }}>
          <Animated.View pointerEvents="none" style={[StyleSheet.absoluteFill, {
            backgroundColor: colors.surfaceContainer,
            opacity: scrollY.interpolate({ inputRange: [0, space.lg], outputRange: [0, 1], extrapolate: 'clamp' }),
          }]} />
          {header}
        </View>
      ) : <View style={{ height: insets.top }} />}
      <View style={{ flex: 1 }} onLayout={(e) => setTop(e.nativeEvent.layout.y)}>
        <KeyboardAvoidingView style={{ flex: 1 }} behavior="height" keyboardVerticalOffset={top}>
          <Animated.ScrollView
            keyboardShouldPersistTaps="handled"
            scrollEventThrottle={16}
            onScroll={title || header ? Animated.event([{ nativeEvent: { contentOffset: { y: scrollY } } }], { useNativeDriver: true }) : undefined}
            contentContainerStyle={{ paddingHorizontal: space.lg, paddingBottom: (overlay ? 104 : space.xxxl) + insets.bottom }}
            // The native Android indicator; it cannot be swapped for the Expressive one, so it is tinted from the tokens.
            refreshControl={onRefresh ? (
              <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh} colors={[colors.primary]} progressBackgroundColor={colors.surfaceContainerHigh} />
            ) : undefined}
          >
            {title ? <ScreenHeader title={title} subtitle={subtitle} actions={actions} /> : null}
            {children}
          </Animated.ScrollView>
        </KeyboardAvoidingView>
        {overlay}
      </View>
      {title ? (
        <Animated.View
          pointerEvents="none"
          // The large headline is the one TalkBack reads; this is its visual echo. It covers the
          // status bar strip too, so the bar colour runs to the top edge.
          importantForAccessibility="no-hide-descendants"
          accessibilityElementsHidden
          style={{
            position: 'absolute', top: 0, left: 0, right: 0, minHeight: 56 + insets.top, paddingTop: insets.top, justifyContent: 'center', paddingHorizontal: space.lg,
          }}
        >
          <Animated.View style={[StyleSheet.absoluteFill, {
            backgroundColor: colors.surfaceContainer,
            opacity: scrollY.interpolate({ inputRange: [COLLAPSE_FROM, BAR_SOLID], outputRange: [0, 1], extrapolate: 'clamp' }),
          }]} />
          <Animated.View style={{ opacity: scrollY.interpolate({ inputRange: [BAR_SOLID, COLLAPSE_TO], outputRange: [0, 1], extrapolate: 'clamp' }) }}>
            <Text variant="titleLarge" numberOfLines={1}>{title}</Text>
          </Animated.View>
        </Animated.View>
      ) : null}
    </View>
  );
}

/** Brand mark + headline for the signed-out screens. */
export function AuthScaffold({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <Screen>
      <View style={{ paddingTop: space.huge, paddingBottom: space.xxxl, gap: space.xl }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
          {/* The launcher icon in miniature: the mark on its midnight squircle. */}
          <View accessible={false} style={{ width: 56, height: 56, borderRadius: shape.medium + space.xs, backgroundColor: brand.night, alignItems: 'center', justifyContent: 'center' }}>
            <SubVoraMark size={44} />
          </View>
          <View style={{ flex: 1 }}>
            <Text variant="titleLargeEmphasized" color={colors.primary}>SubVora</Text>
            <Text variant="bodySmall" color={colors.onSurfaceVariant}>The smart way to manage subscriptions.</Text>
          </View>
        </View>
        <View style={{ gap: space.sm }}>
          <Text variant="displaySmall" accessibilityRole="header">{title}</Text>
          <Text variant="bodyLarge" color={colors.onSurfaceVariant}>{subtitle}</Text>
        </View>
      </View>
      <View style={{ gap: space.lg }}>{children}</View>
    </Screen>
  );
}

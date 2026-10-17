import { Tabs } from 'expo-router';
import type { BottomTabBarProps } from 'expo-router/tabs';
import { useEffect, useState } from 'react';
import { Animated, Keyboard, Pressable, useWindowDimensions, View } from 'react-native';
import * as haptics from '@/lib/haptics';
import { useReducedMotion } from '@/lib/motion';
import { motion, space, useTheme } from '@/theme';
import { Icon, RAIL_MIN_WIDTH, Text, TOUCH, type IconName } from '@/ui';
import { SubVoraMark } from '@/ui/brand';
import { useObstruction } from '@/ui/obstructions';

const DESTINATIONS: Record<string, { title: string; icon: IconName; selected: IconName }> = {
  index: { title: 'Home', icon: 'home-variant-outline', selected: 'home-variant' },
  subscriptions: { title: 'Subs', icon: 'calendar-sync-outline', selected: 'calendar-sync' },
  categories: { title: 'Categories', icon: 'shape-outline', selected: 'shape' },
  payments: { title: 'Payment', icon: 'credit-card-outline', selected: 'credit-card' },
  settings: { title: 'Settings', icon: 'cog-outline', selected: 'cog' },
};

const INDICATOR_HEIGHT = 32;

/** Hidden while the keyboard is up, as the stock bar's tabBarHideOnKeyboard did. */
function useKeyboardVisible() {
  const [visible, setVisible] = useState(false);
  useEffect(() => {
    const show = Keyboard.addListener('keyboardDidShow', () => setVisible(true));
    const hide = Keyboard.addListener('keyboardDidHide', () => setVisible(false));
    return () => { show.remove(); hide.remove(); };
  }, []);
  return visible;
}

/**
 * M3 navigation bar on phones, navigation rail from 600dp (tablets, unfolded foldables). The
 * selected destination gets the pill indicator behind its filled icon.
 */
/**
 * The pill behind the selected icon. It always has its colour and only fades (Android dropped the
 * corner radius when a background appeared on an existing view), and grows out from the icon as
 * M3's indicator does. Native driver: opacity and scale only.
 */
function ActiveIndicator({ focused }: { focused: boolean }) {
  const { colors } = useTheme();
  const reduced = useReducedMotion();
  const [p] = useState(() => new Animated.Value(focused ? 1 : 0));
  useEffect(() => {
    if (reduced) p.setValue(focused ? 1 : 0);
    else Animated.spring(p, { toValue: focused ? 1 : 0, ...motion.spatialFast, useNativeDriver: true }).start();
  }, [focused, reduced, p]);
  return (
    <Animated.View style={{
      position: 'absolute', top: 0, right: 0, bottom: 0, left: 0, borderRadius: INDICATOR_HEIGHT / 2,
      backgroundColor: colors.secondaryContainer,
      opacity: p.interpolate({ inputRange: [0, 1], outputRange: [0, 1], extrapolate: 'clamp' }),
      transform: [{ scaleX: p.interpolate({ inputRange: [0, 1], outputRange: [0.4, 1] }) }],
    }} />
  );
}

function NavigationBar({ state, navigation, insets, rail }: BottomTabBarProps & { rail: boolean }) {
  const { colors } = useTheme();
  const keyboard = useKeyboardVisible();
  useObstruction('navBar', !rail);
  if (keyboard && !rail) return null;

  const items = state.routes.map((route, index) => {
    const focused = state.index === index;
    const d = DESTINATIONS[route.name] ?? { title: route.name, icon: 'circle-outline', selected: 'circle' };
    const onPress = () => {
      const event = navigation.emit({ type: 'tabPress', target: route.key, canPreventDefault: true });
      if (!focused && !event.defaultPrevented) navigation.navigate(route.name, route.params);
    };
    return (
      <Pressable
        key={route.key} onPress={onPress}
        accessibilityRole="tab" accessibilityState={{ selected: focused }} accessibilityLabel={d.title}
        android_ripple={{ color: colors.outlineVariant, borderless: true, radius: TOUCH / 2 + space.sm }}
        style={{ flex: rail ? undefined : 1, minHeight: TOUCH + space.lg, alignItems: 'center', justifyContent: 'center', gap: space.xs, paddingVertical: space.xs }}
      >
        <View style={{ width: 56, height: INDICATOR_HEIGHT, alignItems: 'center', justifyContent: 'center' }}>
          <ActiveIndicator focused={focused} />
          <Icon name={focused ? d.selected : d.icon} size={24} color={focused ? colors.onSecondaryContainer : colors.onSurfaceVariant} />
        </View>
        <Text variant={focused ? 'labelMediumEmphasized' : 'labelMedium'} color={focused ? colors.onSurface : colors.onSurfaceVariant}
          // Five labels share a phone's width; with large text they shrink to fit rather than truncate.
          numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.6} maxFontSizeMultiplier={1.3}>{d.title}</Text>
      </Pressable>
    );
  });

  if (rail) {
    return (
      <View accessibilityRole="tablist" style={{
        width: 96, backgroundColor: colors.surfaceContainer, alignItems: 'center', gap: space.md,
        paddingTop: insets.top + space.xl, paddingBottom: insets.bottom + space.lg, paddingLeft: insets.left,
      }}>
        <View style={{ paddingBottom: space.lg }} accessible={false}><SubVoraMark size={40} monochrome color={colors.primary} /></View>
        {items}
      </View>
    );
  }
  return (
    <View accessibilityRole="tablist" style={{
      flexDirection: 'row', backgroundColor: colors.surfaceContainer, paddingTop: space.md,
      paddingBottom: Math.max(insets.bottom, space.md), paddingHorizontal: space.sm,
    }}>
      {items}
    </View>
  );
}

export default function TabsLayout() {
  const { colors } = useTheme();
  const reduced = useReducedMotion();
  const rail = useWindowDimensions().width >= RAIL_MIN_WIDTH;
  return (
    <Tabs
      // Back walks the tabs you actually visited before leaving the app, not straight to Home.
      backBehavior="history"
      tabBar={(props) => <NavigationBar {...props} rail={rail} />}
      // M3 fades between top-level destinations rather than sliding: they are peers, not a sequence.
      screenOptions={{ headerShown: false, sceneStyle: { backgroundColor: colors.surface }, tabBarPosition: rail ? 'left' : 'bottom', animation: reduced ? 'none' : 'fade' }}
      screenListeners={{ tabPress: () => haptics.tick() }}
    >
      {Object.entries(DESTINATIONS).map(([name, { title }]) => <Tabs.Screen key={name} name={name} options={{ title }} />)}
    </Tabs>
  );
}

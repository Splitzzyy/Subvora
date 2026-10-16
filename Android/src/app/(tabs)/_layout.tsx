import { Tabs } from 'expo-router';
import type { BottomTabBarProps } from 'expo-router/tabs';
import { useEffect, useState } from 'react';
import { Keyboard, Pressable, useWindowDimensions, View } from 'react-native';
import * as haptics from '@/lib/haptics';
import { shape, space, useTheme } from '@/theme';
import { Icon, RAIL_MIN_WIDTH, Text, TOUCH, type IconName } from '@/ui';
import { SubVoraMark } from '@/ui/brand';

const DESTINATIONS: Record<string, { title: string; icon: IconName; selected: IconName }> = {
  index: { title: 'Home', icon: 'home-variant-outline', selected: 'home-variant' },
  subscriptions: { title: 'Subs', icon: 'calendar-sync-outline', selected: 'calendar-sync' },
  categories: { title: 'Categories', icon: 'shape-outline', selected: 'shape' },
  payments: { title: 'Payment', icon: 'credit-card-outline', selected: 'credit-card' },
  settings: { title: 'Settings', icon: 'cog-outline', selected: 'cog' },
};

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
function NavigationBar({ state, navigation, insets, rail }: BottomTabBarProps & { rail: boolean }) {
  const { colors } = useTheme();
  const keyboard = useKeyboardVisible();
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
        <View style={{ width: 56, height: 32, borderRadius: shape.full, alignItems: 'center', justifyContent: 'center', backgroundColor: focused ? colors.secondaryContainer : 'transparent' }}>
          <Icon name={focused ? d.selected : d.icon} size={24} color={focused ? colors.onSecondaryContainer : colors.onSurfaceVariant} />
        </View>
        <Text variant={focused ? 'labelMediumEmphasized' : 'labelMedium'} color={focused ? colors.onSurface : colors.onSurfaceVariant} numberOfLines={1}>{d.title}</Text>
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
  const rail = useWindowDimensions().width >= RAIL_MIN_WIDTH;
  return (
    <Tabs
      // Back walks the tabs you actually visited before leaving the app, not straight to Home.
      backBehavior="history"
      tabBar={(props) => <NavigationBar {...props} rail={rail} />}
      screenOptions={{ headerShown: false, sceneStyle: { backgroundColor: colors.surface }, tabBarPosition: rail ? 'left' : 'bottom' }}
      screenListeners={{ tabPress: () => haptics.tick() }}
    >
      {Object.entries(DESTINATIONS).map(([name, { title }]) => <Tabs.Screen key={name} name={name} options={{ title }} />)}
    </Tabs>
  );
}

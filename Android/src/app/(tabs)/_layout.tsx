import { Tabs } from 'expo-router';
import { createContext, useContext, useState } from 'react';
import { Pressable, StyleSheet, View, type PressableProps } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as haptics from '@/lib/haptics';
import { typescale, useTheme } from '@/theme';
import { Icon, type IconName } from '@/ui';
import { BlurTarget } from '@/ui/blur';

const DESTINATIONS: { name: string; title: string; icon: IconName; selected: IconName }[] = [
  { name: 'index', title: 'Home', icon: 'home-variant-outline', selected: 'home-variant' },
  { name: 'subscriptions', title: 'Subs', icon: 'calendar-sync-outline', selected: 'calendar-sync' },
  { name: 'categories', title: 'Categories', icon: 'shape-outline', selected: 'shape' },
  { name: 'payments', title: 'Payment', icon: 'credit-card-outline', selected: 'credit-card' },
  { name: 'settings', title: 'Settings', icon: 'cog-outline', selected: 'cog' },
];

const Pressed = createContext(false);

/** M3 navigation bar: pill-shaped active indicator behind the selected destination's filled icon. */
function Destination({ icon, focused }: { icon: IconName; focused: boolean }) {
  const { colors } = useTheme();
  const pressed = useContext(Pressed);
  return (
    <View style={{ width: 56, height: 32, borderRadius: 16, overflow: 'hidden', alignItems: 'center', justifyContent: 'center', backgroundColor: focused ? colors.secondaryContainer : 'transparent' }}>
      {/* The pressed state layer lives on the indicator pill, not the whole destination. */}
      {pressed ? <View style={[StyleSheet.absoluteFill, { backgroundColor: focused ? colors.onSecondaryContainer : colors.onSurface, opacity: 0.1 }]} /> : null}
      <Icon name={icon} size={24} color={focused ? colors.onSecondaryContainer : colors.onSurfaceVariant} />
    </View>
  );
}

/**
 * The navigator's default button draws a borderless Android ripple that spreads well past the
 * destination, over the neighbouring tabs. This one draws none and tells the pill it is pressed.
 */
function TabButton({ children, onPressIn, onPressOut, ...props }: PressableProps & { children?: React.ReactNode }) {
  const [pressed, setPressed] = useState(false);
  return (
    <Pressable
      {...props}
      android_ripple={null}
      onPressIn={(e) => { setPressed(true); onPressIn?.(e); }}
      onPressOut={(e) => { setPressed(false); onPressOut?.(e); }}
    >
      <Pressed.Provider value={pressed}>{children}</Pressed.Provider>
    </Pressable>
  );
}

export default function TabsLayout() {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    // What the edit screen and the bottom sheets blur behind themselves.
    <BlurTarget>
    <Tabs
      // Back walks the tabs you actually visited before leaving the app, not straight to Home.
      backBehavior="history"
      screenOptions={{
        headerShown: false,
        sceneStyle: { backgroundColor: colors.surface },
        tabBarActiveTintColor: colors.onSurface,
        tabBarInactiveTintColor: colors.onSurfaceVariant,
        tabBarHideOnKeyboard: true,
        tabBarLabelStyle: { ...typescale.labelMedium, marginTop: 4 },
        tabBarStyle: {
          backgroundColor: colors.surfaceContainer, borderTopWidth: 0, elevation: 0,
          height: 80 + insets.bottom, paddingTop: 12, paddingBottom: Math.max(insets.bottom, 12),
        },
      }}
      screenListeners={{ tabPress: () => haptics.tick() }}
    >
      {DESTINATIONS.map(({ name, title, icon, selected }) => (
        <Tabs.Screen key={name} name={name} options={{
          title,
          tabBarAccessibilityLabel: title,
          tabBarIcon: ({ focused }) => <Destination icon={focused ? selected : icon} focused={focused} />,
          tabBarButton: ({ android_ripple: _ripple, pressColor: _color, pressOpacity: _opacity, hoverEffect: _hover, href: _href, ...props }) => <TabButton {...props} />,
        }} />
      ))}
    </Tabs>
    </BlurTarget>
  );
}

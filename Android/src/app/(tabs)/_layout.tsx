import { Tabs } from 'expo-router';
import { View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as haptics from '@/lib/haptics';
import { typescale, useTheme } from '@/theme';
import { Icon, type IconName } from '@/ui';

const DESTINATIONS: { name: string; title: string; icon: IconName; selected: IconName }[] = [
  { name: 'index', title: 'Home', icon: 'home-variant-outline', selected: 'home-variant' },
  { name: 'subscriptions', title: 'Subs', icon: 'calendar-sync-outline', selected: 'calendar-sync' },
  { name: 'categories', title: 'Categories', icon: 'shape-outline', selected: 'shape' },
  { name: 'payments', title: 'Payment', icon: 'credit-card-outline', selected: 'credit-card' },
  { name: 'settings', title: 'Settings', icon: 'cog-outline', selected: 'cog' },
];

/** M3 navigation bar: pill-shaped active indicator behind the selected destination's filled icon. */
function Destination({ icon, focused }: { icon: IconName; focused: boolean }) {
  const { colors } = useTheme();
  return (
    <View style={{ width: 56, height: 32, borderRadius: 16, alignItems: 'center', justifyContent: 'center', backgroundColor: focused ? colors.secondaryContainer : 'transparent' }}>
      <Icon name={icon} size={24} color={focused ? colors.onSecondaryContainer : colors.onSurfaceVariant} />
    </View>
  );
}

export default function TabsLayout() {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <Tabs
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
        }} />
      ))}
    </Tabs>
  );
}

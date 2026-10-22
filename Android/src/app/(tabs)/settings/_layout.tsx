import { Stack } from 'expo-router';
import { useReducedMotion } from '@/lib/motion';
import { useTheme } from '@/theme';

/**
 * Settings and the set-up screens it leads to (#279): Categories and Payment sources left the tab
 * bar - they are visited once, then rarely - and open from here with a back arrow, the tab bar still
 * showing Settings.
 */
export default function SettingsLayout() {
  const { colors } = useTheme();
  const reduced = useReducedMotion();
  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.surface }, animation: reduced ? 'fade' : 'slide_from_right' }} />;
}

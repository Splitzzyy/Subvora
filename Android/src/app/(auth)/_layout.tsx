import { Stack } from 'expo-router';
import { useTheme } from '@/theme';

/** Where the root stack's guard lands when the session ends. */
export const unstable_settings = { initialRouteName: 'login' };

export default function AuthLayout() {
  const { colors } = useTheme();
  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.surface }, animation: 'slide_from_right' }} />;
}

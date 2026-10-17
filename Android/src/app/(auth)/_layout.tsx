import { Stack } from 'expo-router';
import { useReducedMotion } from '@/lib/motion';
import { useTheme } from '@/theme';

/** Where the root stack's guard lands when the session ends. */
export const unstable_settings = { initialRouteName: 'login' };

export default function AuthLayout() {
  const { colors } = useTheme();
  // Shared-axis X between the sign-in screens, approximated by the native slide; a fade with reduced motion.
  const reduced = useReducedMotion();
  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.surface }, animation: reduced ? 'fade' : 'slide_from_right' }} />;
}

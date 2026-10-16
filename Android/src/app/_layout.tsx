import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import * as SystemUI from 'expo-system-ui';
import { useEffect } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SessionProvider, useSession } from '@/lib/session';
import { useReducedMotion } from '@/lib/motion';
import { ThemeProvider, useTheme } from '@/theme';
import { SnackbarProvider } from '@/ui/snackbar';

SplashScreen.preventAutoHideAsync().catch(() => undefined);

function RootStack() {
  const { status } = useSession();
  const { colors, isDark, surfaceHex } = useTheme();
  const reduced = useReducedMotion();

  useEffect(() => {
    SystemUI.setBackgroundColorAsync(surfaceHex).catch(() => undefined);
  }, [surfaceHex]);

  useEffect(() => {
    if (status !== 'loading') SplashScreen.hideAsync().catch(() => undefined);
  }, [status]);

  if (status === 'loading') return null;

  const signedIn = status === 'signedIn';
  return (
    <>
      <StatusBar style={isDark ? 'light' : 'dark'} />
      {/* Protected groups redirect on their own when the session starts or ends - including a
          refresh the server refused, which flips status to signedOut from inside the API client. */}
      {/* Native-stack approximations of M3 shared-axis transitions: list -> add/edit rises on the
          Y axis. A plain fade with reduced motion. */}
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.surface }, animation: reduced ? 'fade' : 'fade_from_bottom' }}>
        <Stack.Protected guard={signedIn}>
          <Stack.Screen name="(tabs)" />
          <Stack.Screen name="subscription/[id]" options={{ presentation: 'modal', animation: reduced ? 'fade' : 'fade_from_bottom' }} />
        </Stack.Protected>
        <Stack.Protected guard={!signedIn}>
          <Stack.Screen name="(auth)" />
        </Stack.Protected>
      </Stack>
    </>
  );
}

export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <ThemeProvider>
        <SessionProvider>
          <SnackbarProvider>
            <RootStack />
          </SnackbarProvider>
        </SessionProvider>
      </ThemeProvider>
    </SafeAreaProvider>
  );
}

import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import * as SystemUI from 'expo-system-ui';
import { useCallback, useEffect, useState } from 'react';
import { View } from 'react-native';
import NetInfo from '@react-native-community/netinfo';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { setDeviceOffline } from '@/api';
import { SessionProvider, useSession } from '@/lib/session';
import { useReducedMotion } from '@/lib/motion';
import { ThemeProvider, useTheme } from '@/theme';
import { LaunchHandoff, SPLASH_MARK_SIZE, SubVoraLoader, useSplashLook } from '@/ui/brand';
import { SnackbarProvider } from '@/ui/snackbar';

SplashScreen.preventAutoHideAsync().catch(() => undefined);

/** Past this the splash goes and the brand loader waits instead, so a slow keystore never looks frozen. */
const SPLASH_TIMEOUT_MS = 3000;

function RootStack() {
  const { status } = useSession();
  const { colors, isDark, surfaceHex } = useTheme();
  const reduced = useReducedMotion();
  const [timedOut, setTimedOut] = useState(false);
  const [handingOff, setHandingOff] = useState(true);
  const endHandoff = useCallback(() => setHandingOff(false), []);
  const splash = useSplashLook();

  useEffect(() => {
    SystemUI.setBackgroundColorAsync(surfaceHex).catch(() => undefined);
  }, [surfaceHex]);

  // Lets the API client tell "offline" from "the server didn't answer" in its messages.
  useEffect(() => NetInfo.addEventListener((s) => setDeviceOffline(s.isConnected === false)), []);

  useEffect(() => {
    const timer = setTimeout(() => setTimedOut(true), SPLASH_TIMEOUT_MS);
    return () => clearTimeout(timer);
  }, []);

  // Ready means the stored tokens have been read. Fonts are embedded and theme preferences are read
  // synchronously, so neither needs waiting for; the dashboard fetch deliberately does not hold the splash.
  const ready = status !== 'loading';
  useEffect(() => {
    if (ready || timedOut) SplashScreen.hideAsync().catch(() => undefined);
  }, [ready, timedOut]);

  if (!ready) {
    return timedOut ? (
      <View style={{ flex: 1, backgroundColor: splash.background, alignItems: 'center', justifyContent: 'center' }}>
        <StatusBar style={splash.statusBar} />
        <SubVoraLoader size={SPLASH_MARK_SIZE} tone={splash.tone} />
      </View>
    ) : null;
  }

  const signedIn = status === 'signedIn';
  return (
    <>
      <StatusBar style={handingOff ? splash.statusBar : isDark ? 'light' : 'dark'} />
      {/* Protected groups redirect on their own when the session starts or ends - including a
          refresh the server refused, which flips status to signedOut from inside the API client. */}
      {/* Native-stack approximations of M3 shared-axis transitions: list -> add/edit rises on the
          Y axis. A plain fade with reduced motion. */}
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.surface }, animation: reduced ? 'fade' : 'fade_from_bottom' }}>
        <Stack.Protected guard={signedIn}>
          <Stack.Screen name="(tabs)" />
          <Stack.Screen name="subscription/[id]" options={{ animation: reduced ? 'fade' : 'slide_from_right' }} />
          <Stack.Screen name="subscription/new" options={{ presentation: 'modal', animation: reduced ? 'fade' : 'fade_from_bottom' }} />
          <Stack.Screen name="subscription/edit/[id]" options={{ presentation: 'modal', animation: reduced ? 'fade' : 'fade_from_bottom' }} />
        </Stack.Protected>
        <Stack.Protected guard={!signedIn}>
          <Stack.Screen name="(auth)" />
        </Stack.Protected>
      </Stack>
      {/* Same background and mark as the native splash, so its exit is invisible. */}
      {handingOff ? <LaunchHandoff onDone={endHandoff} /> : null}
    </>
  );
}

export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <ThemeProvider>
        {/* Outside the session, which clears it on every sign-in and sign-out. */}
        <SnackbarProvider>
          <SessionProvider>
            <RootStack />
          </SessionProvider>
        </SnackbarProvider>
      </ThemeProvider>
    </SafeAreaProvider>
  );
}

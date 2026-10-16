import { KeyboardAvoidingView, RefreshControl, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { shape, useTheme } from '@/theme';
import { Icon, Text } from '.';
import { Backdrop, BlurTarget } from './blur';

/** Scrolling tab/detail screen with pull-to-refresh. Leaves room at the bottom for a FAB. */
export function Screen({ children, refreshing, onRefresh, overlay, inset = true }: {
  children: React.ReactNode; refreshing?: boolean; onRefresh?: () => void; overlay?: React.ReactNode;
  /** False when something above already clears the status bar, as ModalScreen does. */
  inset?: boolean;
}) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <View style={{ flex: 1, backgroundColor: colors.surface, paddingTop: inset ? insets.top : 0 }}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior="height">
        <ScrollView
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={{ paddingHorizontal: 16, paddingBottom: (overlay ? 104 : 32) + insets.bottom }}
          refreshControl={onRefresh ? (
            <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh}
              colors={[colors.primary as string]} progressBackgroundColor={colors.surfaceContainerHigh as string} />
          ) : undefined}
        >
          {children}
        </ScrollView>
      </KeyboardAvoidingView>
      {overlay}
    </View>
  );
}

/**
 * A full-height sheet over a blurred view of the screen it was opened from, instead of a black
 * card transition. Needs `presentation: 'transparentModal'` so that screen stays drawn behind it.
 * The backdrop does not dismiss: a stray tap above the sheet must not throw away a half-filled form.
 */
export function ModalScreen({ children }: { children: React.ReactNode }) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <View style={{ flex: 1 }}>
      <Backdrop />
      <View style={{
        flex: 1, marginTop: insets.top + 40, overflow: 'hidden', backgroundColor: colors.surface,
        borderTopLeftRadius: shape.xl, borderTopRightRadius: shape.xl,
      }}>
        {/* Its own target, so a picker sheet opened inside blurs this form rather than the tabs. */}
        <BlurTarget>{children}</BlurTarget>
      </View>
    </View>
  );
}

/** Brand mark + headline for the signed-out screens. */
export function AuthScaffold({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <Screen>
      <View style={{ paddingTop: 48, paddingBottom: 32, gap: 20 }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
          <View style={{ width: 56, height: 56, borderRadius: shape.lg + 4, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center', transform: [{ rotate: '-10deg' }] }}>
            <Icon name="autorenew" size={32} color={colors.onPrimary} style={{ transform: [{ rotate: '10deg' }] }} />
          </View>
          <Text variant="titleLarge" color={colors.primary}>SubVora</Text>
        </View>
        <View style={{ gap: 8 }}>
          <Text variant="displaySmall" accessibilityRole="header">{title}</Text>
          <Text variant="bodyLarge" color={colors.onSurfaceVariant}>{subtitle}</Text>
        </View>
      </View>
      <View style={{ gap: 16 }}>{children}</View>
    </Screen>
  );
}

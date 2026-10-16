import { KeyboardAvoidingView, RefreshControl, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { shape, useTheme } from '@/theme';
import { Icon, Text } from '.';

/** Scrolling tab/detail screen with pull-to-refresh. Leaves room at the bottom for a FAB. */
export function Screen({ children, refreshing, onRefresh, overlay }: {
  children: React.ReactNode; refreshing?: boolean; onRefresh?: () => void; overlay?: React.ReactNode;
}) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <View style={{ flex: 1, backgroundColor: colors.surface, paddingTop: insets.top }}>
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

/** Brand mark + headline for the signed-out screens. */
export function AuthScaffold({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  const { colors } = useTheme();
  return (
    <Screen>
      <View style={{ paddingTop: 48, paddingBottom: 32, gap: 20 }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
          <View style={{ width: 56, height: 56, borderRadius: shape.medium + 4, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center', transform: [{ rotate: '-10deg' }] }}>
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

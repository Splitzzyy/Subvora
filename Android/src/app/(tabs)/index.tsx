import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { dashboardApi, type BurnRate } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import { useLoader } from '@/lib/hooks';
import { burnRateWarning, formatMoney, topSpendSummary } from '@/lib/logic';
import { shape, useTheme } from '@/theme';
import { Banner, Button, ButtonGroup, Card, EmptyState, ProgressBar, ScreenHeader, Spinner, Text, Tile } from '@/ui';
import { Screen } from '@/ui/layout';

type Period = 'weekly' | 'monthly' | 'yearly';
const PERIOD_LABEL: Record<Period, string> = { weekly: 'per week', monthly: 'per month', yearly: 'per year' };

export default function Dashboard() {
  const { colors, category, payment } = useTheme();
  const { data, loading, error, cached, reload } = useLoader<BurnRate>(dashboardApi.burnRate, CACHE_KEYS.burnRate);
  const [period, setPeriod] = useState<Period>('monthly');

  const warning = data ? burnRateWarning(data.unresolvedSubscriptionIds.length, data.oldestRateFetchedAt) : null;
  const topSource = data ? topSpendSummary(data.byPaymentSource) : null;
  // Bars are normalised against the leader, not the total: across eight categories a share-of-total
  // bar is a stub for everything below first place.
  const maxOf = (items: { monthlyAmount: number }[]) => Math.max(0, ...items.map((i) => i.monthlyAmount));
  const empty = data && data.yearly === 0 && data.oneTimeThisYear === 0 && data.byCategory.length === 0;

  return (
    <Screen refreshing={loading && !!data} onRefresh={reload}>
      <ScreenHeader subtitle="Burn rate" title="Overview" />

      {cached ? <Banner tone="warning" text="Offline — showing last synced data." /> : null}
      {error ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}
      {warning ? <Banner tone="info" text={warning} /> : null}
      {!data && loading ? <Spinner /> : null}

      {data ? (
        <View style={{ gap: 12 }}>
          {/* Hero: the one number this app exists to show, in display type on the primary container. */}
          <Card tone="primary" style={{ borderRadius: shape.xxl, paddingVertical: 28, gap: 20 }}>
            <View style={{ gap: 4 }}>
              <Text variant="labelLarge" color={colors.onPrimaryContainer}>You spend</Text>
              <Text variant="displayLarge" color={colors.onPrimaryContainer} adjustsFontSizeToFit numberOfLines={1}>
                {formatMoney(data[period], data.homeCurrency)}
              </Text>
              <Text variant="titleMedium" color={colors.onPrimaryContainer}>{PERIOD_LABEL[period]} on subscriptions</Text>
            </View>
            <ButtonGroup<Period>
              value={period}
              onChange={setPeriod}
              options={[{ value: 'weekly', label: 'Week' }, { value: 'monthly', label: 'Month' }, { value: 'yearly', label: 'Year' }]}
            />
          </Card>

          {data.oneTimeThisYear > 0 ? (
            <Card tone="tertiary" style={{ flexDirection: 'row', alignItems: 'center', gap: 16 }}>
              <Tile icon="cash-check" color={colors.onTertiaryContainer} background={colors.surfaceContainerLowest} />
              <View style={{ flex: 1 }}>
                <Text variant="labelLarge" color={colors.onTertiaryContainer}>One-time payments this year</Text>
                <Text variant="headlineSmall" color={colors.onTertiaryContainer}>{formatMoney(data.oneTimeThisYear, data.homeCurrency)}</Text>
              </View>
            </Card>
          ) : null}

          {empty ? (
            <EmptyState icon="rocket-launch-outline" title="Nothing tracked yet"
              body="Add your first subscription and your burn rate shows up here."
              action={<Button label="Add subscription" icon="plus" onPress={() => router.push('/subscription/new')} />} />
          ) : null}

          {data.byCategory.length > 0 ? (
            <Card>
              <Text variant="titleLarge">By category</Text>
              <Text variant="bodyMedium" color={colors.onSurfaceVariant}>Monthly, in {data.homeCurrency}</Text>
              {data.byCategory.map((item) => {
                const id = category(item.categoryName);
                return (
                  <View key={item.categoryId ?? item.categoryName} style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
                    <Tile icon={id.icon} color={id.color} background={id.soft} size={40} />
                    <View style={{ flex: 1, gap: 6 }}>
                      <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 8 }}>
                        <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{item.categoryName}</Text>
                        <Text variant="labelLarge">{formatMoney(item.monthlyAmount, data.homeCurrency)}</Text>
                      </View>
                      <ProgressBar value={item.monthlyAmount / (maxOf(data.byCategory) || 1)} color={id.color} />
                    </View>
                  </View>
                );
              })}
            </Card>
          ) : null}

          {data.byPaymentSource.length > 0 ? (
            <Card>
              <Text variant="titleLarge">By payment source</Text>
              {topSource ? <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{topSource}</Text> : null}
              {data.byPaymentSource.map((item) => {
                // The breakdown carries no type, so every row wears the card tint.
                const id = payment('Card');
                return (
                  <View key={item.paymentSourceId ?? item.paymentSourceLabel} style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
                    <Tile icon={item.paymentSourceId ? id.icon : 'help-circle-outline'} color={id.color} background={id.soft} size={40} />
                    <View style={{ flex: 1, gap: 6 }}>
                      <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 8 }}>
                        <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{item.paymentSourceLabel}</Text>
                        <Text variant="labelLarge">{formatMoney(item.monthlyAmount, data.homeCurrency)}</Text>
                      </View>
                      <ProgressBar value={item.monthlyAmount / (maxOf(data.byPaymentSource) || 1)} color={colors.primary} />
                    </View>
                  </View>
                );
              })}
            </Card>
          ) : null}
        </View>
      ) : null}
    </Screen>
  );
}

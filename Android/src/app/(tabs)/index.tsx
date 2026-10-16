import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { dashboardApi, type BurnRate } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { burnRateWarning, formatMoney, formatSavedAt, topCategoryShare, topSpendSummary } from '@/lib/logic';
import { shape, space, useTheme } from '@/theme';
import { Banner, Button, ButtonGroup, Card, EmptyState, ProgressBar, Text, Tile } from '@/ui';
import { RingMark } from '@/ui/brand';
import { Screen } from '@/ui/layout';
import { AsyncView, DashboardSkeleton } from '@/ui/states';

type Period = 'weekly' | 'monthly' | 'yearly';
const PERIOD_LABEL: Record<Period, string> = { weekly: 'per week', monthly: 'per month', yearly: 'per year' };

export default function Dashboard() {
  const { colors, category, payment } = useTheme();
  const offline = useOffline();
  const { data, loading, error, errorStatus, cached, cachedAt, reload } = useLoader<BurnRate>(dashboardApi.burnRate, CACHE_KEYS.burnRate);
  const [period, setPeriod] = useState<Period>('monthly');

  const warning = data ? burnRateWarning(data.unresolvedSubscriptionIds.length, data.oldestRateFetchedAt) : null;
  const topSource = data ? topSpendSummary(data.byPaymentSource) : null;
  const topCategory = data ? topCategoryShare(data.byCategory) : null;
  // Bars are normalised against the leader, not the total: across eight categories a share-of-total
  // bar is a stub for everything below first place.
  const maxOf = (items: { monthlyAmount: number }[]) => Math.max(0, ...items.map((i) => i.monthlyAmount));
  const empty = !!data && data.yearly === 0 && data.oneTimeThisYear === 0 && data.byCategory.length === 0;

  return (
    <Screen title="Overview" subtitle="Burn rate" refreshing={loading && !!data} onRefresh={reload}>
      {cached ? <Banner tone="warning" text={cachedAt ? `Offline — showing data from ${formatSavedAt(cachedAt)}.` : 'Offline — showing last synced data.'} /> : null}
      {/* A failed reload over live figures: keep them, say so. */}
      {error && data ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}
      {warning ? <Banner tone="info" text={warning} /> : null}

      <AsyncView
        state={asyncState({ hasData: !!data, empty, error, errorStatus })}
        skeleton={<DashboardSkeleton />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
        empty={(
          <EmptyState icon="rocket-launch-outline" title="Nothing tracked yet"
            body="Add your first subscription and your burn rate shows up here."
            action={<Button label="Add subscription" icon="plus" onPress={() => router.push('/subscription/new')} disabled={offline} />} />
        )}
      >
        {data ? (
          <View style={{ gap: space.md }}>
            {/* Hero: the one number this app exists to show, in display type on the primary container. */}
            <Card tone="primary" style={{ borderRadius: shape.extraLarge, paddingVertical: space.xxl + space.xs, gap: space.xl }}>
              <View style={{ gap: space.xs }}>
                <Text variant="labelLarge" color={colors.onPrimaryContainer}>You spend</Text>
                <Text variant="amountDisplay" color={colors.onPrimaryContainer} adjustsFontSizeToFit numberOfLines={1}>
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

            {/* The Renewal Ring: how much of the month's spend the biggest category takes. */}
            {topCategory ? (
              <Card
                style={{ flexDirection: 'row', alignItems: 'center', gap: space.xl }}
              >
                <View accessible accessibilityRole="image" accessibilityLabel={`${topCategory.name} is ${Math.round(topCategory.share * 100)}% of your monthly spend`}>
                  <RingMark size={112} progress={topCategory.share} center={(
                    <Text variant="amountTitle" allowFontScaling={false}>{Math.round(topCategory.share * 100)}%</Text>
                  )} />
                </View>
                <View style={{ flex: 1, gap: space.xs }} importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
                  <Text variant="labelLarge" color={colors.onSurfaceVariant}>Biggest category</Text>
                  <Text variant="titleLargeEmphasized" numberOfLines={2}>{topCategory.name}</Text>
                  <Text variant="bodyMedium" color={colors.onSurfaceVariant}>
                    {Math.round(topCategory.share * 100)}% of your monthly spend
                  </Text>
                </View>
              </Card>
            ) : null}

            {data.oneTimeThisYear > 0 ? (
              <Card tone="tertiary" style={{ flexDirection: 'row', alignItems: 'center', gap: space.lg }}>
                <Tile icon="cash-check" color={colors.onTertiaryContainer} background={colors.surfaceContainerLowest} />
                <View style={{ flex: 1 }}>
                  <Text variant="labelLarge" color={colors.onTertiaryContainer}>One-time payments this year</Text>
                  <Text variant="amountHeadline" color={colors.onTertiaryContainer}>{formatMoney(data.oneTimeThisYear, data.homeCurrency)}</Text>
                </View>
              </Card>
            ) : null}

            {data.byCategory.length > 0 ? (
              <Card>
                <Text variant="titleLargeEmphasized" accessibilityRole="header">By category</Text>
                <Text variant="bodyMedium" color={colors.onSurfaceVariant}>Monthly, in {data.homeCurrency}</Text>
                {data.byCategory.map((item) => {
                  const id = category(item.categoryName);
                  return (
                    <View key={item.categoryId ?? item.categoryName} style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}
                      accessible accessibilityLabel={`${item.categoryName}, ${formatMoney(item.monthlyAmount, data.homeCurrency)} a month`}>
                      <Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />
                      <View style={{ flex: 1, gap: space.xs + space.xxs }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.sm }}>
                          <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{item.categoryName}</Text>
                          <Text variant="amountLabel">{formatMoney(item.monthlyAmount, data.homeCurrency)}</Text>
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
                <Text variant="titleLargeEmphasized" accessibilityRole="header">By payment source</Text>
                {topSource ? <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{topSource}</Text> : null}
                {data.byPaymentSource.map((item) => {
                  // A card when the type is missing: an older API or a cached response.
                  const id = payment(item.paymentSourceType ?? 'Card');
                  return (
                    <View key={item.paymentSourceId ?? item.paymentSourceLabel} style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}
                      accessible accessibilityLabel={`${item.paymentSourceLabel}, ${formatMoney(item.monthlyAmount, data.homeCurrency)} a month`}>
                      <Tile icon={item.paymentSourceId ? id.icon : 'help-circle-outline'} color={id.ink} background={id.soft} size={40} />
                      <View style={{ flex: 1, gap: space.xs + space.xxs }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.sm }}>
                          <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{item.paymentSourceLabel}</Text>
                          <Text variant="amountLabel">{formatMoney(item.monthlyAmount, data.homeCurrency)}</Text>
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
      </AsyncView>
    </Screen>
  );
}

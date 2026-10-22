import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { dashboardApi, subscriptionsApi, type BurnRate, type Subscription } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import {
  burnRateWarning, categoryShares, daysUntil, describeRelative, dueSoon, formatDayDate, formatMoney, formatShortDate,
  homeEquivalent, topSpendSummary,
} from '@/lib/logic';
import { syncReminders } from '@/lib/reminders';
import { useSubscriptionActions } from '@/lib/subscriptionActions';
import { shape, space, useTheme } from '@/theme';
import { Banner, Button, ButtonGroup, Card, EmptyState, FadeIn, ProgressBar, Tap, Text, Tile } from '@/ui';
import { LegendRow, SegmentRing } from '@/ui/charts';
import { Screen } from '@/ui/layout';
import { Logo } from '@/ui/logo';
import { AsyncView, CachedBanner, DashboardSkeleton } from '@/ui/states';

type Period = 'weekly' | 'monthly' | 'yearly';
const PERIOD_LABEL: Record<Period, string> = { weekly: 'per week', monthly: 'per month', yearly: 'per year' };

export default function Dashboard() {
  const { colors, category, payment } = useTheme();
  const offline = useOffline();
  const { data, loading, error, errorStatus, cached, cachedAt, reload } = useLoader<BurnRate>(dashboardApi.burnRate, CACHE_KEYS.burnRate);
  // The list feeds Due soon; it shares the list screen's mirror, so either screen warms the other.
  const subs = useLoader<Subscription[]>(subscriptionsApi.list, CACHE_KEYS.subscriptions);
  const actions = useSubscriptionActions();
  const [period, setPeriod] = useState<Period>('monthly');
  const refresh = () => { void reload(); void subs.reload(); };
  // Home is often the only screen opened: reschedule from its list as the Subscriptions tab does.
  useEffect(() => { if (subs.data) void syncReminders(subs.data); }, [subs.data]);

  const warning = data ? burnRateWarning(data.unresolvedSubscriptionIds.length, data.oldestRateFetchedAt) : null;
  const topSource = data ? topSpendSummary(data.byPaymentSource) : null;
  const shares = data ? categoryShares(data.byCategory) : [];
  // Bars are normalised against the leader, not the total: across eight categories a share-of-total
  // bar is a stub for everything below first place.
  const maxOf = (items: { monthlyAmount: number }[]) => Math.max(0, ...items.map((i) => i.monthlyAmount));
  const noSpend = !!data && data.yearly === 0 && data.oneTimeThisYear === 0 && data.byCategory.length === 0;
  // No spend is not the same as nothing tracked: trials and inactive subscriptions cost nothing.
  const trackedCount = subs.data ? subs.data.length : null;
  const due = subs.data ? dueSoon(subs.data) : null;
  const dueRows = due ? [...due.overdue, ...due.upcoming] : [];
  // A total only when the server converted every row; otherwise a count rather than a wrong sum.
  const dueTotal = dueRows.length > 0 && dueRows.every((s) => s.homeCurrencyAmount != null && s.homeCurrency)
    ? formatMoney(dueRows.reduce((sum, s) => sum + (s.homeCurrencyAmount ?? 0), 0), dueRows[0].homeCurrency)
    : null;
  const openList = (params?: Record<string, string>) => router.navigate({ pathname: '/subscriptions', params });

  // Due soon (#275): what is about to charge, with the overdue ones payable from here. Outside the
  // spend layout too: a free trial costs nothing, so an account with only a trial has no burn rate
  // but does have a deadline to show.
  const dueCard = due && dueRows.length > 0 ? (
    <Card>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'baseline', justifyContent: 'space-between', columnGap: space.sm }}>
        <Text variant="titleLargeEmphasized" accessibilityRole="header">Due soon</Text>
        <Text variant="labelLarge" color={colors.onSurfaceVariant}>
          {dueTotal ? `${dueTotal} to pay this week` : `${dueRows.length} due this week`}
        </Text>
      </View>
      {dueRows.map((sub) => {
        const overdue = due.overdue.includes(sub);
        const id = category(sub.categoryName);
        // Short: the row also holds Mark paid, and a full date with the year was cut off.
        const when = overdue ? `Overdue since ${formatShortDate(sub.nextBillingDate)}`
          : `${sub.isFreeTrial ? 'Trial ends ' : ''}${formatDayDate(sub.nextBillingDate)} · ${describeRelative(sub.nextBillingDate)}`;
        const equivalent = homeEquivalent(sub);
        return (
          <Tap key={sub.id} radius={shape.medium} scaleOnPress={false} onPress={() => router.push(`/subscription/${sub.id}`)}
            accessibilityRole="button" accessibilityLabel={`${sub.customName}, ${formatMoney(sub.costAmount, sub.currency)}, ${when}`}
            accessibilityActions={overdue && actions.canWrite ? [{ name: 'pay', label: 'Mark as paid' }] : undefined}
            onAccessibilityAction={() => void actions.markPaid(sub)}
            style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, minHeight: 56, paddingVertical: space.xs }}>
            <Logo url={sub.catalogLogoUrl} name={sub.customName} color={id.ink} background={id.soft} size={40} />
            <View style={{ flex: 1 }}>
              <Text variant="titleMedium" numberOfLines={1}>{sub.customName}</Text>
              <Text variant="bodyMedium" numberOfLines={1}
                color={overdue ? colors.renewalWarning : daysUntil(sub.nextBillingDate) === 0 ? colors.primary : colors.onSurfaceVariant}>{when}</Text>
            </View>
            {overdue ? (
              <Button label="Mark paid" variant="tonal" onPress={() => void actions.markPaid(sub)}
                loading={actions.busy === `pay:${sub.id}`} disabled={!actions.canWrite} />
            ) : (
              <View style={{ alignItems: 'flex-end' }}>
                <Text variant="amountLabel">{formatMoney(sub.costAmount, sub.currency)}</Text>
                {equivalent ? <Text variant="labelMedium" color={colors.onSurfaceVariant}>{equivalent}</Text> : null}
              </View>
            )}
          </Tap>
        );
      })}
      <Button label="See all subscriptions" variant="text" onPress={() => openList()} style={{ alignSelf: 'flex-start' }} />
    </Card>
  ) : null;

  return (
    <Screen title="Overview" subtitle="Burn rate" refreshing={(loading && !!data) || (subs.loading && !!subs.data)} onRefresh={refresh}>
      {/* Two requests feed this screen: either one showing its mirror makes the screen a mirror,
          and Retry reloads both. */}
      {cached || subs.cached ? (
        <CachedBanner savedAt={cached ? cachedAt : subs.cachedAt} status={errorStatus ?? subs.errorStatus} offline={offline} onRetry={refresh} />
      ) : null}
      {/* A failed reload over live figures: keep them, say so. The list failing alone is said here
          too, or Due soon would vanish without a word. */}
      {data && (error || subs.error) ? (
        <Banner tone="error" text={error ?? `${subs.data ? "Due soon isn't up to date." : "Due soon couldn't load."} ${subs.error}`} actionLabel="Retry" onAction={refresh} />
      ) : null}
      {warning ? <Banner tone="info" text={warning} /> : null}

      <AsyncView
        state={asyncState({ hasData: !!data, empty: noSpend, error, errorStatus })}
        skeleton={<DashboardSkeleton />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
        empty={trackedCount ? (
          <View style={{ gap: space.md }}>
            {dueCard}
            <EmptyState icon="pause-circle-outline" title="No active spend"
            body={`Your ${trackedCount === 1 ? 'subscription is a free trial or paused' : `${trackedCount} subscriptions are free trials or paused`}, so nothing counts towards your burn rate yet.`}
            action={<Button label="View subscriptions" icon="calendar-sync-outline" variant="tonal" onPress={() => router.navigate('/subscriptions')} />} />
          </View>
        ) : (
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
                {/* Re-keyed per period, so a new figure settles in instead of the digits snapping over. */}
                <FadeIn key={period} rise style={{ gap: space.xs }}>
                  <Text variant="amountDisplay" color={colors.onPrimaryContainer} adjustsFontSizeToFit numberOfLines={1}>
                    {formatMoney(data[period], data.homeCurrency)}
                  </Text>
                  <Text variant="titleMedium" color={colors.onPrimaryContainer}>{PERIOD_LABEL[period]} on subscriptions</Text>
                </FadeIn>
              </View>
              <ButtonGroup<Period>
                onPrimaryContainer
                value={period}
                onChange={setPeriod}
                options={[{ value: 'weekly', label: 'Week' }, { value: 'monthly', label: 'Month' }, { value: 'yearly', label: 'Year' }]}
              />
            </Card>

            {dueCard}

            {/* Spend by category as one ring (#284): every category, in the bars' colours. */}
            {shares.length > 0 ? (
              <Card style={{ flexDirection: 'row', alignItems: 'center', gap: space.xl }}>
                <View accessible accessibilityRole="image"
                  accessibilityLabel={`Monthly spend by category: ${shares.map((s) => `${s.categoryName} ${Math.round(s.share * 100)}%`).join(', ')}`}>
                  <SegmentRing
                    size={128}
                    segments={shares.map((s) => ({ key: s.categoryId ?? s.categoryName, share: s.share, color: category(s.categoryName).color }))}
                    center={(
                      <>
                        <Text variant="amountTitle" allowFontScaling={false} adjustsFontSizeToFit numberOfLines={1}>
                          {formatMoney(Math.round(data.monthly), data.homeCurrency).replace(/\.00$/, '')}
                        </Text>
                        <Text variant="labelMedium" color={colors.onSurfaceVariant} allowFontScaling={false}>a month</Text>
                      </>
                    )}
                  />
                </View>
                <View style={{ flex: 1, gap: space.sm }} importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
                  {shares.slice(0, 4).map((s) => (
                    <LegendRow key={s.categoryId ?? s.categoryName} color={category(s.categoryName).color} label={s.categoryName} value={`${Math.round(s.share * 100)}%`} />
                  ))}
                  {shares.length > 4 ? <Text variant="labelMedium" color={colors.onSurfaceVariant}>+{shares.length - 4} more in the list below</Text> : null}
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
                    // Opens the list on this category; the uncategorised bucket has no id to filter by.
                    <Tap key={item.categoryId ?? item.categoryName} radius={shape.small} scaleOnPress={false} disabled={!item.categoryId}
                      onPress={() => item.categoryId && openList({ categoryId: item.categoryId, filterLabel: item.categoryName })}
                      style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, paddingVertical: space.xs }}
                      accessibilityRole={item.categoryId ? 'button' : undefined}
                      accessibilityLabel={`${item.categoryName}, ${formatMoney(item.monthlyAmount, data.homeCurrency)} a month`}>
                      <Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />
                      <View style={{ flex: 1, gap: space.xs + space.xxs }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.sm }}>
                          <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{item.categoryName}</Text>
                          <Text variant="amountLabel">{formatMoney(item.monthlyAmount, data.homeCurrency)}</Text>
                        </View>
                        <ProgressBar value={item.monthlyAmount / (maxOf(data.byCategory) || 1)} color={id.ink} />
                      </View>
                    </Tap>
                  );
                })}
              </Card>
            ) : null}

            {data.byPaymentSource.length > 0 ? (
              <Card>
                <Text variant="titleLargeEmphasized" accessibilityRole="header">By payment source</Text>
                {topSource ? <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{topSource}</Text> : null}
                {data.byPaymentSource.map((item) => {
                  // Unassigned is nobody's card: neutral. A missing type on a real source (an older
                  // API or a cached response) reads as a card.
                  const id = item.paymentSourceId ? payment(item.paymentSourceType ?? 'Card') : category(null);
                  return (
                    <Tap key={item.paymentSourceId ?? item.paymentSourceLabel} radius={shape.small} scaleOnPress={false} disabled={!item.paymentSourceId}
                      onPress={() => item.paymentSourceId && openList({ paymentSourceId: item.paymentSourceId, filterLabel: item.paymentSourceLabel })}
                      style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, paddingVertical: space.xs }}
                      accessibilityRole={item.paymentSourceId ? 'button' : undefined}
                      accessibilityLabel={`${item.paymentSourceLabel}, ${formatMoney(item.monthlyAmount, data.homeCurrency)} a month`}>
                      <Tile icon={item.paymentSourceId ? id.icon : 'help-circle-outline'} color={id.ink} background={id.soft} size={40} />
                      <View style={{ flex: 1, gap: space.xs + space.xxs }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: space.sm }}>
                          <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{item.paymentSourceLabel}</Text>
                          <Text variant="amountLabel">{formatMoney(item.monthlyAmount, data.homeCurrency)}</Text>
                        </View>
                        <ProgressBar value={item.monthlyAmount / (maxOf(data.byPaymentSource) || 1)} color={id.ink} />
                      </View>
                    </Tap>
                  );
                })}
              </Card>
            ) : null}
          </View>
        ) : null}
      </AsyncView>
      {actions.confirmDialog}
    </Screen>
  );
}

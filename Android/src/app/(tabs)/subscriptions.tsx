import Storage from 'expo-sqlite/kv-store';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { subscriptionsApi, type Subscription } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import {
  CYCLE_LABEL, daysUntil, describeRelative, DUE_SOON_DAYS, filterSubscriptions, formatDayDate, formatLongDate, formatMoney, groupSubscriptions,
  homeEquivalent, isOverdue, sectionByDue, sortByCost,
} from '@/lib/logic';
import { syncReminders } from '@/lib/reminders';
import { useSubscriptionActions } from '@/lib/subscriptionActions';
import { space, useTheme } from '@/theme';
import { Banner, Button, ButtonGroup, EmptyState, Fab, IconButton, ListItem, OfflineBanner, Pill, positionOf, SectionHeader, Text, TextField } from '@/ui';
import { Screen } from '@/ui/layout';
import { Logo } from '@/ui/logo';
import { ActionSheet } from '@/ui/sheets';
import { AsyncView, CachedBanner, ListSkeleton } from '@/ui/states';

type Sort = 'due' | 'category' | 'cost';
const SORT_KEY = 'prefs:subscriptions-sort';
const readSort = (): Sort => {
  try {
    const stored = Storage.getItemSync(SORT_KEY);
    return stored === 'category' || stored === 'cost' ? stored : 'due';
  } catch {
    return 'due';
  }
};

export default function Subscriptions() {
  const { colors, category } = useTheme();
  const offline = useOffline();
  // Set by Home, Categories and Payment sources to open the list on one category or card.
  const params = useLocalSearchParams<{ categoryId?: string; paymentSourceId?: string; filterLabel?: string }>();
  const { data, loading, error, errorStatus, cached, cachedAt, reload } = useLoader(subscriptionsApi.list, CACHE_KEYS.subscriptions);
  const { markPaid, remove, busy, canWrite, confirmDialog } = useSubscriptionActions();
  const [menuFor, setMenuFor] = useState<Subscription | null>(null);
  const [query, setQuery] = useState('');
  const [sort, setSortState] = useState<Sort>(readSort);
  const setSort = (next: Sort) => {
    setSortState(next);
    Storage.setItemAsync(SORT_KEY, next).catch(() => undefined);
  };

  // Reminders are re-derived from whichever list won - live or the offline mirror. Writes reschedule
  // on their own (resyncReminders), so this covers changes made on another device.
  useEffect(() => { if (data) void syncReminders(data); }, [data]);

  const filter = { categoryId: params.categoryId ?? null, paymentSourceId: params.paymentSourceId ?? null };
  const filtered = data ? filterSubscriptions(data, { ...filter, query }) : [];
  const sections: { title: string; data: Subscription[]; summary?: string }[] = sort === 'due' ? sectionByDue(filtered)
    : sort === 'cost' ? (filtered.length ? [{ title: 'By monthly cost', data: sortByCost(filtered) }] : [])
    : groupSubscriptions(filtered).map((g) => ({ title: g.title, data: g.data, summary: g.summary }));
  const filterLabel = filter.categoryId || filter.paymentSourceId ? (params.filterLabel || 'Filtered') : null;
  const clearFilter = () => router.setParams({ categoryId: undefined, paymentSourceId: undefined, filterLabel: undefined });
  const add = () => router.push('/subscription/new');
  const hasAny = !!data && data.length > 0;

  return (
    <Screen
      title="Subscriptions"
      subtitle={data ? `${data.length} tracked` : undefined}
      refreshing={loading && !!data}
      onRefresh={reload}
      // One Add button at a time: the empty state already has one (#291).
      overlay={hasAny ? <Fab compact icon="plus" label="Add subscription" onPress={add} disabled={offline} /> : undefined}
    >
      {offline ? <OfflineBanner />
        : cached ? <CachedBanner savedAt={cachedAt} status={errorStatus} offline={offline} onRetry={reload} />
        : null}
      {error && data ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

      <AsyncView
        state={asyncState({ hasData: !!data, empty: data?.length === 0, error, errorStatus })}
        skeleton={<ListSkeleton rows={5} />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
        empty={(
          <EmptyState icon="calendar-plus" title="No subscriptions yet" body="Add one to see when it renews and what it costs you each month."
            action={<Button label="Add subscription" icon="plus" onPress={add} disabled={offline} />} />
        )}
      >
        <View style={{ gap: space.md }}>
          <TextField label="Search subscriptions" value={query} onChangeText={setQuery} autoCapitalize="none" autoCorrect={false} returnKeyType="search" />
          <ButtonGroup<Sort>
            value={sort}
            onChange={setSort}
            options={[{ value: 'due', label: 'Due date' }, { value: 'category', label: 'Category' }, { value: 'cost', label: 'Cost' }]}
          />
          {filterLabel ? (
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
              <Pill label={`Showing: ${filterLabel}`} tone="tertiary" icon="filter-variant" />
              <IconButton icon="close" label="Clear filter" onPress={clearFilter} />
            </View>
          ) : null}
        </View>

        {sections.length === 0 ? (
          <EmptyState icon="magnify" title="Nothing matches" body={query ? `No subscription is called "${query.trim()}".` : 'No subscriptions here yet.'}
            action={filterLabel ? <Button label="Show all" variant="tonal" onPress={clearFilter} /> : undefined} />
        ) : null}

        {sections.map((section) => (
          <View key={section.title}>
            <SectionHeader title={section.title} summary={section.summary ?? `${section.data.length} ${section.data.length === 1 ? 'subscription' : 'subscriptions'}`} />
            {section.data.map((sub, i) => {
              const overdue = isOverdue(sub);
              const id = category(sub.categoryName);
              // Sorted by monthly cost: say what that is where the price alone does not.
              const perMonth = sort === 'cost' && sub.cycleCadence !== 'Monthly' && sub.homeCurrencyMonthly != null && sub.homeCurrency
                ? `≈ ${formatMoney(sub.homeCurrencyMonthly, sub.homeCurrency)} a month` : null;
              const equivalent = homeEquivalent(sub);
              const price = `${formatMoney(sub.costAmount, sub.currency)} · ${CYCLE_LABEL[sub.cycleCadence]}`;
              const soon = !overdue && sub.isActive && daysUntil(sub.nextBillingDate) <= DUE_SOON_DAYS;
              const when = overdue ? `Overdue · was due ${formatLongDate(sub.nextBillingDate)}` : `${sub.isFreeTrial ? 'Trial ends' : 'Renews'} ${describeRelative(sub.nextBillingDate)}`;
              return (
                <ListItem
                  key={sub.id}
                  position={positionOf(i, section.data.length)}
                  accessibilityLabel={`${sub.customName}, ${price}${equivalent ? `, ${equivalent}` : ''}${perMonth ? `, ${perMonth}` : ''}, ${when}`}
                  onPress={() => router.push(`/subscription/${sub.id}`)}
                  onLongPress={() => setMenuFor(sub)}
                  // The row is one TalkBack stop; its buttons are offered as actions instead.
                  actions={[
                    ...(overdue && canWrite ? [{ label: 'Mark as paid', run: () => void markPaid(sub) }] : []),
                    { label: 'More options', run: () => setMenuFor(sub) },
                  ]}
                  leading={<Logo url={sub.catalogLogoUrl} name={sub.customName} color={id.ink} background={id.soft} size={40} />}
                  title={
                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
                      <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{sub.customName}</Text>
                      {sub.isFreeTrial ? <Pill label="Trial" tone="tertiary" /> : null}
                      {!sub.isActive ? <Pill label="Inactive" /> : null}
                    </View>
                  }
                  subtitle={
                    <View>
                      <Text variant="amountLabel" color={colors.onSurfaceVariant} numberOfLines={1}>
                        {equivalent ? `${price} · ${equivalent}` : price}
                      </Text>
                      {/* Its own line: beside a yearly price and the trailing date it was cut off. */}
                      {perMonth ? <Text variant="labelMedium" color={colors.onSurfaceVariant} numberOfLines={1}>{perMonth}</Text> : null}
                      {/* The trailing slot holds Mark paid, so the date that is owed goes here. Never colour alone: it says so. */}
                      {overdue ? <Text variant="labelMedium" color={colors.renewalWarning} numberOfLines={1}>{`Was due ${formatLongDate(sub.nextBillingDate)}`}</Text> : null}
                    </View>
                  }
                  trailing={overdue ? (
                    <Button label="Mark paid" variant="tonal" onPress={() => void markPaid(sub)}
                      loading={busy === `pay:${sub.id}`} disabled={!canWrite} />
                  ) : sub.isActive ? (
                    <View style={{ alignItems: 'flex-end', gap: space.xxs }}>
                      <Text variant="labelLarge" color={soon ? colors.primary : colors.onSurfaceVariant}>{describeRelative(sub.nextBillingDate)}</Text>
                      {soon ? <Text variant="labelMedium" color={colors.onSurfaceVariant}>{formatDayDate(sub.nextBillingDate)}</Text> : null}
                    </View>
                  ) : null}
                />
              );
            })}
          </View>
        ))}
      </AsyncView>

      {confirmDialog}
      <ActionSheet
        title={menuFor?.customName ?? null}
        onClose={() => setMenuFor(null)}
        actions={menuFor ? [
          { label: 'Edit', icon: 'pencil-outline', onPress: () => router.push(`/subscription/edit/${menuFor.id}`) },
          ...(isOverdue(menuFor) && canWrite ? [{ label: 'Mark as paid', icon: 'check-circle-outline' as const, onPress: () => void markPaid(menuFor) }] : []),
          ...(canWrite ? [{ label: 'Delete', icon: 'delete-outline' as const, destructive: true, onPress: () => remove(menuFor) }] : []),
        ] : []}
      />
    </Screen>
  );
}

import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Alert, View } from 'react-native';
import { isStatus, subscriptionsApi, writeErrorMessage, type Subscription } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { CYCLE_LABEL, describeRelative, formatLongDate, formatMoney, groupSubscriptions, initialOf, isOverdue } from '@/lib/logic';
import { syncReminders } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { useTheme } from '@/theme';
import {
  Banner, Button, EmptyState, Fab, IconButton, ListItem, OfflineBanner, Pill, positionOf, ScreenHeader, SectionHeader, Spinner, Text, Tile,
} from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet } from '@/ui/sheets';

export default function Subscriptions() {
  const { colors, category } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const { data, loading, error, cached, reload } = useLoader(subscriptionsApi.list, CACHE_KEYS.subscriptions);
  const [busy, setBusy] = useState(false);
  const [writeError, setWriteError] = useState<string | null>(null);
  const [menuFor, setMenuFor] = useState<Subscription | null>(null);

  // Reminders are re-derived from whichever list won - live or the offline mirror.
  // ponytail: only refreshed when this screen loads; move behind dataVersion if reminders must follow
  // edits made without visiting the list.
  useEffect(() => { if (data) void syncReminders(data); }, [data]);

  /** A second write landing on the first would, for mark-paid, advance the date two cycles. */
  const write = async (action: () => Promise<unknown>) => {
    if (busy) return;
    setWriteError(null);
    setBusy(true);
    try {
      await action();
      haptics.confirm();
      dataChanged();
    } catch (e) {
      haptics.reject();
      // Nothing is queued for later, so a failed settle must say so or the OVERDUE chip lies.
      setWriteError(writeErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const markPaid = (sub: Subscription) => write(async () => {
    try {
      await subscriptionsApi.markPaid(sub.id, sub.nextBillingDate);
    } catch (e) {
      // The charge on screen is no longer the one due - settled on another device, or edited. Show
      // the current state rather than an error about a list that was simply out of date.
      if (!isStatus(e, 409)) throw e;
    }
  });

  const remove = (sub: Subscription) => Alert.alert('Delete subscription', `Delete ${sub.customName}? This cannot be undone.`, [
    { text: 'Cancel', style: 'cancel' },
    { text: 'Delete', style: 'destructive', onPress: () => write(() => subscriptionsApi.remove(sub.id)) },
  ]);

  const canWrite = !offline && !busy;
  const groups = data ? groupSubscriptions(data) : [];

  return (
    <Screen
      refreshing={loading && !!data}
      onRefresh={reload}
      overlay={<Fab icon="plus" label="Add" onPress={() => router.push('/subscription/new')} disabled={offline} />}
    >
      <ScreenHeader subtitle={data ? `${data.length} tracked` : undefined} title="Subscriptions" />

      {offline ? <OfflineBanner /> : cached ? <Banner tone="warning" text="Offline — showing last synced data." /> : null}
      {error ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}
      {writeError ? <Banner tone="error" text={writeError} /> : null}
      {!data && loading ? <Spinner /> : null}

      {data && data.length === 0 ? (
        <EmptyState icon="calendar-plus" title="No subscriptions yet" body="Add one to see when it renews and what it costs you each month." />
      ) : null}

      {groups.map((group) => (
        <View key={group.title}>
          <SectionHeader title={group.title} summary={group.summary} />
          {group.data.map((sub, i) => {
            const overdue = isOverdue(sub);
            const id = category(sub.categoryName);
            return (
              <ListItem
                key={sub.id}
                position={positionOf(i, group.data.length)}
                accessibilityLabel={`${sub.customName}, ${formatMoney(sub.costAmount, sub.currency)} ${CYCLE_LABEL[sub.cycleCadence]}`}
                onPress={() => router.push(`/subscription/${sub.id}`)}
                onLongPress={() => setMenuFor(sub)}
                leading={<Tile letter={initialOf(sub.customName)} color={id.color} background={id.soft} />}
                title={
                  <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
                    <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{sub.customName}</Text>
                    {sub.isFreeTrial ? <Pill label="Trial" tone="tertiary" /> : null}
                    {!sub.isActive ? <Pill label="Inactive" /> : null}
                  </View>
                }
                subtitle={
                  <Text variant="bodyMedium" color={overdue ? colors.error : colors.onSurfaceVariant}>
                    {formatMoney(sub.costAmount, sub.currency)} · {CYCLE_LABEL[sub.cycleCadence]} · {overdue ? `was due ${formatLongDate(sub.nextBillingDate)}` : `renews ${describeRelative(sub.nextBillingDate)}`}
                  </Text>
                }
                trailing={<IconButton icon="dots-vertical" label={`More for ${sub.customName}`} onPress={() => setMenuFor(sub)} />}
              >
                {overdue ? (
                  <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, paddingHorizontal: 16, paddingBottom: 14 }}>
                    <Pill label="Overdue" tone="error" />
                    <View style={{ flex: 1 }} />
                    <Button label="Mark as paid" icon="check" variant="tonal" onPress={() => markPaid(sub)} disabled={!canWrite} />
                  </View>
                ) : null}
              </ListItem>
            );
          })}
        </View>
      ))}

      <ActionSheet
        title={menuFor?.customName ?? null}
        onClose={() => setMenuFor(null)}
        actions={menuFor ? [
          { label: 'Edit', icon: 'pencil-outline', onPress: () => router.push(`/subscription/${menuFor.id}`) },
          ...(isOverdue(menuFor) && canWrite ? [{ label: 'Mark as paid', icon: 'check-circle-outline' as const, onPress: () => markPaid(menuFor) }] : []),
          ...(canWrite ? [{ label: 'Delete', icon: 'delete-outline' as const, destructive: true, onPress: () => remove(menuFor) }] : []),
        ] : []}
      />
    </Screen>
  );
}

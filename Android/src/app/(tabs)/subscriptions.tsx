import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { isStatus, subscriptionsApi, writeErrorMessage, type Subscription } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { CYCLE_LABEL, describeRelative, formatLongDate, formatMoney, groupSubscriptions, initialOf, isOverdue } from '@/lib/logic';
import { syncReminders } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { space, useTheme } from '@/theme';
import { Banner, Button, EmptyState, Fab, IconButton, ListItem, OfflineBanner, Pill, positionOf, SectionHeader, Text, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, useConfirm } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';
import { AsyncView, CachedBanner, ListSkeleton } from '@/ui/states';

const STALE_CHARGE = 'Not marked paid: this charge changed on another device. The list now shows what is due.';

export default function Subscriptions() {
  const { colors, category } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data, loading, error, errorStatus, cached, cachedAt, reload } = useLoader(subscriptionsApi.list, CACHE_KEYS.subscriptions);
  /** Which write is in flight ("pay:<id>", "delete:<id>"), or null. Any value blocks a second write. */
  const [busy, setBusy] = useState<string | null>(null);
  /** The guard itself: state lags a render, and a fast double tap on Mark as paid lands in that gap. */
  const inFlight = useRef(false);
  const [menuFor, setMenuFor] = useState<Subscription | null>(null);
  const [confirmDialog, confirm] = useConfirm();

  // Reminders are re-derived from whichever list won - live or the offline mirror.
  // ponytail: only refreshed when this screen loads; move behind dataVersion if reminders must follow
  // edits made without visiting the list.
  useEffect(() => { if (data) void syncReminders(data); }, [data]);

  /** A second write landing on the first would, for mark-paid, advance the date two cycles. */
  const write = async (key: string, success: string, action: () => Promise<unknown>) => {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(key);
    try {
      await action();
      haptics.confirm();
      dataChanged();
      snackbar({ text: success });
    } catch (e) {
      haptics.reject();
      // Nothing is queued for later, so a failed settle must say so or the OVERDUE chip lies.
      snackbar({ tone: 'error', text: isStatus(e, 409) ? STALE_CHARGE : writeErrorMessage(e) });
    } finally {
      inFlight.current = false;
      setBusy(null);
    }
  };

  const markPaid = (sub: Subscription) => write(`pay:${sub.id}`, `${sub.customName} marked as paid.`, async () => {
    try {
      await subscriptionsApi.markPaid(sub.id, sub.nextBillingDate);
    } catch (e) {
      // The charge on screen is no longer the one due - settled on another device, or its date
      // edited. Nothing was recorded, so it is not a success: reload to show what is due now, and
      // say why rather than leave the user thinking this tap paid it.
      if (isStatus(e, 409)) dataChanged();
      throw e;
    }
  });

  // No undo: there is no endpoint that restores a deleted subscription as it was.
  const remove = (sub: Subscription) => confirm({
    title: 'Delete subscription', body: `Delete ${sub.customName}? This cannot be undone.`, confirmLabel: 'Delete', destructive: true,
    onConfirm: () => write(`delete:${sub.id}`, `${sub.customName} deleted.`, () => subscriptionsApi.remove(sub.id)),
  });

  const canWrite = !offline && !busy;
  const groups = data ? groupSubscriptions(data) : [];
  const add = () => router.push('/subscription/new');

  return (
    <Screen
      title="Subscriptions"
      subtitle={data ? `${data.length} tracked` : undefined}
      refreshing={loading && !!data}
      onRefresh={reload}
      overlay={<Fab icon="plus" label="Add subscription" onPress={add} disabled={offline} />}
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
        {groups.map((group) => (
          <View key={group.title}>
            <SectionHeader title={group.title} summary={group.summary} />
            {group.data.map((sub, i) => {
              const overdue = isOverdue(sub);
              const id = category(sub.categoryName);
              const price = `${formatMoney(sub.costAmount, sub.currency)} · ${CYCLE_LABEL[sub.cycleCadence]}`;
              const when = overdue ? `Overdue · was due ${formatLongDate(sub.nextBillingDate)}` : `Renews ${describeRelative(sub.nextBillingDate)}`;
              return (
                <ListItem
                  key={sub.id}
                  position={positionOf(i, group.data.length)}
                  accessibilityLabel={`${sub.customName}, ${price}, ${when}`}
                  onPress={() => router.push(`/subscription/${sub.id}`)}
                  onLongPress={() => setMenuFor(sub)}
                  // The row is one TalkBack stop; its buttons are offered as actions instead.
                  actions={[
                    ...(overdue && canWrite ? [{ label: 'Mark as paid', run: () => markPaid(sub) }] : []),
                    { label: 'More options', run: () => setMenuFor(sub) },
                  ]}
                  leading={<Tile letter={initialOf(sub.customName)} color={id.ink} background={id.soft} />}
                  title={
                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
                      <Text variant="titleMedium" numberOfLines={1} style={{ flexShrink: 1 }}>{sub.customName}</Text>
                      {sub.isFreeTrial ? <Pill label="Trial" tone="tertiary" /> : null}
                      {!sub.isActive ? <Pill label="Inactive" /> : null}
                    </View>
                  }
                  subtitle={
                    <View style={{ gap: space.xs + space.xxs, alignItems: 'flex-start' }}>
                      <Text variant="amountLabel" color={colors.onSurfaceVariant}>{price}</Text>
                      {/* Overdue is never colour alone: the chip carries an icon and says it. */}
                      <Pill label={when} tone={overdue ? 'warning' : 'neutral'} icon={overdue ? 'alert-outline' : 'calendar-refresh-outline'} />
                    </View>
                  }
                  trailing={<IconButton icon="dots-vertical" label={`More for ${sub.customName}`} onPress={() => setMenuFor(sub)} />}
                >
                  {overdue ? (
                    <View style={{ flexDirection: 'row', justifyContent: 'flex-end', paddingHorizontal: space.lg, paddingBottom: space.md + space.xxs }}>
                      <Button label="Mark as paid" icon="check" variant="tonal" onPress={() => markPaid(sub)}
                        loading={busy === `pay:${sub.id}`} disabled={!canWrite} />
                    </View>
                  ) : null}
                </ListItem>
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
          { label: 'Edit', icon: 'pencil-outline', onPress: () => router.push(`/subscription/${menuFor.id}`) },
          ...(isOverdue(menuFor) && canWrite ? [{ label: 'Mark as paid', icon: 'check-circle-outline' as const, onPress: () => markPaid(menuFor) }] : []),
          ...(canWrite ? [{ label: 'Delete', icon: 'delete-outline' as const, destructive: true, onPress: () => remove(menuFor) }] : []),
        ] : []}
      />
    </Screen>
  );
}

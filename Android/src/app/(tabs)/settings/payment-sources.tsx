import { randomUUID } from 'expo-crypto';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { View } from 'react-native';
import { dashboardApi, isRetryable, isStatus, paymentSourcesApi, subscriptionsApi, writeErrorMessage, type PaymentSource, type PaymentSourceType } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { formatMoney } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { space, useTheme } from '@/theme';
import { Banner, Button, ButtonGroup, EmptyState, IconButton, ListItem, OfflineBanner, positionOf, SectionHeader, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, PromptDialog, Sheet, useConfirm } from '@/ui/sheets';
import { useSnackbar, type SnackbarMessage } from '@/ui/snackbar';
import { AsyncView, ListSkeleton } from '@/ui/states';

const EARLIER_ADD = 'Your earlier add went through before this change - it is in the list. Tap Add again to add this one as well.';

const TYPE_LABEL: Record<PaymentSourceType, string> = { Card: 'Card', BankAccount: 'Bank', Wallet: 'Wallet', Other: 'Other' };

/**
 * Payment sources (#281): the list first, each with what it carries a month and how many
 * subscriptions bill to it - "what lands on this card if I cancel it?". Adding is a sheet behind Add.
 */
export default function PaymentSources() {
  const { colors, payment } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data, loading, error, errorStatus, reload } = useLoader(paymentSourcesApi.list);
  const burn = useLoader(dashboardApi.burnRate, CACHE_KEYS.burnRate);
  const subs = useLoader(subscriptionsApi.list, CACHE_KEYS.subscriptions);
  const [adding, setAdding] = useState(false);
  /** Said inside the sheet: a snackbar would sit behind it. */
  const [addError, setAddError] = useState<string | null>(null);
  const [label, setLabel] = useState('');
  const [sourceType, setSourceType] = useState<PaymentSourceType>('Card');
  /** Which write is in flight ('add', 'rename', 'delete', 'undo') for the spinners; the ref is the guard. */
  const [busy, setBusy] = useState<string | null>(null);
  const inFlight = useRef<string | null>(null);
  const [confirmDialog, confirm] = useConfirm();
  const [menuFor, setMenuFor] = useState<PaymentSource | null>(null);
  const [renaming, setRenaming] = useState<PaymentSource | null>(null);

  /** Deletes and renames both move the dashboard's spend-by-account breakdown. */
  const write = async (key: string, action: () => Promise<SnackbarMessage>) => {
    if (inFlight.current) return false;
    inFlight.current = key;
    setBusy(key);
    try {
      const result = await action();
      haptics.confirm();
      dataChanged();
      snackbar(result);
    } catch (e) {
      haptics.reject();
      snackbar({
        tone: 'error', text: isStatus(e, 409) ? EARLIER_ADD : writeErrorMessage(e),
        ...(isRetryable(e) ? { actionLabel: 'Retry', onAction: () => void write(key, action) } : {}),
      });
    } finally {
      inFlight.current = null;
      setBusy(null);
    }
    return true;
  };

  /** An undo is never dropped silently: if another write is still saving, say so. */
  const undo = (action: () => Promise<SnackbarMessage>) => {
    void write('undo', action).then((started) => {
      if (!started) snackbar({ tone: 'error', text: "Couldn't undo — another change is still saving. Try again." });
    });
  };

  // One id per payment source being added: a retry after a lost response resends it and gets the
  // original back instead of a duplicate. Replaced only once the add has gone through.
  const [createId, setCreateId] = useState(randomUUID);

  const add = async () => {
    if (inFlight.current) return;
    inFlight.current = 'add';
    setBusy('add');
    setAddError(null);
    try {
      await paymentSourcesApi.create(createId, label.trim(), sourceType);
      haptics.confirm();
      dataChanged();
      snackbar({ text: `${label.trim()} added.` });
      setCreateId(randomUUID());
      setLabel('');
      setSourceType('Card');
      setAdding(false);
    } catch (e) {
      haptics.reject();
      // The earlier add went through, but the form has changed since, so the server refused to
      // pass that record off as this one. Show it, and give the next tap a new id so it adds this.
      if (isStatus(e, 409)) {
        setCreateId(randomUUID());
        dataChanged();
      }
      setAddError(isStatus(e, 409) ? EARLIER_ADD : writeErrorMessage(e));
    } finally {
      inFlight.current = null;
      setBusy(null);
    }
  };

  // In place, not delete-and-recreate: that would detach every subscription that billed to it.
  const rename = (target: PaymentSource, value: string) => {
    setRenaming(null);
    if (!value.trim() || value.trim() === target.label) return;
    void write('rename', async () => {
      await paymentSourcesApi.update(target.id, value.trim(), target.sourceType);
      // The same in-place update reverses it cleanly, so a rename gets an undo.
      return {
        text: `Renamed to ${value.trim()}.`,
        actionLabel: 'Undo',
        onAction: () => undo(async () => {
          await paymentSourcesApi.update(target.id, target.label, target.sourceType);
          return { text: `Name changed back to ${target.label}.` };
        }),
      };
    });
  };

  const remove = (target: PaymentSource) => confirm({
    title: 'Delete payment source',
    body: `Delete "${target.label}"? Subscriptions billed to it will stay, without a payment source.`,
    confirmLabel: 'Delete',
    destructive: true,
    onConfirm: () => void write('delete', async () => {
      await paymentSourcesApi.remove(target.id);
      return { text: `${target.label} deleted.` };
    }),
  });

  const canAdd = !offline && !busy && !!label.trim();
  const spend = new Map((burn.data?.byPaymentSource ?? []).map((b) => [b.paymentSourceId, b.monthlyAmount]));
  const counts = new Map<string, number>();
  for (const s of subs.data ?? []) if (s.paymentSourceId) counts.set(s.paymentSourceId, (counts.get(s.paymentSourceId) ?? 0) + 1);
  // Biggest monthly amount first: the cards that matter most when one is cancelled or replaced.
  const rows = [...(data ?? [])].sort((a, b) => (spend.get(b.id) ?? 0) - (spend.get(a.id) ?? 0) || a.label.localeCompare(b.label));
  const total = rows.reduce((sum, r) => sum + (spend.get(r.id) ?? 0), 0);
  const open = (source: PaymentSource) => router.navigate({ pathname: '/subscriptions', params: { paymentSourceId: source.id, filterLabel: source.label } });

  return (
    <Screen
      refreshing={loading && !!data}
      onRefresh={() => { void reload(); void subs.reload(); void burn.reload(); }}
      header={(
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingVertical: space.sm, paddingHorizontal: space.sm }}>
          <IconButton icon="arrow-left" label="Back" onPress={() => router.back()} />
          <Text variant="titleLargeEmphasized" accessibilityRole="header" style={{ flex: 1 }} numberOfLines={1}>Payment sources</Text>
          <Button label="Add" icon="plus" variant="text" onPress={() => setAdding(true)} disabled={offline} />
        </View>
      )}
    >
      {offline ? <OfflineBanner /> : null}
      {error && data ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

      <AsyncView
        state={asyncState({ hasData: !!data, empty: rows.length === 0, error, errorStatus })}
        skeleton={<ListSkeleton rows={3} />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
        empty={<EmptyState icon="credit-card-plus-outline" title="No payment sources" body="Add the cards and accounts you pay from to see which one carries the most spend."
          action={<Button label="Add payment source" icon="plus" onPress={() => setAdding(true)} disabled={offline} />} />}
      >
        <SectionHeader title="Your sources" summary={burn.data && total > 0
          ? `${formatMoney(total, burn.data.homeCurrency)} a month across ${rows.length} ${rows.length === 1 ? 'source' : 'sources'}`
          : `${rows.length} ${rows.length === 1 ? 'source' : 'sources'}`} />
        {rows.map((source, i) => {
          const id = payment(source.sourceType);
          const n = counts.get(source.id) ?? 0;
          const amount = spend.get(source.id);
          const detail = subs.data ? `${TYPE_LABEL[source.sourceType]} · ${n} ${n === 1 ? 'subscription' : 'subscriptions'}` : TYPE_LABEL[source.sourceType];
          return (
            <ListItem
              key={source.id}
              position={positionOf(i, rows.length)}
              title={source.label}
              subtitle={detail}
              accessibilityLabel={[source.label, detail, amount ? `${formatMoney(amount, burn.data?.homeCurrency)} a month` : null].filter(Boolean).join(', ')}
              leading={<Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />}
              onPress={n > 0 ? () => open(source) : undefined}
              onLongPress={offline ? undefined : () => setMenuFor(source)}
              actions={offline ? undefined : [{ label: 'Rename or delete', run: () => setMenuFor(source) }]}
              trailing={(
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs }}>
                  {amount ? (
                    <View style={{ alignItems: 'flex-end' }}>
                      <Text variant="amountLabel">{formatMoney(amount, burn.data?.homeCurrency)}</Text>
                      <Text variant="labelMedium" color={colors.onSurfaceVariant}>a month</Text>
                    </View>
                  ) : null}
                  <IconButton icon="dots-vertical" label={`Manage ${source.label}`} onPress={() => setMenuFor(source)} disabled={offline} />
                </View>
              )}
            />
          );
        })}
        {rows.length > 0 ? <Text variant="bodySmall" color={colors.onSurfaceVariant} style={{ paddingHorizontal: space.xs, paddingTop: space.sm }}>Tap a source to see the subscriptions charged to it.</Text> : null}
      </AsyncView>

      {confirmDialog}
      <Sheet visible={adding} title="New payment source" onClose={() => { setAdding(false); setAddError(null); }}>
        <View style={{ gap: space.lg, paddingHorizontal: space.xxl }}>
          {addError ? <Banner tone="error" text={addError} actionLabel={canAdd ? 'Retry' : undefined} onAction={() => void add()} /> : null}
          {/* The keyboard's Done obeys the button: no blank label, no write while offline. */}
          <TextField label="Label" value={label} onChangeText={setLabel} placeholder="e.g. HDFC Credit Card" autoFocus returnKeyType="done" onSubmitEditing={() => { if (canAdd) void add(); }} />
          <ButtonGroup<PaymentSourceType>
            value={sourceType}
            onChange={setSourceType}
            options={(Object.keys(TYPE_LABEL) as PaymentSourceType[]).map((t) => ({ value: t, label: TYPE_LABEL[t] }))}
          />
          <Button label="Add payment source" icon="plus" onPress={add} loading={busy === 'add'} disabled={!canAdd} style={{ alignSelf: 'flex-end' }} />
        </View>
      </Sheet>
      <ActionSheet
        title={menuFor?.label ?? null}
        onClose={() => setMenuFor(null)}
        actions={menuFor ? [
          { label: 'Rename', icon: 'pencil-outline', onPress: () => setRenaming(menuFor) },
          { label: 'Delete', icon: 'delete-outline', destructive: true, onPress: () => remove(menuFor) },
        ] : []}
      />
      <PromptDialog
        visible={renaming !== null}
        title="Rename payment source"
        label="New label"
        initial={renaming?.label ?? ''}
        confirmLabel="Rename"
        onClose={() => setRenaming(null)}
        onSubmit={(value) => renaming && rename(renaming, value)}
      />
    </Screen>
  );
}

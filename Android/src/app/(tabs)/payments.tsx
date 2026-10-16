import { randomUUID } from 'expo-crypto';
import { useState } from 'react';
import { Alert } from 'react-native';
import { isStatus, paymentSourcesApi, writeErrorMessage, type PaymentSource, type PaymentSourceType } from '@/api';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { useSession } from '@/lib/session';
import { useTheme } from '@/theme';
import { Banner, Button, ButtonGroup, Card, EmptyState, IconButton, ListItem, OfflineBanner, positionOf, SectionHeader, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, PromptDialog } from '@/ui/sheets';
import { useSnackbar, type SnackbarMessage } from '@/ui/snackbar';
import { AsyncView, ListSkeleton } from '@/ui/states';

const EARLIER_ADD = 'Your earlier add went through before this change - it is in the list. Tap Add again to add this one as well.';

const TYPE_LABEL: Record<PaymentSourceType, string> = { Card: 'Card', BankAccount: 'Bank', Wallet: 'Wallet', Other: 'Other' };

export default function PaymentSources() {
  const { payment } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data, loading, error, errorStatus, reload } = useLoader(paymentSourcesApi.list);
  const [label, setLabel] = useState('');
  const [sourceType, setSourceType] = useState<PaymentSourceType>('Card');
  const [busy, setBusy] = useState(false);
  const [menuFor, setMenuFor] = useState<PaymentSource | null>(null);
  const [renaming, setRenaming] = useState<PaymentSource | null>(null);

  /** Deletes and renames both move the dashboard's spend-by-account breakdown. */
  const write = async (action: () => Promise<SnackbarMessage>) => {
    if (busy) return;
    setBusy(true);
    try {
      const result = await action();
      haptics.confirm();
      dataChanged();
      snackbar(result);
    } catch (e) {
      haptics.reject();
      snackbar({ tone: 'error', text: isStatus(e, 409) ? EARLIER_ADD : writeErrorMessage(e) });
    } finally {
      setBusy(false);
    }
  };

  // One id per payment source being added: a retry after a lost response resends it and gets the
  // original back instead of a duplicate. Replaced only once the add has gone through.
  const [createId, setCreateId] = useState(randomUUID);

  const add = () => write(async () => {
    try {
      await paymentSourcesApi.create(createId, label.trim(), sourceType);
    } catch (e) {
      // The earlier add went through, but the form has changed since, so the server refused to
      // pass that record off as this one. Show it, and give the next tap a new id so it adds this.
      if (isStatus(e, 409)) {
        setCreateId(randomUUID());
        dataChanged();
      }
      throw e;
    }
    const added = label.trim();
    setCreateId(randomUUID());
    setLabel('');
    setSourceType('Card');
    return { text: `${added} added.` };
  });

  // In place, not delete-and-recreate: that would detach every subscription that billed to it.
  const rename = (target: PaymentSource, value: string) => {
    setRenaming(null);
    if (!value.trim() || value.trim() === target.label) return;
    void write(async () => {
      await paymentSourcesApi.update(target.id, value.trim(), target.sourceType);
      // The same in-place update reverses it cleanly, so a rename gets an undo.
      return {
        text: `Renamed to ${value.trim()}.`,
        actionLabel: 'Undo',
        onAction: () => void write(async () => {
          await paymentSourcesApi.update(target.id, target.label, target.sourceType);
          return { text: `Name changed back to ${target.label}.` };
        }),
      };
    });
  };

  const remove = (target: PaymentSource) => Alert.alert(
    'Delete payment source',
    `Delete "${target.label}"? Subscriptions billed to it will stay, without a payment source.`,
    [{ text: 'Cancel', style: 'cancel' }, {
      text: 'Delete', style: 'destructive', onPress: () => write(async () => {
        await paymentSourcesApi.remove(target.id);
        return { text: `${target.label} deleted.` };
      }),
    }],
  );

  const rows = data ?? [];

  return (
    <Screen title="Payment" subtitle="Where it's charged" refreshing={loading && !!data} onRefresh={reload}>
      {offline ? <OfflineBanner /> : null}
      {error && data ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

      <Card tone="default">
        <TextField label="Label" value={label} onChangeText={setLabel} placeholder="e.g. HDFC Credit Card" onSubmitEditing={add} />
        <ButtonGroup<PaymentSourceType>
          value={sourceType}
          onChange={setSourceType}
          options={(Object.keys(TYPE_LABEL) as PaymentSourceType[]).map((t) => ({ value: t, label: TYPE_LABEL[t] }))}
        />
        <Button label="Add payment source" icon="plus" variant="tonal" onPress={add} loading={busy && !!label.trim()} disabled={offline || busy || !label.trim()} style={{ alignSelf: 'flex-end' }} />
      </Card>

      <AsyncView
        state={asyncState({ hasData: !!data, empty: rows.length === 0, error, errorStatus })}
        skeleton={<ListSkeleton rows={3} />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
        empty={<EmptyState icon="credit-card-plus-outline" title="No payment sources" body="Add the cards and accounts you pay from to see which one carries the most spend." />}
      >
        <SectionHeader title="Your sources" summary={`${rows.length} ${rows.length === 1 ? 'source' : 'sources'}`} />
        {rows.map((source, i) => {
          const id = payment(source.sourceType);
          return (
            <ListItem
              key={source.id}
              position={positionOf(i, rows.length)}
              title={source.label}
              subtitle={TYPE_LABEL[source.sourceType]}
              accessibilityLabel={`${source.label}, ${TYPE_LABEL[source.sourceType]}`}
              leading={<Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />}
              onLongPress={offline ? undefined : () => setMenuFor(source)}
              trailing={<IconButton icon="dots-vertical" label={`Manage ${source.label}`} onPress={() => setMenuFor(source)} disabled={offline} />}
            />
          );
        })}
      </AsyncView>

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

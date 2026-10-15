import { useState } from 'react';
import { Alert } from 'react-native';
import { paymentSourcesApi, writeErrorMessage, type PaymentSource, type PaymentSourceType } from '@/api';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { useSession } from '@/lib/session';
import { useTheme } from '@/theme';
import {
  Banner, Button, ButtonGroup, Card, EmptyState, IconButton, ListItem, OfflineBanner, positionOf, ScreenHeader, SectionHeader, Spinner, TextField, Tile,
} from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, PromptDialog } from '@/ui/sheets';

const TYPE_LABEL: Record<PaymentSourceType, string> = { Card: 'Card', BankAccount: 'Bank', Wallet: 'Wallet', Other: 'Other' };

export default function PaymentSources() {
  const { payment } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const { data, loading, error, reload } = useLoader(paymentSourcesApi.list);
  const [label, setLabel] = useState('');
  const [sourceType, setSourceType] = useState<PaymentSourceType>('Card');
  const [writeError, setWriteError] = useState<string | null>(null);
  const [menuFor, setMenuFor] = useState<PaymentSource | null>(null);
  const [renaming, setRenaming] = useState<PaymentSource | null>(null);

  /** Deletes and renames both move the dashboard's spend-by-account breakdown. */
  const write = async (action: () => Promise<unknown>) => {
    setWriteError(null);
    try {
      await action();
      haptics.confirm();
      dataChanged();
    } catch (e) {
      haptics.reject();
      setWriteError(writeErrorMessage(e));
    }
  };

  const add = () => write(async () => {
    await paymentSourcesApi.create(label.trim(), sourceType);
    setLabel('');
    setSourceType('Card');
  });

  // In place, not delete-and-recreate: that would detach every subscription that billed to it.
  const rename = (target: PaymentSource, value: string) => {
    setRenaming(null);
    if (!value.trim() || value.trim() === target.label) return;
    void write(() => paymentSourcesApi.update(target.id, value.trim(), target.sourceType));
  };

  const remove = (target: PaymentSource) => Alert.alert(
    'Delete payment source',
    `Delete "${target.label}"? Subscriptions billed to it will stay, without a payment source.`,
    [{ text: 'Cancel', style: 'cancel' }, { text: 'Delete', style: 'destructive', onPress: () => write(() => paymentSourcesApi.remove(target.id)) }],
  );

  const rows = data ?? [];

  return (
    <Screen refreshing={loading && !!data} onRefresh={reload}>
      <ScreenHeader subtitle="Where it's charged" title="Payment" />
      {offline ? <OfflineBanner /> : null}
      {error ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}
      {writeError ? <Banner tone="error" text={writeError} /> : null}

      <Card tone="default">
        <TextField label="Label" value={label} onChangeText={setLabel} placeholder="e.g. HDFC Credit Card" onSubmitEditing={add} />
        <ButtonGroup<PaymentSourceType>
          value={sourceType}
          onChange={setSourceType}
          options={(Object.keys(TYPE_LABEL) as PaymentSourceType[]).map((t) => ({ value: t, label: TYPE_LABEL[t] }))}
        />
        <Button label="Add payment source" icon="plus" variant="tonal" onPress={add} disabled={offline || !label.trim()} style={{ alignSelf: 'flex-end' }} />
      </Card>

      {!data && loading ? <Spinner /> : null}
      {data && rows.length === 0 ? (
        <EmptyState icon="credit-card-plus-outline" title="No payment sources" body="Add the cards and accounts you pay from to see which one carries the most spend." />
      ) : null}

      {rows.length > 0 ? <SectionHeader title="Your sources" summary={`${rows.length} ${rows.length === 1 ? 'source' : 'sources'}`} /> : null}
      {rows.map((source, i) => {
        const id = payment(source.sourceType);
        return (
          <ListItem
            key={source.id}
            position={positionOf(i, rows.length)}
            title={source.label}
            subtitle={TYPE_LABEL[source.sourceType]}
            leading={<Tile icon={id.icon} color={id.color} background={id.soft} size={40} />}
            onLongPress={offline ? undefined : () => setMenuFor(source)}
            trailing={<IconButton icon="dots-vertical" label={`Manage ${source.label}`} onPress={() => setMenuFor(source)} disabled={offline} />}
          />
        );
      })}

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

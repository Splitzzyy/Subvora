import { useRef, useState } from 'react';
import { View } from 'react-native';
import { categoriesApi, isStatus, writeErrorMessage, type Category } from '@/api';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { useSession } from '@/lib/session';
import { useTheme } from '@/theme';
import { Banner, Button, Card, IconButton, ListItem, OfflineBanner, positionOf, SectionHeader, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, PromptDialog, useConfirm } from '@/ui/sheets';
import { useSnackbar, type SnackbarMessage } from '@/ui/snackbar';
import { AsyncView, ListSkeleton } from '@/ui/states';

const DUPLICATE = 'A category with this name already exists.';
const byName = (a: Category, b: Category) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

export default function Categories() {
  const { category } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data, loading, error, errorStatus, reload } = useLoader(categoriesApi.list);
  const [name, setName] = useState('');
  /** Which write is in flight ('add', 'rename', 'delete', 'undo') for the spinners; the ref is the guard. */
  const [busy, setBusy] = useState<string | null>(null);
  const inFlight = useRef<string | null>(null);
  const [confirmDialog, confirm] = useConfirm();
  const [menuFor, setMenuFor] = useState<Category | null>(null);
  const [renaming, setRenaming] = useState<Category | null>(null);

  /** Every write moves the dashboard breakdown or the list grouping, so all of them announce it. */
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
      snackbar({ tone: 'error', text: isStatus(e, 409) ? DUPLICATE : writeErrorMessage(e) });
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

  const add = () => write('add', async () => {
    const created = await categoriesApi.create(name.trim());
    setName('');
    return { text: `${created.name} added.` };
  });

  const rename = (target: Category, newName: string) => {
    setRenaming(null);
    if (!newName.trim() || newName.trim() === target.name) return;
    void write('rename', async () => {
      await categoriesApi.rename(target.id, newName.trim());
      // A rename is cleanly reversible with the same call, so it gets an undo.
      return {
        text: `Renamed to ${newName.trim()}.`,
        actionLabel: 'Undo',
        onAction: () => undo(async () => {
          await categoriesApi.rename(target.id, target.name);
          return { text: `Name changed back to ${target.name}.` };
        }),
      };
    });
  };

  // Subscriptions survive a delete (FK is SET NULL) but lose their grouping - say so first.
  const remove = (target: Category) => confirm({
    title: 'Delete category',
    body: `Delete "${target.name}"? Subscriptions using it will stay, but become uncategorised.`,
    confirmLabel: 'Delete',
    destructive: true,
    onConfirm: () => void write('delete', async () => {
      const { subscriptionsUncategorized: n } = await categoriesApi.remove(target.id);
      return { text: n > 0 ? `Category deleted. ${n} ${n === 1 ? 'subscription is' : 'subscriptions are'} now uncategorised.` : 'Category deleted.' };
    }),
  });

  const canAdd = !offline && !busy && !!name.trim();

  // Yours first - the half you can act on. System defaults are shared and read-only.
  const sections = [
    { title: 'Your categories', rows: (data ?? []).filter((c) => !c.isSystemDefault).sort(byName), editable: true },
    { title: 'System', rows: (data ?? []).filter((c) => c.isSystemDefault).sort(byName), editable: false },
  ].filter((s) => s.rows.length > 0);

  return (
    <Screen title="Categories" subtitle="Organise" refreshing={loading && !!data} onRefresh={reload}>
      {offline ? <OfflineBanner /> : null}
      {error && data ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

      <Card tone="default">
        {/* The keyboard's Done obeys the button: it must not send a blank name, or write while offline. */}
        <TextField label="New category" value={name} onChangeText={setName} placeholder="e.g. Music" onSubmitEditing={() => { if (canAdd) void add(); }} returnKeyType="done" />
        <Button label="Add category" icon="plus" variant="tonal" onPress={add} loading={busy === 'add'} disabled={!canAdd} style={{ alignSelf: 'flex-end' }} />
      </Card>

      <AsyncView
        state={asyncState({ hasData: !!data, error, errorStatus })}
        skeleton={<ListSkeleton rows={6} />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
      >
        {sections.map((section) => (
          <View key={section.title}>
            <SectionHeader title={section.title} summary={`${section.rows.length} ${section.rows.length === 1 ? 'category' : 'categories'}`} />
            {section.rows.map((c, i) => {
              const id = category(c.name);
              return (
                <ListItem
                  key={c.id}
                  position={positionOf(i, section.rows.length)}
                  title={c.name}
                  accessibilityLabel={c.name}
                  leading={<Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />}
                  onLongPress={section.editable && !offline ? () => setMenuFor(c) : undefined}
                  actions={section.editable && !offline ? [{ label: 'Rename or delete', run: () => setMenuFor(c) }] : undefined}
                  trailing={section.editable
                    ? <IconButton icon="dots-vertical" label={`Manage ${c.name}`} onPress={() => setMenuFor(c)} disabled={offline} />
                    : null}
                />
              );
            })}
          </View>
        ))}
      </AsyncView>

      {confirmDialog}
      <ActionSheet
        title={menuFor?.name ?? null}
        onClose={() => setMenuFor(null)}
        actions={menuFor ? [
          { label: 'Rename', icon: 'pencil-outline', onPress: () => setRenaming(menuFor) },
          { label: 'Delete', icon: 'delete-outline', destructive: true, onPress: () => remove(menuFor) },
        ] : []}
      />
      <PromptDialog
        visible={renaming !== null}
        title="Rename category"
        label="New name"
        initial={renaming?.name ?? ''}
        confirmLabel="Rename"
        onClose={() => setRenaming(null)}
        onSubmit={(value) => renaming && rename(renaming, value)}
      />
    </Screen>
  );
}

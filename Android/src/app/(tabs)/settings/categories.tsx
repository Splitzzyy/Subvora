import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { View } from 'react-native';
import { categoriesApi, dashboardApi, isRetryable, isStatus, subscriptionsApi, writeErrorMessage, type Category } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { formatMoney } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { space, useTheme } from '@/theme';
import { Banner, Button, Icon, IconButton, ListItem, OfflineBanner, Pill, positionOf, SectionHeader, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, PromptDialog, Sheet, useConfirm } from '@/ui/sheets';
import { useSnackbar, type SnackbarMessage } from '@/ui/snackbar';
import { AsyncView, ListSkeleton } from '@/ui/states';

const DUPLICATE = 'A category with this name already exists.';
const byName = (a: Category, b: Category) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

/**
 * Categories (#280): which ones carry subscriptions and what they cost, each opening the list on
 * itself. Adding is a sheet behind New rather than a form that always filled the top of the screen.
 */
export default function Categories() {
  const { colors, category } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data, loading, error, errorStatus, reload } = useLoader(categoriesApi.list);
  // Spend comes from the burn rate, counts from the list; both mirrors the other screens already keep.
  const burn = useLoader(dashboardApi.burnRate, CACHE_KEYS.burnRate);
  const subs = useLoader(subscriptionsApi.list, CACHE_KEYS.subscriptions);
  const [name, setName] = useState('');
  const [adding, setAdding] = useState(false);
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
      snackbar({
        tone: 'error', text: isStatus(e, 409) ? DUPLICATE : writeErrorMessage(e),
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

  /** Inside the sheet, so its failure is said there too: a snackbar would sit behind the sheet. */
  const [addError, setAddError] = useState<string | null>(null);
  const add = async () => {
    if (inFlight.current) return;
    inFlight.current = 'add';
    setBusy('add');
    setAddError(null);
    try {
      const created = await categoriesApi.create(name.trim());
      haptics.confirm();
      dataChanged();
      setName('');
      setAdding(false);
      snackbar({ text: `${created.name} added.` });
    } catch (e) {
      haptics.reject();
      setAddError(isStatus(e, 409) ? DUPLICATE : writeErrorMessage(e));
    } finally {
      inFlight.current = null;
      setBusy(null);
    }
  };

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

  const counts = new Map<string, number>();
  for (const s of subs.data ?? []) if (s.categoryId) counts.set(s.categoryId, (counts.get(s.categoryId) ?? 0) + 1);
  const spend = new Map((burn.data?.byCategory ?? []).map((b) => [b.categoryId, b.monthlyAmount]));
  const known = !!subs.data;
  // In use: by spend, then name. Not used yet: quieter, by name. Built-in and yours mix; yours carry
  // a tag and the menu (#177's split, kept as a mark rather than a "System" heading).
  const inUse = (data ?? []).filter((c) => (counts.get(c.id) ?? 0) > 0)
    .sort((a, b) => (spend.get(b.id) ?? 0) - (spend.get(a.id) ?? 0) || byName(a, b));
  const unused = (data ?? []).filter((c) => !inUse.includes(c)).sort(byName);
  const sections = known
    ? [{ title: 'In use', rows: inUse, used: true }, { title: 'Not used yet', rows: unused, used: false }].filter((s) => s.rows.length > 0)
    : [{ title: 'All categories', rows: [...(data ?? [])].sort(byName), used: true }];
  const describe = (c: Category) => {
    const n = counts.get(c.id) ?? 0;
    if (!known || n === 0) return undefined;
    const amount = spend.get(c.id);
    return `${n} ${n === 1 ? 'subscription' : 'subscriptions'}${amount ? ` · ${formatMoney(amount, burn.data?.homeCurrency)} a month` : ''}`;
  };
  const open = (c: Category) => router.navigate({ pathname: '/subscriptions', params: { categoryId: c.id, filterLabel: c.name } });

  return (
    <Screen
      refreshing={loading && !!data}
      onRefresh={() => { void reload(); void subs.reload(); void burn.reload(); }}
      header={(
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingVertical: space.sm, paddingHorizontal: space.sm }}>
          <IconButton icon="arrow-left" label="Back" onPress={() => router.back()} />
          <Text variant="titleLargeEmphasized" accessibilityRole="header" style={{ flex: 1 }} numberOfLines={1}>Categories</Text>
          <Button label="New" icon="plus" variant="text" onPress={() => setAdding(true)} disabled={offline} />
        </View>
      )}
    >
      {offline ? <OfflineBanner /> : null}
      {error && data ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

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
              const editable = !c.isSystemDefault;
              const detail = describe(c);
              return (
                <ListItem
                  key={c.id}
                  position={positionOf(i, section.rows.length)}
                  title={(
                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
                      <Text variant="titleMedium" numberOfLines={1} color={section.used ? colors.onSurface : colors.onSurfaceVariant} style={{ flexShrink: 1 }}>{c.name}</Text>
                      {editable ? <Pill label="Yours" /> : null}
                    </View>
                  )}
                  subtitle={detail}
                  accessibilityLabel={[c.name, editable ? 'yours' : null, detail].filter(Boolean).join(', ')}
                  leading={<Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />}
                  onPress={section.used && detail ? () => open(c) : undefined}
                  onLongPress={editable && !offline ? () => setMenuFor(c) : undefined}
                  actions={editable && !offline ? [{ label: 'Rename or delete', run: () => setMenuFor(c) }] : undefined}
                  trailing={editable
                    ? <IconButton icon="dots-vertical" label={`Manage ${c.name}`} onPress={() => setMenuFor(c)} disabled={offline} />
                    : detail ? <Icon name="chevron-right" size={24} color={colors.onSurfaceVariant} /> : null}
                />
              );
            })}
          </View>
        ))}
      </AsyncView>

      {confirmDialog}
      <Sheet visible={adding} title="New category" onClose={() => { setAdding(false); setName(''); setAddError(null); }}>
        <View style={{ gap: space.lg, paddingHorizontal: space.xxl }}>
          {addError ? <Banner tone="error" text={addError} actionLabel={canAdd ? 'Retry' : undefined} onAction={() => void add()} /> : null}
          {/* The keyboard's Done obeys the button: it must not send a blank name, or write while offline. */}
          <TextField label="Name" value={name} onChangeText={setName} placeholder="e.g. Music" autoFocus onSubmitEditing={() => { if (canAdd) void add(); }} returnKeyType="done" />
          <Button label="Add category" icon="plus" onPress={add} loading={busy === 'add'} disabled={!canAdd} style={{ alignSelf: 'flex-end' }} />
        </View>
      </Sheet>
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

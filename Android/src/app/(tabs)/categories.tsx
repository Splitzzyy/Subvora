import { useState } from 'react';
import { Alert, View } from 'react-native';
import { categoriesApi, isStatus, writeErrorMessage, type Category } from '@/api';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { useSession } from '@/lib/session';
import { useTheme } from '@/theme';
import { Banner, Button, Card, IconButton, ListItem, OfflineBanner, positionOf, ScreenHeader, SectionHeader, Spinner, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { ActionSheet, PromptDialog } from '@/ui/sheets';

const DUPLICATE = 'A category with this name already exists.';
const byName = (a: Category, b: Category) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

export default function Categories() {
  const { category } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const { data, loading, error, reload } = useLoader(categoriesApi.list);
  const [name, setName] = useState('');
  const [writeError, setWriteError] = useState<string | null>(null);
  const [menuFor, setMenuFor] = useState<Category | null>(null);
  const [renaming, setRenaming] = useState<Category | null>(null);

  /** Every write moves the dashboard breakdown or the list grouping, so all of them announce it. */
  const write = async (action: () => Promise<void>) => {
    setWriteError(null);
    try {
      await action();
      haptics.confirm();
      dataChanged();
    } catch (e) {
      haptics.reject();
      setWriteError(isStatus(e, 409) ? DUPLICATE : writeErrorMessage(e));
    }
  };

  const add = () => write(async () => {
    await categoriesApi.create(name.trim());
    setName('');
  });

  const rename = (target: Category, newName: string) => {
    setRenaming(null);
    if (!newName.trim() || newName.trim() === target.name) return;
    void write(async () => { await categoriesApi.rename(target.id, newName.trim()); });
  };

  // Subscriptions survive a delete (FK is SET NULL) but lose their grouping - say so first.
  const remove = (target: Category) => Alert.alert(
    'Delete category',
    `Delete "${target.name}"? Subscriptions using it will stay, but become uncategorised.`,
    [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete', style: 'destructive', onPress: () => write(async () => {
          const { subscriptionsUncategorized: n } = await categoriesApi.remove(target.id);
          if (n > 0) Alert.alert('Category deleted', `${n} ${n === 1 ? 'subscription is' : 'subscriptions are'} now uncategorised.`);
        }),
      },
    ],
  );

  // Yours first - the half you can act on. System defaults are shared and read-only.
  const sections = [
    { title: 'Your categories', rows: (data ?? []).filter((c) => !c.isSystemDefault).sort(byName), editable: true },
    { title: 'System', rows: (data ?? []).filter((c) => c.isSystemDefault).sort(byName), editable: false },
  ].filter((s) => s.rows.length > 0);

  return (
    <Screen refreshing={loading && !!data} onRefresh={reload}>
      <ScreenHeader subtitle="Organise" title="Categories" />
      {offline ? <OfflineBanner /> : null}
      {error ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}
      {writeError ? <Banner tone="error" text={writeError} /> : null}

      <Card tone="default">
        <TextField label="New category" value={name} onChangeText={setName} placeholder="e.g. Music" onSubmitEditing={add} returnKeyType="done" />
        <Button label="Add category" icon="plus" variant="tonal" onPress={add} disabled={offline || !name.trim()} style={{ alignSelf: 'flex-end' }} />
      </Card>

      {!data && loading ? <Spinner /> : null}

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
                leading={<Tile icon={id.icon} color={id.ink} background={id.soft} size={40} />}
                onLongPress={section.editable && !offline ? () => setMenuFor(c) : undefined}
                trailing={section.editable
                  ? <IconButton icon="dots-vertical" label={`Manage ${c.name}`} onPress={() => setMenuFor(c)} disabled={offline} />
                  : null}
              />
            );
          })}
        </View>
      ))}

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

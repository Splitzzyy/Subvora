import DateTimePicker, { DateTimePickerAndroid } from '@react-native-community/datetimepicker';
import { useMemo, useState } from 'react';
import { FlatList, KeyboardAvoidingView, Modal, Platform, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { formatLongDate, parseDateOnly, toDateOnly } from '@/lib/logic';
import { shape, useTheme } from '@/theme';
import { Button, Icon, IconButton, SelectField, Tap, Text, TextField, type IconName } from '.';

/** Modal bottom sheet. The backdrop is a sibling, so scrolling the content never dismisses it. */
export function Sheet({ visible, title, onClose, children }: { visible: boolean; title: string; onClose: () => void; children: React.ReactNode }) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <Modal visible={visible} transparent animationType="slide" statusBarTranslucent navigationBarTranslucent onRequestClose={onClose}>
      <KeyboardAvoidingView style={{ flex: 1, justifyContent: 'flex-end' }} behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>
        <Pressable style={[StyleSheet.absoluteFill, { backgroundColor: colors.scrim }]} onPress={onClose} accessibilityLabel={`Dismiss ${title}`} />
        <View accessibilityViewIsModal style={{
          backgroundColor: colors.surfaceContainerLow, borderTopLeftRadius: shape.extraLarge, borderTopRightRadius: shape.extraLarge,
          paddingTop: 12, paddingBottom: Math.max(insets.bottom, 16), maxHeight: '85%',
        }}>
          <View style={{ width: 32, height: 4, borderRadius: 2, backgroundColor: colors.outlineVariant, alignSelf: 'center', marginBottom: 8 }} />
          <View style={{ flexDirection: 'row', alignItems: 'center', paddingHorizontal: 24, paddingBottom: 8 }}>
            <Text variant="titleLarge" accessibilityRole="header" style={{ flex: 1 }} numberOfLines={1}>{title}</Text>
            <IconButton icon="close" label={`Close ${title}`} onPress={onClose} />
          </View>
          {children}
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

export interface SheetAction { label: string; icon: IconName; destructive?: boolean; onPress: () => void }

export function ActionSheet({ title, actions, onClose }: { title: string | null; actions: SheetAction[]; onClose: () => void }) {
  const { colors } = useTheme();
  return (
    <Sheet visible={title !== null} title={title ?? ''} onClose={onClose}>
      <View style={{ paddingHorizontal: 12 }}>
        {actions.map((action) => {
          const fg = action.destructive ? colors.error : colors.onSurface;
          return (
            <Tap key={action.label} radius={shape.medium} accessibilityRole="button" onPress={() => { onClose(); action.onPress(); }}
              style={{ flexDirection: 'row', alignItems: 'center', gap: 16, paddingHorizontal: 16, paddingVertical: 16 }}>
              <Icon name={action.icon} size={24} color={fg} />
              <Text variant="bodyLarge" color={fg}>{action.label}</Text>
            </Tap>
          );
        })}
      </View>
    </Sheet>
  );
}

export interface PickerOption { value: string; label: string; caption?: string; icon?: IconName; iconColor?: string; iconBackground?: string }

/** Searchable single-choice list. `custom` turns an unmatched query into an option of its own. */
export function PickerSheet({ visible, title, options, selected, onSelect, onClose, searchable, custom }: {
  visible: boolean; title: string; options: PickerOption[]; selected: string | null; onSelect: (value: string) => void;
  onClose: () => void; searchable?: boolean; custom?: (query: string) => PickerOption | null;
}) {
  const { colors } = useTheme();
  const [query, setQuery] = useState('');
  const close = () => { setQuery(''); onClose(); };

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    const matches = q ? options.filter((o) => o.label.toLowerCase().includes(q) || o.value.toLowerCase().includes(q)) : options;
    const extra = custom?.(query.trim());
    return extra && !options.some((o) => o.value === extra.value) ? [extra, ...matches] : matches;
  }, [options, query, custom]);

  return (
    <Sheet visible={visible} title={title} onClose={close}>
      {searchable ? (
        <View style={{ paddingHorizontal: 24, paddingBottom: 8 }}>
          <TextField label="Search" value={query} onChangeText={setQuery} autoCapitalize="none" autoCorrect={false} />
        </View>
      ) : null}
      <FlatList
        data={shown}
        keyExtractor={(o) => o.value}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={{ paddingHorizontal: 12, paddingBottom: 8 }}
        ListEmptyComponent={<Text variant="bodyMedium" color={colors.onSurfaceVariant} style={{ padding: 16 }}>Nothing matches.</Text>}
        renderItem={({ item }) => {
          const isSelected = item.value === selected;
          return (
            <Tap radius={shape.medium} accessibilityRole="radio" accessibilityState={{ selected: isSelected }} accessibilityLabel={item.label}
              onPress={() => { onSelect(item.value); close(); }}
              style={{ flexDirection: 'row', alignItems: 'center', gap: 16, paddingHorizontal: 16, paddingVertical: 12, backgroundColor: isSelected ? colors.secondaryContainer : 'transparent' }}>
              {item.icon ? (
                <View style={{ width: 36, height: 36, borderRadius: 12, backgroundColor: item.iconBackground ?? colors.surfaceContainerHighest, alignItems: 'center', justifyContent: 'center' }}>
                  <Icon name={item.icon} size={20} color={item.iconColor ?? colors.onSurfaceVariant} />
                </View>
              ) : item.caption ? (
                <Text variant="titleMedium" color={colors.primary} style={{ width: 36, textAlign: 'center' }}>{item.caption}</Text>
              ) : null}
              <Text variant="bodyLarge" style={{ flex: 1 }} color={isSelected ? colors.onSecondaryContainer : colors.onSurface}>{item.label}</Text>
              {isSelected ? <Icon name="check" size={22} color={colors.onSecondaryContainer} /> : null}
            </Tap>
          );
        }}
      />
    </Sheet>
  );
}

interface PromptProps { title: string; label: string; initial: string; confirmLabel: string; onSubmit: (value: string) => void; onClose: () => void }

/** Text-input dialog - Android has no Alert.prompt. Mounted per opening, so it starts from `initial`. */
export function PromptDialog({ visible, ...props }: PromptProps & { visible: boolean }) {
  return (
    <Modal visible={visible} transparent animationType="fade" statusBarTranslucent onRequestClose={props.onClose}>
      {visible ? <PromptBody {...props} /> : null}
    </Modal>
  );
}

function PromptBody({ title, label, initial, confirmLabel, onSubmit, onClose }: PromptProps) {
  const { colors } = useTheme();
  const [value, setValue] = useState(initial);
  return (
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : 'height'} style={{ flex: 1, justifyContent: 'center', padding: 24, backgroundColor: colors.scrim }}>
        <View accessibilityViewIsModal style={{ backgroundColor: colors.surfaceContainerHigh, borderRadius: shape.extraLarge, padding: 24, gap: 16 }}>
          <Text variant="headlineSmall" accessibilityRole="header">{title}</Text>
          <TextField label={label} value={value} onChangeText={setValue} autoFocus onSubmitEditing={() => onSubmit(value)} />
          <View style={{ flexDirection: 'row', justifyContent: 'flex-end', gap: 8 }}>
            <Button label="Cancel" variant="text" onPress={onClose} />
            <Button label={confirmLabel} variant="text" onPress={() => onSubmit(value)} disabled={!value.trim()} />
          </View>
        </View>
      </KeyboardAvoidingView>
  );
}

/** Date-only field ("yyyy-MM-dd"). Android opens the native Material date dialog. */
export function DateField({ label, value, onChange, disabled }: { label: string; value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const { colors } = useTheme();
  const [iosOpen, setIosOpen] = useState(false);
  const open = () => {
    if (Platform.OS !== 'android') return setIosOpen(true);
    DateTimePickerAndroid.open({
      value: parseDateOnly(value),
      mode: 'date',
      onValueChange: (_, date) => onChange(toDateOnly(date)),
    });
  };
  return (
    <>
      <SelectField label={label} value={formatLongDate(value)} placeholder="" onPress={open} disabled={disabled}
        leading={<Icon name="calendar-month-outline" size={22} color={colors.onSurfaceVariant} />} />
      {iosOpen ? (
        <DateTimePicker value={parseDateOnly(value)} mode="date" display="inline"
          onValueChange={(_, date) => { setIosOpen(false); onChange(toDateOnly(date)); }} onDismiss={() => setIosOpen(false)} />
      ) : null}
    </>
  );
}

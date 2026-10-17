import DateTimePicker, { DateTimePickerAndroid } from '@react-native-community/datetimepicker';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Animated, FlatList, KeyboardAvoidingView, Modal, PanResponder, Platform, Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { formatLongDate, parseDateOnly, toDateOnly } from '@/lib/logic';
import { useReducedMotion } from '@/lib/motion';
import { motion, shape, space, useTheme } from '@/theme';
import { Button, Icon, IconButton, SelectField, Tap, Text, TextField, type IconName } from '.';

/**
 * Modal bottom sheet. The scrim fades in on its own while only the sheet rises (spatial spring), and
 * the sheet stays mounted for its exit. The handle and header drag it down: past 30% of its height,
 * or with a flick, it closes. The backdrop is a sibling, so scrolling the content never dismisses it.
 */
export function Sheet({ visible, title, onClose, children }: { visible: boolean; title: string; onClose: () => void; children: React.ReactNode }) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const reduced = useReducedMotion();
  const { height: windowHeight } = useWindowDimensions();
  const [mounted, setMounted] = useState(visible);
  const [offset] = useState(() => new Animated.Value(windowHeight));
  const [sheetHeight, setSheetHeight] = useState(windowHeight);
  if (visible && !mounted) setMounted(true);

  useEffect(() => {
    if (visible) {
      if (reduced) offset.setValue(0);
      else Animated.spring(offset, { toValue: 0, ...motion.spatialDefault, useNativeDriver: true }).start();
      return;
    }
    // Out without overshoot, then unmount; reduced motion just lets the Modal fade.
    const exit = reduced
      ? Animated.timing(offset, { toValue: sheetHeight, duration: 0, useNativeDriver: true })
      : Animated.spring(offset, { toValue: sheetHeight, ...motion.effectsDefault, useNativeDriver: true });
    exit.start(() => setMounted(false));
    return () => exit.stop();
  }, [visible, reduced, offset, sheetHeight]);

  const drag = useMemo(() => PanResponder.create({
    onMoveShouldSetPanResponder: (_, g) => g.dy > 4 && Math.abs(g.dy) > Math.abs(g.dx),
    onPanResponderMove: (_, g) => offset.setValue(Math.max(0, g.dy)),
    onPanResponderRelease: (_, g) => {
      if (g.dy > sheetHeight * 0.3 || g.vy > 1) onClose();
      else Animated.spring(offset, { toValue: 0, ...motion.spatialFast, useNativeDriver: true }).start();
    },
  }), [offset, sheetHeight, onClose]);

  return (
    <Modal visible={mounted} transparent animationType="fade" statusBarTranslucent navigationBarTranslucent onRequestClose={onClose}>
      <KeyboardAvoidingView style={{ flex: 1, justifyContent: 'flex-end' }} behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>
        <Pressable style={[StyleSheet.absoluteFill, { backgroundColor: colors.scrim }]} onPress={onClose} accessibilityLabel={`Dismiss ${title}`} />
        <Animated.View
          accessibilityViewIsModal
          onLayout={(e) => setSheetHeight(e.nativeEvent.layout.height)}
          style={{
            backgroundColor: colors.surfaceContainerLow, borderTopLeftRadius: shape.extraLarge, borderTopRightRadius: shape.extraLarge,
            paddingTop: space.md, paddingBottom: Math.max(insets.bottom, space.lg), maxHeight: '85%',
            transform: [{ translateY: offset }],
          }}
        >
          <View {...drag.panHandlers}>
            <View style={{ width: 32, height: 4, borderRadius: shape.full, backgroundColor: colors.outline, alignSelf: 'center', marginBottom: space.sm }} />
            <View style={{ flexDirection: 'row', alignItems: 'center', paddingHorizontal: space.xxl, paddingBottom: space.sm }}>
              <Text variant="titleLarge" accessibilityRole="header" style={{ flex: 1 }} numberOfLines={1}>{title}</Text>
              <IconButton icon="close" label={`Close ${title}`} onPress={onClose} />
            </View>
          </View>
          {children}
        </Animated.View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

export interface SheetAction { label: string; icon: IconName; destructive?: boolean; onPress: () => void }

export function ActionSheet({ title, actions, onClose }: { title: string | null; actions: SheetAction[]; onClose: () => void }) {
  const { colors } = useTheme();
  // Callers clear the title (and so the actions) on close; the sheet keeps the last ones while it
  // slides away rather than emptying first. Only an open sheet's actions can run.
  const [last, setLast] = useState({ title: title ?? '', actions });
  if (title !== null && (title !== last.title || actions !== last.actions)) setLast({ title, actions });
  const open = title !== null;
  return (
    <Sheet visible={open} title={open ? title : last.title} onClose={onClose}>
      <View style={{ paddingHorizontal: space.md }}>
        {(open ? actions : last.actions).map((action) => {
          const fg = action.destructive ? colors.error : colors.onSurface;
          return (
            <Tap key={action.label} radius={shape.medium} accessibilityRole="button" accessibilityLabel={action.label} onPress={() => { if (!open) return; onClose(); action.onPress(); }}
              style={{ flexDirection: 'row', alignItems: 'center', gap: space.lg, paddingHorizontal: space.lg, paddingVertical: space.lg, minHeight: 56 }}>
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
  const list = useRef<FlatList<PickerOption>>(null);
  /** Placed on the selection once per opening, so a later relayout (the keyboard) never jumps it back. */
  const placed = useRef(false);
  const close = () => { setQuery(''); placed.current = false; onClose(); };

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    const matches = q ? options.filter((o) => o.label.toLowerCase().includes(q) || o.value.toLowerCase().includes(q)) : options;
    const extra = custom?.(query.trim());
    return extra && !options.some((o) => o.value === extra.value) ? [extra, ...matches] : matches;
  }, [options, query, custom]);
  const selectedIndex = shown.findIndex((o) => o.value === selected);

  return (
    <Sheet visible={visible} title={title} onClose={close}>
      {searchable ? (
        <View style={{ paddingHorizontal: space.xxl, paddingBottom: space.sm }}>
          <TextField label="Search" value={query} onChangeText={setQuery} autoCapitalize="none" autoCorrect={false} />
        </View>
      ) : null}
      <FlatList
        ref={list}
        data={shown}
        keyExtractor={(o) => o.value}
        // Opens on the current choice rather than the top of a long list (currencies run to ~40).
        onLayout={() => {
          if (placed.current) return;
          placed.current = true;
          if (!query && selectedIndex > 0) list.current?.scrollToIndex({ index: selectedIndex, viewPosition: 0.3, animated: false });
        }}
        // Rows are not measured yet on the first layout: render through the selection, then retry once they are.
        initialNumToRender={Math.max(10, selectedIndex + 4)}
        onScrollToIndexFailed={({ index }) => setTimeout(() => list.current?.scrollToIndex({ index, viewPosition: 0.3, animated: false }), 50)}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={{ paddingHorizontal: space.md, paddingBottom: space.sm }}
        ListEmptyComponent={<Text variant="bodyMedium" color={colors.onSurfaceVariant} style={{ padding: space.lg }}>Nothing matches.</Text>}
        renderItem={({ item }) => {
          const isSelected = item.value === selected;
          return (
            <Tap radius={shape.medium} accessibilityRole="radio" accessibilityState={{ selected: isSelected }} accessibilityLabel={item.label}
              onPress={() => { onSelect(item.value); close(); }}
              style={{ flexDirection: 'row', alignItems: 'center', gap: space.lg, paddingHorizontal: space.lg, paddingVertical: space.md, minHeight: 56, backgroundColor: isSelected ? colors.secondaryContainer : 'transparent' }}>
              {item.icon ? (
                <View style={{ width: 36, height: 36, borderRadius: shape.small, backgroundColor: item.iconBackground ?? colors.surfaceContainerHighest, alignItems: 'center', justifyContent: 'center' }}>
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
  const [value, setValue] = useState(initial);
  return (
    <DialogFrame onDismiss={onClose}>
      <Text variant="headlineSmall" accessibilityRole="header">{title}</Text>
      <TextField label={label} value={value} onChangeText={setValue} autoFocus onSubmitEditing={() => onSubmit(value)} />
      <View style={{ flexDirection: 'row', justifyContent: 'flex-end', gap: space.sm }}>
        <Button label="Cancel" variant="text" onPress={onClose} />
        <Button label={confirmLabel} variant="text" onPress={() => onSubmit(value)} disabled={!value.trim()} />
      </View>
    </DialogFrame>
  );
}

/**
 * The M3 basic dialog's scrim and container. The Modal fades the scrim; the container grows in from
 * 90% on the spatial spring, as M3 dialogs enter. Tapping the scrim dismisses, as Cancel does.
 */
function DialogFrame({ onDismiss, children }: { onDismiss: () => void; children: React.ReactNode }) {
  const { colors } = useTheme();
  const reduced = useReducedMotion();
  const [enter] = useState(() => new Animated.Value(reduced ? 1 : 0));
  useEffect(() => {
    if (!reduced) Animated.spring(enter, { toValue: 1, ...motion.spatialDefault, useNativeDriver: true }).start();
  }, [enter, reduced]);
  return (
    <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : 'height'} style={{ flex: 1, justifyContent: 'center', padding: space.xxl }}>
      <Pressable style={[StyleSheet.absoluteFill, { backgroundColor: colors.scrim }]} onPress={onDismiss} accessibilityLabel="Dismiss dialog" />
      <Animated.View accessibilityViewIsModal style={{
        backgroundColor: colors.surfaceContainerHigh, borderRadius: shape.extraLarge, padding: space.xxl, gap: space.lg,
        opacity: enter.interpolate({ inputRange: [0, 1], outputRange: [0, 1], extrapolate: 'clamp' }),
        transform: [{ scale: enter.interpolate({ inputRange: [0, 1], outputRange: [0.9, 1] }) }],
      }}>
        {children}
      </Animated.View>
    </KeyboardAvoidingView>
  );
}

export interface ConfirmRequest {
  title: string;
  body: string;
  confirmLabel: string;
  /** Data loss: the confirm action reads in the error colour. */
  destructive?: boolean;
  onConfirm: () => void;
}

/**
 * M3 basic dialog for confirmations, in place of the native Alert (which the OEM themes: system
 * font, uppercase buttons in the platform accent). Use through useConfirm().
 */
export function ConfirmDialog({ request, onClose }: { request: ConfirmRequest | null; onClose: () => void }) {
  const { colors } = useTheme();
  // The last request stays on screen while the Modal fades out, rather than the card vanishing
  // and leaving a bare scrim; only a live request can be confirmed, so a tap mid-fade does nothing.
  const [last, setLast] = useState(request);
  if (request && request !== last) setLast(request);
  const shown = request ?? last;
  return (
    <Modal visible={request !== null} transparent animationType="fade" statusBarTranslucent onRequestClose={onClose}>
      {shown ? (
        <DialogFrame onDismiss={onClose}>
          <Text variant="headlineSmall" accessibilityRole="header">{shown.title}</Text>
          <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{shown.body}</Text>
          <View style={{ flexDirection: 'row', justifyContent: 'flex-end', flexWrap: 'wrap', gap: space.sm }}>
            <Button label="Cancel" variant="text" onPress={onClose} />
            <Button
              label={shown.confirmLabel} variant={shown.destructive ? 'textDanger' : 'text'}
              onPress={() => { if (!request) return; onClose(); request.onConfirm(); }}
            />
          </View>
        </DialogFrame>
      ) : null}
    </Modal>
  );
}

/** `[dialog, confirm]`: render `dialog` once, call `confirm({...})` to ask. */
export function useConfirm() {
  const [request, setRequest] = useState<ConfirmRequest | null>(null);
  const dialog = <ConfirmDialog request={request} onClose={() => setRequest(null)} />;
  return [dialog, setRequest] as const;
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

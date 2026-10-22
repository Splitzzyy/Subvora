import Constants from 'expo-constants';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Linking, View } from 'react-native';
import { API_BASE_URL, categoriesApi, isRetryable, paymentSourcesApi, subscriptionsApi, usersApi, writeErrorMessage } from '@/api';
import { CACHE_KEYS } from '@/lib/cache';
import { CURRENCIES, currencyOption } from '@/lib/currencies';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { currencySymbol, initialOf } from '@/lib/logic';
import { reminderPermission, requestReminderPermission, sendTestReminder } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { space, supportsDynamicColor, useTheme, type ThemeMode } from '@/theme';
import { Banner, Button, ButtonGroup, Card, Icon, ListItem, OfflineBanner, positionOf, SectionHeader, SelectField, SwitchRow, Text, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { PickerSheet } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';
import { AsyncView, FormSkeleton } from '@/ui/states';

const customCurrency = (q: string) => (/^[A-Za-z]{3}$/.test(q) ? currencyOption(q.toUpperCase()) : null);
/** The server's floor when no default is set (SubscriptionsController.DefaultAlertDaysAdvance). */
const SERVER_DEFAULT_DAYS = 3;
const PRESET_DAYS = [1, 3, 7];
const daysLabel = (d: number) => (d === 7 ? '1 week' : `${d} ${d === 1 ? 'day' : 'days'}`);

/**
 * Settings grouped by task (#282): the account, the set-up lists that left the tab bar, reminders
 * and preferences. Every choice here saves when made - there is no Save button to miss.
 */
export default function Settings() {
  const theme = useTheme();
  const { colors } = theme;
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data: profile, setData: setProfile, loading, error, errorStatus, reload } = useLoader(usersApi.me);
  // Counts for the Manage rows only; they show nothing rather than an error if a list cannot load.
  const { data: categories } = useLoader(categoriesApi.list);
  const { data: sources } = useLoader(paymentSourcesApi.list);
  const { data: subs } = useLoader(subscriptionsApi.list, CACHE_KEYS.subscriptions);

  const [saving, setSaving] = useState<'currency' | 'reminder' | null>(null);
  const [pickingCurrency, setPickingCurrency] = useState(false);
  const [permission, setPermission] = useState<{ granted: boolean; canAskAgain: boolean } | null>(null);
  useFocusEffect(useCallback(() => { void reminderPermission().then(setPermission); }, []));

  const currency = profile?.preferredCurrency ?? '';
  const currencies = currency && !CURRENCIES.some((c) => c.value === currency) ? [currencyOption(currency), ...CURRENCIES] : CURRENCIES;
  const defaultDays = profile?.defaultAlertDaysAdvance ?? SERVER_DEFAULT_DAYS;
  const dayOptions = [...new Set([...PRESET_DAYS, defaultDays])].sort((a, b) => a - b);

  // The profile as last saved, read when a write is sent rather than when its button was drawn: one
  // PUT carries both preferences, so a currency Undo or a Retry built from an older render would
  // also put back a reminder choice made since.
  const latest = useRef(profile);
  useEffect(() => { latest.current = profile; }, [profile]);
  /** One preference write at a time; Undo and Retry come from snackbars the disabled controls do not cover. */
  const inFlight = useRef(false);

  /** Changes one preference and keeps the other as last saved. `undoOf` marks the undo of a currency change. */
  const update = async (change: { currency: string } | { days: number | null }, undoOf?: string) => {
    const base = latest.current;
    if (!base || inFlight.current) return;
    const kind = 'currency' in change ? 'currency' : 'reminder';
    const nextCurrency = 'currency' in change ? change.currency : base.preferredCurrency;
    const nextDays = 'days' in change ? change.days : base.defaultAlertDaysAdvance;
    inFlight.current = true;
    setSaving(kind);
    try {
      const saved = await usersApi.updateMe(nextCurrency, nextDays);
      latest.current = saved;
      setProfile(saved);
      haptics.confirm();
      // The home currency is what every burn-rate figure converts into.
      dataChanged();
      if (kind === 'reminder') snackbar({ text: `New subscriptions remind you ${daysLabel(nextDays ?? SERVER_DEFAULT_DAYS)} before.` });
      else if (undoOf) snackbar({ text: `Home currency set back to ${nextCurrency}.` });
      else snackbar({ text: `Home currency set to ${nextCurrency}.`, actionLabel: 'Undo', onAction: () => undoCurrency(base.preferredCurrency, nextCurrency) });
    } catch (e) {
      haptics.reject();
      snackbar({
        tone: 'error', text: writeErrorMessage(e),
        ...(isRetryable(e) ? { actionLabel: 'Retry', onAction: () => void update(change, undoOf) } : {}),
      });
    } finally {
      inFlight.current = false;
      setSaving(null);
    }
  };

  /** Puts the earlier currency back - unless a later change replaced this one, which leaves nothing to undo. */
  const undoCurrency = (previous: string, changedTo: string) => {
    if (latest.current?.preferredCurrency !== changedTo) return;
    void update({ currency: previous }, changedTo);
  };

  const turnOnNotifications = async () => {
    if (permission?.canAskAgain) setPermission({ granted: await requestReminderPermission(), canAskAgain: true });
    else void Linking.openSettings();
  };
  const test = async () => {
    snackbar(await sendTestReminder()
      ? { text: 'A test reminder is on its way.' }
      : { tone: 'error', text: "Couldn't send a test reminder. Check that notifications are on for SubVora." });
  };

  const inUse = categories && subs ? new Set(subs.map((s) => s.categoryId).filter(Boolean)).size : null;

  return (
    <Screen title="Settings" refreshing={loading && !!profile} onRefresh={reload}>
      {offline ? <OfflineBanner /> : null}
      {error && profile ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

      <View style={{ gap: space.md }}>
        <AsyncView
          state={asyncState({ hasData: !!profile, error, errorStatus })}
          skeleton={<FormSkeleton cards={1} />}
          error={error}
          errorStatus={errorStatus}
          onRetry={reload}
        >
          {profile ? (
            <ListItem
              position="single"
              onPress={() => router.push('/settings/account')}
              accessibilityLabel={`Account, ${profile.email}`}
              leading={<Tile letter={initialOf(profile.email)} color={colors.onPrimary} background={colors.primary} size={48} />}
              title={profile.email}
              subtitle="Password, sign out, delete account"
              trailing={<Icon name="chevron-right" size={24} color={colors.onSurfaceVariant} />}
            />
          ) : null}
        </AsyncView>

        <View>
          <SectionHeader title="Manage" />
          {[
            { title: 'Categories', icon: 'shape-outline' as const, href: '/settings/categories' as const,
              subtitle: categories ? (inUse !== null ? `${inUse} in use · ${categories.length} total` : `${categories.length} categories`) : 'Group your subscriptions' },
            { title: 'Payment sources', icon: 'credit-card-outline' as const, href: '/settings/payment-sources' as const,
              subtitle: sources ? `${sources.length} ${sources.length === 1 ? 'source' : 'sources'}` : 'Cards and accounts you pay from' },
          ].map((row, i) => (
            <ListItem
              key={row.title}
              position={positionOf(i, 2)}
              onPress={() => router.push(row.href)}
              accessibilityLabel={`${row.title}, ${row.subtitle}`}
              leading={<Tile icon={row.icon} color={colors.onSecondaryContainer} background={colors.secondaryContainer} size={40} />}
              title={row.title}
              subtitle={row.subtitle}
              trailing={<Icon name="chevron-right" size={24} color={colors.onSurfaceVariant} />}
            />
          ))}
        </View>

        <Card>
          <Text variant="titleLargeEmphasized" accessibilityRole="header">Reminders</Text>
          <Text variant="bodyMedium" color={colors.onSurfaceVariant}>Remind me before each renewal</Text>
          <ButtonGroup<string>
            value={String(defaultDays)}
            onChange={(v) => void update({ days: Number(v) })}
            disabled={!profile || offline || saving !== null}
            options={dayOptions.map((d) => ({ value: String(d), label: daysLabel(d) }))}
          />
          <Text variant="bodySmall" color={colors.onSurfaceVariant}>For new subscriptions. Each subscription can change this.</Text>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, paddingTop: space.sm }}>
            <Icon name={permission?.granted ? 'bell-check-outline' : 'bell-off-outline'} size={24}
              color={permission?.granted ? colors.success : colors.renewalWarning} />
            <View style={{ flex: 1 }}>
              <Text variant="bodyLarge">Notifications</Text>
              <Text variant="bodyMedium" color={colors.onSurfaceVariant}>
                {permission === null ? 'Checking…' : permission.granted ? 'Allowed' : 'Off - reminders cannot reach you'}
              </Text>
            </View>
            {permission?.granted
              ? <Button label="Send test" variant="text" onPress={() => void test()} />
              : permission ? <Button label="Turn on" variant="tonal" onPress={() => void turnOnNotifications()} /> : null}
          </View>
        </Card>

        <Card>
          <Text variant="titleLargeEmphasized" accessibilityRole="header">Preferences</Text>
          <SelectField
            label="Home currency" placeholder="Choose a currency" disabled={!profile || offline || saving !== null}
            value={currency ? currencyOption(currency).label : null}
            onPress={() => setPickingCurrency(true)}
            // The chosen currency's own sign; a fixed "$" icon read as dollars whatever was picked.
            leading={(
              <View style={{ minWidth: 24, alignItems: 'center' }}>
                <Text variant="titleLarge" color={colors.onSurfaceVariant} numberOfLines={1}>{currency ? currencySymbol(currency) : '¤'}</Text>
              </View>
            )}
          />
          <Text variant="bodyMedium" color={colors.onSurfaceVariant}>Every total is converted into this currency. Subscriptions keep their own.</Text>
          <Text variant="labelLarge" color={colors.onSurfaceVariant} style={{ paddingTop: space.sm }}>Appearance</Text>
          <ButtonGroup<ThemeMode>
            value={theme.mode}
            onChange={theme.setMode}
            options={[
              { value: 'system', label: 'System', icon: 'theme-light-dark' },
              { value: 'light', label: 'Light', icon: 'white-balance-sunny' },
              { value: 'dark', label: 'Dark', icon: 'weather-night' },
            ]}
          />
          {supportsDynamicColor ? (
            <SwitchRow label="Wallpaper colours" caption="Tint buttons and highlights from your wallpaper (Material You)." value={theme.dynamic} onValueChange={theme.setDynamic} />
          ) : null}
        </Card>

        {/* The version helps support; the API address is for developers only. */}
        <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center', paddingTop: space.sm }}>
          SubVora {Constants.expoConfig?.version ?? ''}{__DEV__ ? ` · ${API_BASE_URL}` : ''}
        </Text>
      </View>

      <PickerSheet
        visible={pickingCurrency}
        title="Home currency"
        options={currencies}
        selected={currency}
        onSelect={(code) => { if (code !== currency) void update({ currency: code }); }}
        onClose={() => setPickingCurrency(false)}
        searchable
        custom={customCurrency}
      />
    </Screen>
  );
}

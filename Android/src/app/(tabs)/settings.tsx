import { useState } from 'react';
import { Alert, View } from 'react-native';
import { API_BASE_URL, authApi, tokens, usersApi, writeErrorMessage } from '@/api';
import { CURRENCIES, currencyOption } from '@/lib/currencies';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { currencySymbol, initialOf } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { space, supportsDynamicColor, useTheme, type ThemeMode } from '@/theme';
import { Banner, Button, ButtonGroup, Card, OfflineBanner, SelectField, SwitchRow, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { PickerSheet } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';
import { AsyncView, FormSkeleton } from '@/ui/states';

const customCurrency = (q: string) => (/^[A-Za-z]{3}$/.test(q) ? currencyOption(q.toUpperCase()) : null);

export default function Settings() {
  const theme = useTheme();
  const { colors } = theme;
  const { signOut, dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data: profile, setData: setProfile, loading, error, errorStatus, reload } = useLoader(usersApi.me);

  // Drafts over the loaded profile: null means "unchanged", so a fresh profile shows through as-is.
  const [currencyDraft, setCurrency] = useState<string | null>(null);
  const [alertDaysDraft, setAlertDays] = useState<string | null>(null);
  const currency = currencyDraft ?? profile?.preferredCurrency ?? '';
  const alertDays = alertDaysDraft ?? profile?.defaultAlertDaysAdvance?.toString() ?? '';
  const [pickingCurrency, setPickingCurrency] = useState(false);
  const [saving, setSaving] = useState(false);
  const [profileError, setProfileError] = useState<string | null>(null);

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [changing, setChanging] = useState(false);

  // A stored code the list does not know must still be selectable, or Save would silently change it.
  const currencies = currency && !CURRENCIES.some((c) => c.value === currency) ? [currencyOption(currency), ...CURRENCIES] : CURRENCIES;
  const days = alertDays.trim() === '' ? null : Number(alertDays);
  const daysInvalid = days !== null && (!Number.isInteger(days) || days < 0);

  const save = async () => {
    setProfileError(null);
    setSaving(true);
    try {
      setProfile(await usersApi.updateMe(currency, days));
      setCurrency(null);
      setAlertDays(null);
      snackbar({ text: 'Preferences saved. Your dashboard now uses them.' });
      haptics.confirm();
      // The home currency is what every burn-rate figure converts into.
      dataChanged();
    } catch (e) {
      haptics.reject();
      setProfileError(writeErrorMessage(e));
    } finally {
      setSaving(false);
    }
  };

  const changePassword = async () => {
    setPasswordError(null);
    setChanging(true);
    try {
      // Success revokes every refresh token on the account, this device's included: keep the new pair
      // or the next 401 signs this phone out too.
      await tokens.save(await authApi.changePassword(currentPassword, newPassword));
      setCurrentPassword('');
      setNewPassword('');
      snackbar({ text: 'Password changed. Any other devices have been signed out.' });
      haptics.confirm();
    } catch (e) {
      haptics.reject();
      // 400 covers a wrong current password and a weak new one; the server's message says which.
      setPasswordError(writeErrorMessage(e));
    } finally {
      setChanging(false);
    }
  };

  const confirmSignOut = () => Alert.alert('Sign out', 'Are you sure you want to sign out?', [
    { text: 'Cancel', style: 'cancel' },
    { text: 'Sign out', style: 'destructive', onPress: () => void signOut() },
  ]);

  return (
    <Screen title="Settings" subtitle="Account" refreshing={loading && !!profile} onRefresh={reload}>
      {offline ? <OfflineBanner /> : null}
      {error && profile ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}

      <View style={{ gap: space.md }}>
        {/* Only the account half needs the server; appearance, password and sign-out stay usable. */}
        <AsyncView
          state={asyncState({ hasData: !!profile, error, errorStatus })}
          skeleton={<FormSkeleton cards={2} />}
          error={error}
          errorStatus={errorStatus}
          onRetry={reload}
        >
          <View style={{ gap: space.md }}>
            {profile ? (
              <Card tone="primary" style={{ flexDirection: 'row', alignItems: 'center', gap: space.lg }}>
                <Tile letter={initialOf(profile.email)} color={colors.onPrimary} background={colors.primary} size={56} />
                <View style={{ flex: 1 }}>
                  <Text variant="labelLarge" color={colors.onPrimaryContainer}>Signed in as</Text>
                  <Text variant="titleMedium" color={colors.onPrimaryContainer} numberOfLines={1}>{profile.email}</Text>
                </View>
              </Card>
            ) : null}

            <Card>
              <Text variant="titleLargeEmphasized" accessibilityRole="header">Preferences</Text>
              {profileError ? <Banner tone="error" text={profileError} /> : null}
              <SelectField
                label="Home currency" placeholder="Choose a currency" disabled={!profile}
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
              <TextField
                label="Default reminder (days before renewal)" value={alertDays} keyboardType="number-pad" placeholder="Server default"
                onChangeText={(v) => setAlertDays(v.replace(/\D/g, ''))}
                error={daysInvalid ? 'Enter a whole number of days.' : null}
              />
              <Button label="Save preferences" onPress={save} loading={saving} disabled={!profile || offline || daysInvalid || !currency} style={{ alignSelf: 'flex-end' }} />
            </Card>
          </View>
        </AsyncView>

        <Card>
          <Text variant="titleLargeEmphasized" accessibilityRole="header">Appearance</Text>
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

        <Card>
          <Text variant="titleLargeEmphasized" accessibilityRole="header">Change password</Text>
          {passwordError ? <Banner tone="error" text={passwordError} /> : null}
          <TextField label="Current password" value={currentPassword} onChangeText={setCurrentPassword} secure autoComplete="current-password" />
          <TextField label="New password" value={newPassword} onChangeText={setNewPassword} secure autoComplete="new-password" />
          <Button label="Change password" variant="tonal" onPress={changePassword} loading={changing} disabled={offline || !currentPassword || !newPassword} style={{ alignSelf: 'flex-end' }} />
        </Card>

        <Button label="Sign out" icon="logout" variant="danger" size="lg" onPress={confirmSignOut} />
        <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center', paddingTop: space.sm }}>{API_BASE_URL}</Text>
      </View>

      <PickerSheet
        visible={pickingCurrency}
        title="Home currency"
        options={currencies}
        selected={currency}
        onSelect={setCurrency}
        onClose={() => setPickingCurrency(false)}
        searchable
        custom={customCurrency}
      />
    </Screen>
  );
}

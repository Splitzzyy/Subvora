import { useRef, useState } from 'react';
import Constants from 'expo-constants';
import { View, type TextInput } from 'react-native';
import { API_BASE_URL, authApi, tokens, usersApi, writeErrorMessage } from '@/api';
import { CURRENCIES, currencyOption } from '@/lib/currencies';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { currencySymbol, initialOf, PASSWORD_HINT } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { space, supportsDynamicColor, useTheme, type ThemeMode } from '@/theme';
import { Banner, Button, ButtonGroup, Card, OfflineBanner, SelectField, SwitchRow, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { PickerSheet, Sheet, useConfirm } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';
import { AsyncView, FormSkeleton } from '@/ui/states';

const customCurrency = (q: string) => (/^[A-Za-z]{3}$/.test(q) ? currencyOption(q.toUpperCase()) : null);

export default function Settings() {
  const theme = useTheme();
  const { colors } = theme;
  const { signOut, endLocally, dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data: profile, setData: setProfile, loading, error, errorStatus, reload } = useLoader(usersApi.me);

  // The reminder default is a draft over the loaded profile (null: unchanged) with its own Save. The
  // home currency is a single choice, so it applies as soon as it is picked, with an Undo.
  const [alertDaysDraft, setAlertDays] = useState<string | null>(null);
  const currency = profile?.preferredCurrency ?? '';
  const alertDays = alertDaysDraft ?? profile?.defaultAlertDaysAdvance?.toString() ?? '';
  const [applyingCurrency, setApplyingCurrency] = useState(false);
  const [confirmDialog, confirm] = useConfirm();
  const [pickingCurrency, setPickingCurrency] = useState(false);
  const [saving, setSaving] = useState(false);
  const [profileError, setProfileError] = useState<string | null>(null);

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [changing, setChanging] = useState(false);
  const newPasswordField = useRef<TextInput>(null);
  const canChangePassword = !offline && !changing && !!currentPassword && !!newPassword;

  const [deletingAccount, setDeletingAccount] = useState(false);
  const [deletePassword, setDeletePassword] = useState('');
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const canDelete = !offline && !deleting && !!deletePassword;

  // A stored code the list does not know must still be selectable, or Save would silently change it.
  const currencies = currency && !CURRENCIES.some((c) => c.value === currency) ? [currencyOption(currency), ...CURRENCIES] : CURRENCIES;
  const days = alertDays.trim() === '' ? null : Number(alertDays);
  const daysInvalid = days !== null && (!Number.isInteger(days) || days < 0);

  /** `keepDays` is the saved reminder default at pick time, so an unsaved draft there is not sent along. */
  const setHomeCurrency = async (code: string, keepDays: number | null, undoOf?: string) => {
    setProfileError(null);
    setApplyingCurrency(true);
    try {
      setProfile(await usersApi.updateMe(code, keepDays));
      haptics.confirm();
      // The home currency is what every burn-rate figure converts into.
      dataChanged();
      snackbar(undoOf
        ? { text: `Home currency set back to ${code}.` }
        : { text: `Home currency set to ${code}.`, actionLabel: 'Undo', onAction: () => void setHomeCurrency(currency, keepDays, code) });
    } catch (e) {
      haptics.reject();
      snackbar({ tone: 'error', text: writeErrorMessage(e) });
    } finally {
      setApplyingCurrency(false);
    }
  };

  const save = async () => {
    setProfileError(null);
    setSaving(true);
    try {
      setProfile(await usersApi.updateMe(currency, days));
      setAlertDays(null);
      snackbar({ text: 'Reminder default saved.' });
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

  const closeDelete = () => {
    if (deleting) return;
    setDeletingAccount(false);
    setDeletePassword('');
    setDeleteError(null);
  };

  const deleteAccount = async () => {
    setDeleteError(null);
    setDeleting(true);
    try {
      await usersApi.deleteMe(deletePassword);
      haptics.confirm();
      // The snackbar lives above the router, so it is still showing once the login screen is.
      snackbar({ text: 'Account deleted.' });
      // Not signOut: its server logout would only find the session already gone with the account.
      await endLocally();
    } catch (e) {
      haptics.reject();
      // 400 is a wrong current password; the server's title says so in change-password's words.
      setDeleteError(writeErrorMessage(e));
      setDeleting(false);
    }
  };

  const confirmSignOut = () => confirm({
    title: 'Sign out', body: 'Are you sure you want to sign out?', confirmLabel: 'Sign out', onConfirm: () => void signOut(),
  });

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
                label="Home currency" placeholder="Choose a currency" disabled={!profile || offline || applyingCurrency}
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
              <Button label="Save reminder" onPress={save} loading={saving} disabled={!profile || offline || daysInvalid || alertDaysDraft === null} style={{ alignSelf: 'flex-end' }} />
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
          <TextField label="Current password" value={currentPassword} onChangeText={setCurrentPassword} secure autoComplete="current-password"
            returnKeyType="next" submitBehavior="submit" onSubmitEditing={() => newPasswordField.current?.focus()} />
          <TextField ref={newPasswordField} label="New password" value={newPassword} onChangeText={setNewPassword} secure autoComplete="new-password"
            supporting={PASSWORD_HINT} returnKeyType="done" onSubmitEditing={() => { if (canChangePassword) void changePassword(); }} />
          <Button label="Change password" variant="tonal" onPress={changePassword} loading={changing} disabled={offline || !currentPassword || !newPassword} style={{ alignSelf: 'flex-end' }} />
        </Card>

        {/* Not red: signing out loses nothing. Red is for errors and data loss. */}
        <Button label="Sign out" icon="logout" variant="outlined" size="lg" onPress={confirmSignOut} />
        {/* Google Play requires an in-app way to delete the account. Red: this one loses everything. */}
        <Button label="Delete account" icon="delete-outline" variant="textDanger" onPress={() => setDeletingAccount(true)} disabled={offline} />
        {/* The version helps support; the API address is for developers only. */}
        <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center', paddingTop: space.sm }}>
          SubVora {Constants.expoConfig?.version ?? ''}{__DEV__ ? ` · ${API_BASE_URL}` : ''}
        </Text>
      </View>

      {confirmDialog}
      <Sheet visible={deletingAccount} title="Delete account" onClose={closeDelete}>
        <View style={{ gap: space.lg, paddingHorizontal: space.xxl }}>
          <Text variant="bodyMedium" color={colors.onSurfaceVariant}>
            This permanently deletes your subscriptions, categories and payment sources, and signs out every device. It cannot be undone.
          </Text>
          {deleteError ? <Banner tone="error" text={deleteError} /> : null}
          <TextField label="Current password" value={deletePassword} onChangeText={setDeletePassword} secure autoComplete="current-password"
            returnKeyType="done" onSubmitEditing={() => { if (canDelete) void deleteAccount(); }} />
          <View style={{ flexDirection: 'row', justifyContent: 'flex-end', gap: space.sm }}>
            <Button label="Cancel" variant="text" onPress={closeDelete} disabled={deleting} />
            <Button label="Delete account" variant="danger" onPress={deleteAccount} loading={deleting} disabled={!canDelete} />
          </View>
        </View>
      </Sheet>
      <PickerSheet
        visible={pickingCurrency}
        title="Home currency"
        options={currencies}
        selected={currency}
        onSelect={(code) => { if (profile && code !== currency) void setHomeCurrency(code, profile.defaultAlertDaysAdvance); }}
        onClose={() => setPickingCurrency(false)}
        searchable
        custom={customCurrency}
      />
    </Screen>
  );
}

import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { View, type TextInput } from 'react-native';
import { authApi, isRetryable, tokens, usersApi, writeErrorMessage } from '@/api';
import * as haptics from '@/lib/haptics';
import { useLoader, useOffline } from '@/lib/hooks';
import { initialOf, PASSWORD_HINT } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { space, useTheme } from '@/theme';
import { Banner, Button, Card, IconButton, OfflineBanner, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { Sheet, useConfirm } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';

/**
 * The account and what can end it (#282): the password form on a screen of its own, rather than
 * open in the middle of Settings, then sign-out and deletion.
 */
export default function Account() {
  const { colors } = useTheme();
  const { signOut, endLocally } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const { data: profile } = useLoader(usersApi.me);
  const [confirmDialog, confirm] = useConfirm();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [passwordError, setPasswordError] = useState<{ text: string; retry: boolean } | null>(null);
  const [changing, setChanging] = useState(false);
  const newPasswordField = useRef<TextInput>(null);
  const canChangePassword = !offline && !changing && !!currentPassword && !!newPassword;

  const [deletingAccount, setDeletingAccount] = useState(false);
  const [deletePassword, setDeletePassword] = useState('');
  const [deleteError, setDeleteError] = useState<{ text: string; retry: boolean } | null>(null);
  const [deleting, setDeleting] = useState(false);
  const canDelete = !offline && !deleting && !!deletePassword;

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
      setPasswordError({ text: writeErrorMessage(e), retry: isRetryable(e) });
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
      // Not signOut: its server logout would only find the session already gone with the account.
      // After it, not before: ending the session clears every message about it (#285).
      await endLocally();
      snackbar({ text: 'Account deleted.' });
    } catch (e) {
      haptics.reject();
      // 400 is a wrong current password; the server's title says so in change-password's words.
      setDeleteError({ text: writeErrorMessage(e), retry: isRetryable(e) });
      setDeleting(false);
    }
  };

  const confirmSignOut = () => confirm({
    title: 'Sign out', body: 'Are you sure you want to sign out?', confirmLabel: 'Sign out', onConfirm: () => void signOut(),
  });

  return (
    <Screen header={(
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingVertical: space.sm, paddingHorizontal: space.sm }}>
        <IconButton icon="arrow-left" label="Back" onPress={() => router.back()} />
        <Text variant="titleLargeEmphasized" accessibilityRole="header" style={{ flex: 1 }} numberOfLines={1}>Account</Text>
      </View>
    )}>
      {offline ? <OfflineBanner /> : null}
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
          <Text variant="titleLargeEmphasized" accessibilityRole="header">Change password</Text>
          {passwordError ? (
            <Banner tone="error" text={passwordError.text} actionLabel={passwordError.retry && canChangePassword ? 'Retry' : undefined} onAction={() => void changePassword()} />
          ) : null}
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
      </View>

      {confirmDialog}
      <Sheet visible={deletingAccount} title="Delete account" onClose={closeDelete}>
        <View style={{ gap: space.lg, paddingHorizontal: space.xxl }}>
          <Text variant="bodyMedium" color={colors.onSurfaceVariant}>
            This permanently deletes your subscriptions, categories and payment sources, and signs out every device. It cannot be undone.
          </Text>
          {deleteError ? (
            <Banner tone="error" text={deleteError.text} actionLabel={deleteError.retry && canDelete ? 'Retry' : undefined} onAction={() => void deleteAccount()} />
          ) : null}
          <TextField label="Current password" value={deletePassword} onChangeText={setDeletePassword} secure autoComplete="current-password"
            returnKeyType="done" onSubmitEditing={() => { if (canDelete) void deleteAccount(); }} />
          <View style={{ flexDirection: 'row', justifyContent: 'flex-end', gap: space.sm }}>
            <Button label="Cancel" variant="text" onPress={closeDelete} disabled={deleting} />
            <Button label="Delete account" variant="danger" onPress={deleteAccount} loading={deleting} disabled={!canDelete} />
          </View>
        </View>
      </Sheet>
    </Screen>
  );
}

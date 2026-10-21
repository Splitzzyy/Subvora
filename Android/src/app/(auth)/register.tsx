import { router } from 'expo-router';
import { useRef, useState } from 'react';
import type { TextInput } from 'react-native';
import { authApi, errorMessage } from '@/api';
import * as haptics from '@/lib/haptics';
import { PASSWORD_HINT } from '@/lib/logic';
import { requestReminderPermission } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { Banner, Button, TextField } from '@/ui';
import { AuthScaffold } from '@/ui/layout';

export default function Register() {
  const { signIn } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  /** A mismatch belongs under the field that has to change, not in a banner above the form. */
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const passwordField = useRef<TextInput>(null);
  const confirmField = useRef<TextInput>(null);

  const register = async () => {
    setError(null);
    if (password !== confirm) {
      haptics.reject();
      return setConfirmError('Passwords do not match.');
    }
    setBusy(true);
    try {
      await authApi.register(email.trim(), password);
      // Register answers 202 whether or not the address was taken, so it cannot be used to probe
      // for accounts. The login is what tells the two apart for whoever holds the password.
      try {
        await signIn(await authApi.login(email.trim(), password));
        haptics.confirm();
        // Same moment as after login: signed in, so the reminder offer makes sense.
        void requestReminderPermission();
        return;
      } catch {
        setError('Please log in to continue.');
      }
    } catch (e) {
      setError(errorMessage(e));
    }
    haptics.reject();
    setBusy(false);
  };

  return (
    <AuthScaffold title="Create account" subtitle="Track subscriptions, see your burn rate, never miss a renewal.">
      {error ? <Banner tone="error" text={error} actionLabel={error === 'Please log in to continue.' ? 'Sign in' : undefined} onAction={() => router.back()} /> : null}
      <TextField label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} autoComplete="email" keyboardType="email-address"
        returnKeyType="next" submitBehavior="submit" onSubmitEditing={() => passwordField.current?.focus()} />
      <TextField ref={passwordField} label="Password" value={password} onChangeText={(v) => { setPassword(v); setConfirmError(null); }} secure autoComplete="new-password"
        supporting={PASSWORD_HINT} returnKeyType="next" submitBehavior="submit" onSubmitEditing={() => confirmField.current?.focus()} />
      <TextField ref={confirmField} label="Confirm password" value={confirm} onChangeText={(v) => { setConfirm(v); setConfirmError(null); }} secure autoComplete="new-password"
        error={confirmError} returnKeyType="go" onSubmitEditing={() => { if (!busy && email.trim() && password && confirm) void register(); }} />
      <Button label="Create account" size="lg" onPress={register} loading={busy} disabled={!email.trim() || !password || !confirm} />
      <Button label="I already have an account" variant="text" onPress={() => router.back()} />
    </AuthScaffold>
  );
}

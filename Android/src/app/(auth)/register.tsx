import { router } from 'expo-router';
import { useState } from 'react';
import { authApi, errorMessage } from '@/api';
import * as haptics from '@/lib/haptics';
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

  const register = async () => {
    setError(null);
    if (password !== confirm) return setError('Passwords do not match.');
    setBusy(true);
    try {
      await authApi.register(email.trim(), password);
      // Register answers 202 whether or not the address was taken, so it cannot be used to probe
      // for accounts. The login is what tells the two apart for whoever holds the password.
      try {
        await signIn(await authApi.login(email.trim(), password));
        haptics.confirm();
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
      <TextField label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} autoComplete="email" keyboardType="email-address" />
      <TextField label="Password" value={password} onChangeText={setPassword} secure autoComplete="new-password" />
      <TextField label="Confirm password" value={confirm} onChangeText={setConfirm} secure autoComplete="new-password" onSubmitEditing={register} />
      <Button label="Create account" size="lg" onPress={register} loading={busy} disabled={!email.trim() || !password || !confirm} />
      <Button label="I already have an account" variant="text" onPress={() => router.back()} />
    </AuthScaffold>
  );
}

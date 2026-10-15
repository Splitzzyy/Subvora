import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { authApi, errorMessage, isStatus } from '@/api';
import * as haptics from '@/lib/haptics';
import { requestReminderPermission } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { Banner, Button, TextField } from '@/ui';
import { AuthScaffold } from '@/ui/layout';

export default function Login() {
  const { signIn } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const login = async () => {
    setError(null);
    setBusy(true);
    try {
      await signIn(await authApi.login(email.trim(), password));
      haptics.confirm();
      // Asked here rather than on cold start: having just signed in, "we can remind you before a
      // charge lands" is obvious.
      void requestReminderPermission();
    } catch (e) {
      haptics.reject();
      // Login's own 401 is bad credentials, not an expired session.
      setError(isStatus(e, 401) ? 'Invalid email or password.' : errorMessage(e));
      setBusy(false);
    }
  };

  return (
    <AuthScaffold title="Welcome back" subtitle="Every renewal, before it lands.">
      {error ? <Banner tone="error" text={error} /> : null}
      <TextField label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} autoComplete="email" keyboardType="email-address" textContentType="emailAddress" />
      <TextField label="Password" value={password} onChangeText={setPassword} secure autoComplete="current-password" onSubmitEditing={login} />
      <Button label="Sign in" size="lg" onPress={login} loading={busy} disabled={!email.trim() || !password} />
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', flexWrap: 'wrap' }}>
        <Button label="Forgot password?" variant="text" onPress={() => router.push('/forgot-password')} />
        <Button label="Create account" variant="text" onPress={() => router.push('/register')} />
      </View>
    </AuthScaffold>
  );
}

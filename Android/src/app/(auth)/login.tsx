import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { View, type TextInput } from 'react-native';
import { authApi, errorMessage, isStatus } from '@/api';
import * as haptics from '@/lib/haptics';
import { formatCountdown, signInCooldown } from '@/lib/logic';
import { requestReminderPermission } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { clearFailures, readFailures, recordFailure } from '@/lib/signInAttempts';
import { Banner, Button, TextField } from '@/ui';
import { AuthScaffold } from '@/ui/layout';

const WRONG = 'Invalid email or password.';
const LOCKED = 'Too many attempts. Wait 15 minutes, or reset your password.';

export default function Login() {
  const { signIn } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  /** When the 3-tries cooldown for this address ends (#290), or null. */
  const [lockedUntil, setLockedUntil] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const passwordField = useRef<TextInput>(null);
  const locked = lockedUntil !== null && lockedUntil > now;
  const canSubmit = !busy && !locked && !!email.trim() && !!password;

  // The cooldown belongs to the address: typing another one shows that one's state.
  useEffect(() => {
    let live = true;
    const timer = setTimeout(() => {
      void readFailures(email).then((failures) => {
        if (!live) return;
        const state = signInCooldown(failures, Date.now());
        setLockedUntil(state.locked ? Date.now() + state.remainingMs : null);
        setError((current) => (state.locked ? LOCKED : current === LOCKED ? null : current));
      });
    }, 300);
    return () => { live = false; clearTimeout(timer); };
  }, [email]);

  // The countdown on the button; when it runs out, Sign in comes back by itself.
  useEffect(() => {
    if (lockedUntil === null) return;
    const tick = setInterval(() => {
      const t = Date.now();
      setNow(t);
      if (t >= lockedUntil) {
        setLockedUntil(null);
        setError((current) => (current === LOCKED ? null : current));
      }
    }, 1000);
    return () => clearInterval(tick);
  }, [lockedUntil]);

  const login = async () => {
    if (!canSubmit) return;
    setError(null);
    setBusy(true);
    try {
      await signIn(await authApi.login(email.trim(), password));
      void clearFailures(email);
      haptics.confirm();
      // Asked here rather than on cold start: having just signed in, "we can remind you before a
      // charge lands" is obvious.
      void requestReminderPermission();
    } catch (e) {
      haptics.reject();
      if (isStatus(e, 401)) {
        // Counted on this device only, and the same for addresses with and without an account, so
        // it says nothing the server's single 401 does not. The server keeps its own, later lockout.
        const state = signInCooldown(await recordFailure(email), Date.now());
        // Cleared, so a refused password is not offered to the password manager either (#289).
        setPassword('');
        setNow(Date.now());
        setLockedUntil(state.locked ? Date.now() + state.remainingMs : null);
        setError(state.locked ? LOCKED : WRONG);
      } else {
        setError(errorMessage(e));
      }
      setBusy(false);
    }
  };

  const reset = () => router.push({ pathname: '/forgot-password', params: { email: email.trim() } });

  return (
    <AuthScaffold title="Welcome back" subtitle="Every renewal, before it lands.">
      {error ? (
        <Banner tone="error" text={error} actionLabel={error === WRONG || error === LOCKED ? 'Reset password' : undefined} onAction={reset} />
      ) : null}
      <TextField label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} autoComplete="email" keyboardType="email-address" textContentType="emailAddress"
        returnKeyType="next" submitBehavior="submit" onSubmitEditing={() => passwordField.current?.focus()} />
      {/* Go signs in only when the button could: never with a blank field, during the cooldown, or with a sign-in already out. */}
      <TextField ref={passwordField} label="Password" value={password} onChangeText={setPassword} secure autoComplete="current-password"
        returnKeyType="go" onSubmitEditing={() => void login()} />
      <Button label={locked ? `Try again in ${formatCountdown(lockedUntil! - now)}` : 'Sign in'} size="lg" onPress={login} loading={busy}
        disabled={locked || !email.trim() || !password} />
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', flexWrap: 'wrap' }}>
        <Button label="Forgot password?" variant="text" onPress={reset} />
        <Button label="Create account" variant="text" onPress={() => router.push('/register')} />
      </View>
    </AuthScaffold>
  );
}

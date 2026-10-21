import { router } from 'expo-router';
import { useRef, useState } from 'react';
import type { TextInput } from 'react-native';
import { authApi, fieldErrorMessage, isStatus, writeErrorMessage } from '@/api';
import { useOffline } from '@/lib/hooks';
import { PASSWORD_HINT } from '@/lib/logic';
import { requestReminderPermission } from '@/lib/reminders';
import { useSession } from '@/lib/session';
import { Banner, Button, OfflineBanner, TextField } from '@/ui';
import { AuthScaffold } from '@/ui/layout';
import { useSnackbar } from '@/ui/snackbar';

/** Two states on one screen: ask for a code, then spend it. The second step needs the first's email. */
export default function ForgotPassword() {
  const offline = useOffline();
  const { signIn } = useSession();
  const snackbar = useSnackbar();
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [newPassword, setNewPassword] = useState('');
  // Never unset: going back would discard a code the user may already have received.
  const [codeSent, setCodeSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const passwordField = useRef<TextInput>(null);
  const canRequest = !busy && !offline && !!email.trim();
  const canReset = !busy && !offline && code.length === 6 && !!newPassword;

  const run = async (action: () => Promise<void>) => {
    setError(null);
    setInfo(null);
    setBusy(true);
    try { await action(); } finally { setBusy(false); }
  };

  const requestCode = () => run(async () => {
    try {
      await authApi.forgotPassword(email.trim());
      // Hedged: the server will not say whether the address has an account, so neither do we.
      setInfo("If that address has an account, we've sent it a 6-digit code. It expires in 15 minutes.");
      setCodeSent(true);
    } catch (e) {
      setError(writeErrorMessage(e));
    }
  });

  const reset = () => run(async () => {
    try {
      const session = await authApi.resetPassword(email.trim(), code.trim(), newPassword);
      // Signed straight in: recovery must not depend on login, which someone guessing at this
      // address may be keeping locked. The session change routes to the app on its own.
      if (session?.accessToken) {
        await signIn(session);
        void requestReminderPermission();
        return;
      }
      snackbar({ text: 'Password reset. Sign in with your new password.' });
      router.back();
    } catch (e) {
      // One 400 for wrong, expired, used and over-limit - deliberately, so guessing learns nothing. A
      // 400 naming a field is the new password itself (too short) instead.
      setError(fieldErrorMessage(e) ?? (isStatus(e, 400)
        ? "That code isn't valid. It may have expired or already been used - request a new one if you need to."
        : writeErrorMessage(e)));
    }
  });

  return (
    <AuthScaffold title="Reset password" subtitle={codeSent ? 'Enter the code from your email and choose a new password.' : "We'll email you a 6-digit code."}>
      {offline ? <OfflineBanner /> : null}
      {info ? <Banner tone="info" text={info} /> : null}
      {error ? <Banner tone="error" text={error} /> : null}
      <TextField label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" editable={!codeSent}
        returnKeyType="send" onSubmitEditing={() => { if (canRequest) void requestCode(); }} />
      {codeSent ? (
        <>
          <TextField label="6-digit code" value={code} onChangeText={(v) => setCode(v.replace(/\D/g, '').slice(0, 6))} keyboardType="number-pad" autoComplete="one-time-code"
            returnKeyType="next" submitBehavior="submit" onSubmitEditing={() => passwordField.current?.focus()} />
          <TextField ref={passwordField} label="New password" value={newPassword} onChangeText={setNewPassword} secure autoComplete="new-password"
            supporting={PASSWORD_HINT} returnKeyType="done" onSubmitEditing={() => { if (canReset) void reset(); }} />
          <Button label="Reset password" size="lg" onPress={reset} loading={busy} disabled={offline || code.length !== 6 || !newPassword} />
          <Button label="Send a new code" variant="text" onPress={requestCode} disabled={busy || offline} />
        </>
      ) : (
        <Button label="Send code" size="lg" onPress={requestCode} loading={busy} disabled={offline || !email.trim()} />
      )}
      <Button label="Back to sign in" variant="text" onPress={() => router.back()} />
    </AuthScaffold>
  );
}

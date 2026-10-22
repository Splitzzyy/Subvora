import Storage from 'expo-sqlite/kv-store';
import { SIGN_IN_COOLDOWN_MS, SIGN_IN_WINDOW_MS } from './logic';

/**
 * Rejected sign-ins per address, on this device (#290), so the 3-tries cooldown survives closing
 * the app. The rule itself is signInCooldown in logic.ts. Best-effort: storage failing never blocks
 * signing in.
 */
const key = (email: string) => `auth:failures:${email.trim().toLowerCase()}`;
const KEEP_MS = SIGN_IN_WINDOW_MS + SIGN_IN_COOLDOWN_MS;

export async function readFailures(email: string): Promise<number[]> {
  try {
    const stored = JSON.parse((await Storage.getItemAsync(key(email))) ?? '[]');
    return Array.isArray(stored) ? stored.filter((t): t is number => typeof t === 'number') : [];
  } catch {
    return [];
  }
}

export async function recordFailure(email: string, now = Date.now()): Promise<number[]> {
  const failures = [...(await readFailures(email)).filter((t) => now - t < KEEP_MS), now];
  await Storage.setItemAsync(key(email), JSON.stringify(failures)).catch(() => undefined);
  return failures;
}

/** After a successful sign-in or password reset for that address. */
export const clearFailures = (email: string) => Storage.removeItemAsync(key(email)).catch(() => undefined);

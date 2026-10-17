import Storage from 'expo-sqlite/kv-store';

/**
 * Read-only mirror of the last successful GETs, so the dashboard and list still show something
 * offline. There are no offline writes: no outbox, no replay (decided in #144, see CLAUDE.md).
 * Each entry keeps when it was saved beside it (UI metadata for the offline banner).
 */
export const CACHE_KEYS = { subscriptions: 'cache:subscriptions', burnRate: 'cache:burn-rate' } as const;
export type CacheKey = (typeof CACHE_KEYS)[keyof typeof CACHE_KEYS];

const savedAtKey = (key: CacheKey) => `${key}:saved-at`;

/** The mirrored value and when it was saved (null for a mirror written before timestamps existed). */
export async function readCache<T>(key: CacheKey): Promise<{ data: T; savedAt: string | null } | null> {
  try {
    const value = await Storage.getItemAsync(key);
    if (!value) return null;
    return { data: JSON.parse(value) as T, savedAt: await Storage.getItemAsync(savedAtKey(key)) };
  } catch {
    return null;
  }
}

export async function writeCache(key: CacheKey, value: unknown) {
  try {
    await Storage.setItemAsync(key, JSON.stringify(value));
    await Storage.setItemAsync(savedAtKey(key), new Date().toISOString());
  } catch { /* the mirror is best-effort */ }
}

/** On sign-out and expiry: the next person to open the app offline must not see this account. */
export async function clearCache() {
  for (const key of Object.values(CACHE_KEYS)) {
    try {
      await Storage.removeItemAsync(key);
      await Storage.removeItemAsync(savedAtKey(key));
    } catch { /* best-effort */ }
  }
}

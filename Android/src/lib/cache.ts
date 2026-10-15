import Storage from 'expo-sqlite/kv-store';

/**
 * Read-only mirror of the last successful GETs, so the dashboard and list still show something
 * offline. There are no offline writes: no outbox, no replay (decided in #144, see CLAUDE.md).
 */
export const CACHE_KEYS = { subscriptions: 'cache:subscriptions', burnRate: 'cache:burn-rate' } as const;
export type CacheKey = (typeof CACHE_KEYS)[keyof typeof CACHE_KEYS];

export async function readCache<T>(key: CacheKey): Promise<T | null> {
  try {
    const value = await Storage.getItemAsync(key);
    return value ? (JSON.parse(value) as T) : null;
  } catch {
    return null;
  }
}

export async function writeCache(key: CacheKey, value: unknown) {
  try { await Storage.setItemAsync(key, JSON.stringify(value)); } catch { /* the mirror is best-effort */ }
}

/** On sign-out and expiry: the next person to open the app offline must not see this account. */
export async function clearCache() {
  for (const key of Object.values(CACHE_KEYS)) {
    try { await Storage.removeItemAsync(key); } catch { /* best-effort */ }
  }
}

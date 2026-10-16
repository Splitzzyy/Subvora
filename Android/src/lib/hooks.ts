import { useNetInfo } from '@react-native-community/netinfo';
import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, errorMessage } from '@/api';
import { readCache, writeCache, type CacheKey } from './cache';
import { loaderVisibility } from './loading';
import { useSession } from './session';

/**
 * Whether the phone has no network - gates write buttons so an edit that cannot succeed is not
 * offered. A reachable phone talking to a dead server still reads online; that case is caught by
 * the write failing and saying so.
 */
export function useOffline() {
  return useNetInfo().isConnected === false;
}

/**
 * `fetcher` must be stable (a module-level API function) - it is a dependency of the loader.
 * Loads on focus, but only when nothing is loaded yet or a write has happened since (dataVersion).
 * Tab switches therefore do not refetch. With a cacheKey, a failed load falls back to the mirror.
 */
export function useLoader<T>(fetcher: () => Promise<T>, cacheKey?: CacheKey) {
  const { dataVersion } = useSession();
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /** The failed request's status (0: no response): for the offline/error states, "More info" and the cached banner. */
  const [errorStatus, setErrorStatus] = useState<number | null>(null);
  const [cached, setCached] = useState(false);
  /** When the mirror on screen was saved; null when live, or for a mirror older than timestamps. */
  const [cachedAt, setCachedAt] = useState<string | null>(null);

  const versionRef = useRef(dataVersion);
  useEffect(() => { versionRef.current = dataVersion; }, [dataVersion]);
  const loadedVersion = useRef<number | null>(null);
  const inflight = useRef(false);

  const reload = useCallback(async () => {
    if (inflight.current) return;
    inflight.current = true;
    const version = versionRef.current;
    setLoading(true);
    setError(null);
    setErrorStatus(null);
    try {
      const result = await fetcher();
      setData(result);
      setCached(false);
      setCachedAt(null);
      loadedVersion.current = version;
      if (cacheKey) await writeCache(cacheKey, result);
    } catch (e) {
      const message = errorMessage(e);
      const mirror = cacheKey ? await readCache<T>(cacheKey) : null;
      if (mirror) {
        // Cached rows count as loaded - refetching on every tab tap would be a 30s spinner.
        setData(mirror.data);
        setCached(true);
        setCachedAt(mirror.savedAt);
        // Why the mirror is showing: no response (offline) reads differently from a server error.
        setErrorStatus(e instanceof ApiError ? e.status : null);
        loadedVersion.current = version;
      } else {
        // Nothing on screen, so loadedVersion stays put and the next visit retries.
        setError(message);
        setErrorStatus(e instanceof ApiError ? e.status : null);
        setCached(false);
      }
    } finally {
      inflight.current = false;
      setLoading(false);
    }
  }, [fetcher, cacheKey]);

  useFocusEffect(useCallback(() => {
    if (loadedVersion.current !== dataVersion) void reload();
  }, [dataVersion, reload]));

  return { data, setData, loading, error, errorStatus, cached, cachedAt, reload };
}

/**
 * Loader visibility with the timing rules in loading.ts: nothing for the first 150 ms, then at
 * least 300 ms once shown, so quick loads never flash and slow ones never blink.
 */
export function useDelayedLoading(loading: boolean) {
  const [visible, setVisible] = useState(false);
  const startedAt = useRef<number | null>(null);
  const shownAt = useRef<number | null>(null);
  useEffect(() => {
    startedAt.current = loading ? (startedAt.current ?? Date.now()) : null;
    let timer: ReturnType<typeof setTimeout>;
    const check = () => {
      const now = Date.now();
      const { visible: next, recheckIn } = loaderVisibility(loading, startedAt.current, shownAt.current, now);
      if (next && shownAt.current === null) shownAt.current = now;
      if (!next) shownAt.current = null;
      setVisible(next);
      if (recheckIn !== null) timer = setTimeout(check, recheckIn);
    };
    timer = setTimeout(check, 0);
    return () => clearTimeout(timer);
  }, [loading]);
  return visible;
}

/** Runs `fn` after `delayMs` of quiet; a newer call or cancel() drops the pending one. */
export function useDebounced(delayMs = 500) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cancel = useCallback(() => { if (timer.current) clearTimeout(timer.current); timer.current = null; }, []);
  const schedule = useCallback((fn: () => void) => {
    cancel();
    timer.current = setTimeout(fn, delayMs);
  }, [cancel, delayMs]);
  return { schedule, cancel };
}

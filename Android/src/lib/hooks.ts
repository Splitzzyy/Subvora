import { useNetInfo } from '@react-native-community/netinfo';
import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { errorMessage } from '@/api';
import { readCache, writeCache, type CacheKey } from './cache';
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
  const [cached, setCached] = useState(false);

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
    try {
      const result = await fetcher();
      setData(result);
      setCached(false);
      loadedVersion.current = version;
      if (cacheKey) await writeCache(cacheKey, result);
    } catch (e) {
      const message = errorMessage(e);
      const mirror = cacheKey ? await readCache<T>(cacheKey) : null;
      if (mirror) {
        // Cached rows count as loaded - refetching on every tab tap would be a 30s spinner.
        setData(mirror);
        setCached(true);
        loadedVersion.current = version;
      } else {
        // Nothing on screen, so loadedVersion stays put and the next visit retries.
        setError(message);
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

  return { data, setData, loading, error, cached, reload };
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

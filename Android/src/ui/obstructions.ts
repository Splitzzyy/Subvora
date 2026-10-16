import { useEffect, useSyncExternalStore } from 'react';

/**
 * What currently covers the bottom of the screen: the navigation bar and a FAB. The snackbar reads
 * this to sit directly above them, and only above a FAB when one is actually on screen.
 */
const counts = { navBar: 0, fab: 0 };
let snapshot = { navBar: false, fab: false };
const listeners = new Set<() => void>();

const change = (key: keyof typeof counts, delta: number) => {
  counts[key] += delta;
  snapshot = { navBar: counts.navBar > 0, fab: counts.fab > 0 };
  listeners.forEach((l) => l());
};

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
};

export const useObstructions = () => useSyncExternalStore(subscribe, () => snapshot);

/** Declares `key` on screen while `active`. */
export function useObstruction(key: keyof typeof counts, active: boolean) {
  useEffect(() => {
    if (!active) return;
    change(key, 1);
    return () => change(key, -1);
  }, [key, active]);
}

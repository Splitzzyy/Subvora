import { useSyncExternalStore } from 'react';
import { AccessibilityInfo } from 'react-native';

// One subscription for the whole app; components read it synchronously or through the hook.
let reduceMotion = false;
const listeners = new Set<() => void>();
const set = (value: boolean) => { reduceMotion = value; listeners.forEach((l) => l()); };
AccessibilityInfo.isReduceMotionEnabled().then(set).catch(() => undefined);
AccessibilityInfo.addEventListener('reduceMotionChanged', set);

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
};

/** The system "remove animations" setting, as of now. For event handlers. */
export const reduceMotionEnabled = () => reduceMotion;

/** Same, re-rendering when the setting changes. Animations fall back to plain fades when true. */
export const useReducedMotion = () => useSyncExternalStore(subscribe, reduceMotionEnabled);

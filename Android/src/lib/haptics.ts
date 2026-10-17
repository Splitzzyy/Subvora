import * as Haptics from 'expo-haptics';
import { Platform } from 'react-native';

// Best-effort: missing hardware or a disabled system setting must never break the action itself.
const safely = (run: () => Promise<void>) => { try { run().catch(() => undefined); } catch { /* ignore */ } };
const android = Platform.OS === 'android';

export const tick = () => safely(() => android
  ? Haptics.performAndroidHapticsAsync(Haptics.AndroidHaptics.Segment_Tick)
  : Haptics.selectionAsync());

export const confirm = () => safely(() => android
  ? Haptics.performAndroidHapticsAsync(Haptics.AndroidHaptics.Confirm)
  : Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success));

export const reject = () => safely(() => android
  ? Haptics.performAndroidHapticsAsync(Haptics.AndroidHaptics.Reject)
  : Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error));

export const longPress = () => safely(() => android
  ? Haptics.performAndroidHapticsAsync(Haptics.AndroidHaptics.Long_Press)
  : Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium));

import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';
import type { Subscription } from '@/api';
import { planReminders } from './logic';

/**
 * Renewal reminders are scheduled on-device and delivered by the OS with the app closed - there is
 * no push service. Every failure here is swallowed: reminders are a bonus, never a precondition.
 */
const CHANNEL_ID = 'renewals';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: false,
    shouldSetBadge: false,
  }),
});

async function ensureChannel() {
  if (Platform.OS === 'android') {
    await Notifications.setNotificationChannelAsync(CHANNEL_ID, {
      name: 'Renewal reminders',
      importance: Notifications.AndroidImportance.DEFAULT,
    });
  }
}

/** Asked right after sign-in, where "we can remind you before a charge lands" is obvious. */
export async function requestReminderPermission() {
  try {
    await ensureChannel();
    if ((await Notifications.getPermissionsAsync()).granted) return true;
    return (await Notifications.requestPermissionsAsync()).granted;
  } catch {
    return false;
  }
}

/** Full replace rather than a diff: at most 64 entries, and re-deriving beats reconciling. */
export async function syncReminders(subscriptions: Subscription[]) {
  try {
    await ensureChannel();
    await Notifications.cancelAllScheduledNotificationsAsync();
    for (const reminder of planReminders(subscriptions, new Date())) {
      await Notifications.scheduleNotificationAsync({
        content: { title: reminder.title, body: reminder.body },
        trigger: { type: Notifications.SchedulableTriggerInputTypes.DATE, date: reminder.notifyAt, channelId: CHANNEL_ID },
      });
    }
  } catch (error) {
    console.warn('Could not schedule renewal reminders.', error);
  }
}

export const cancelReminders = () => Notifications.cancelAllScheduledNotificationsAsync().catch(() => undefined);

import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';
import { subscriptionsApi, type Subscription } from '@/api';
import { CACHE_KEYS, writeCache } from './cache';
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

/**
 * One change to the schedule at a time. Two syncs interleaving (cancel, cancel, schedule, schedule)
 * would deliver every reminder twice; queued, the last list to arrive is the one that stands.
 */
let queue: Promise<unknown> = Promise.resolve();
const enqueue = (step: () => Promise<unknown>) => (queue = queue.then(step, step));
/** Bumped by sign-out, so a refresh that was already in flight cannot reschedule a signed-out account. */
let generation = 0;

/** Full replace rather than a diff: at most 64 entries, and re-deriving beats reconciling. */
export function syncReminders(subscriptions: Subscription[]) {
  return enqueue(async () => {
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
  });
}

/**
 * After a write that moves a date (add, edit, mark paid, delete): fetch the list and reschedule from
 * it, whichever screen the write came from. Without it only the Subscriptions tab rescheduled, so a
 * charge paid early from Home or the details screen kept its reminder until that tab was opened.
 * The fresh list also refreshes the offline mirror. A failure leaves the old schedule in place.
 */
export async function resyncReminders() {
  const started = generation;
  try {
    const subscriptions = await subscriptionsApi.list();
    if (started !== generation) return;
    await writeCache(CACHE_KEYS.subscriptions, subscriptions);
    await syncReminders(subscriptions);
  } catch {
    // Reminders are a bonus: the next list load reschedules.
  }
}

export const cancelReminders = () => {
  generation += 1;
  return enqueue(() => Notifications.cancelAllScheduledNotificationsAsync().catch(() => undefined));
};

/** Whether reminders can be shown, for Settings (#282); `canAskAgain` false means only system settings can change it. */
export async function reminderPermission(): Promise<{ granted: boolean; canAskAgain: boolean }> {
  try {
    const { granted, canAskAgain } = await Notifications.getPermissionsAsync();
    return { granted, canAskAgain };
  } catch {
    return { granted: false, canAskAgain: false };
  }
}

/** One local notification a few seconds out, so people can see what a reminder looks like. Local only - no push service. */
export async function sendTestReminder(): Promise<boolean> {
  try {
    await ensureChannel();
    await Notifications.scheduleNotificationAsync({
      content: { title: 'Subscription renewing soon', body: 'This is how SubVora reminds you before a charge lands.' },
      trigger: { type: Notifications.SchedulableTriggerInputTypes.TIME_INTERVAL, seconds: 3, channelId: CHANNEL_ID },
    });
    return true;
  } catch {
    return false;
  }
}

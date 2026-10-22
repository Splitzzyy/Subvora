import { useRef, useState } from 'react';
import { isRetryable, isStatus, subscriptionsApi, writeErrorMessage, type Subscription } from '@/api';
import { useConfirm } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';
import * as haptics from './haptics';
import { useOffline } from './hooks';
import { resyncReminders } from './reminders';
import { useSession } from './session';

const STALE_CHARGE = 'Not marked paid: this charge changed on another device. The list now shows what is due.';

/**
 * Mark as paid and Delete, shared by Home's Due soon card, the list and the details screen. One
 * write at a time: a second mark-paid landing on the first would advance the date two cycles.
 * Render `confirmDialog` once in the screen.
 */
export function useSubscriptionActions() {
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  /** Which write is in flight ("pay:<id>", "delete:<id>"), or null. Any value blocks a second write. */
  const [busy, setBusy] = useState<string | null>(null);
  /** The guard itself: state lags a render, and a fast double tap on Mark as paid lands in that gap. */
  const inFlight = useRef(false);
  const [confirmDialog, confirm] = useConfirm();

  const write = async (key: string, success: string, action: () => Promise<unknown>, after?: () => void) => {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(key);
    try {
      await action();
      haptics.confirm();
      dataChanged();
      // Wherever the write came from - the list, Home or the details screen.
      void resyncReminders();
      snackbar({ text: success });
      after?.();
    } catch (e) {
      haptics.reject();
      // Nothing is queued for later, so a failed write must say so or the OVERDUE chip lies. No
      // answer at all is worth a Retry, which resends the same request.
      snackbar({
        tone: 'error',
        text: isStatus(e, 409) ? STALE_CHARGE : writeErrorMessage(e),
        ...(isRetryable(e) ? { actionLabel: 'Retry', onAction: () => void write(key, success, action, after) } : {}),
      });
    } finally {
      inFlight.current = false;
      setBusy(null);
    }
  };

  /** Settles the charge on the date shown - so a retry or a second device settles it once, not the next one too. */
  const markPaid = (sub: Subscription) => write(`pay:${sub.id}`, `${sub.customName} marked as paid.`, async () => {
    try {
      await subscriptionsApi.markPaid(sub.id, sub.nextBillingDate);
    } catch (e) {
      // The charge on screen is no longer the one due - settled on another device, or its date
      // edited. Nothing was recorded: reload to show what is due now, and say why.
      if (isStatus(e, 409)) {
        dataChanged();
        void resyncReminders();
      }
      throw e;
    }
  });

  // No undo: there is no endpoint that restores a deleted subscription as it was.
  const remove = (sub: Subscription, after?: () => void) => confirm({
    title: 'Delete subscription', body: `Delete ${sub.customName}? This cannot be undone.`, confirmLabel: 'Delete', destructive: true,
    onConfirm: () => void write(`delete:${sub.id}`, `${sub.customName} deleted.`, async () => {
      try {
        await subscriptionsApi.remove(sub.id);
      } catch (e) {
        // Already gone is the outcome the user asked for.
        if (!isStatus(e, 404)) throw e;
      }
    }, after),
  });

  return { markPaid, remove, busy, canWrite: !offline && !busy, confirmDialog };
}

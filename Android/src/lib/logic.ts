/**
 * Pure rules ported from the MAUI client. No imports, so `node --test` can run them directly
 * (tests/logic.test.ts) - keep it that way.
 */

/** Matches the API enum. Order of this union is irrelevant; the picker uses CYCLES_ORDERED. */
export type BillingCycle = 'Weekly' | 'Monthly' | 'Yearly' | 'OneTime' | 'Quarterly';

/** Shortest cycle first, for reading. The API enum is ordered for storage, not for people. */
export const CYCLES_ORDERED: BillingCycle[] = ['Weekly', 'Monthly', 'Quarterly', 'Yearly', 'OneTime'];

export const CYCLE_LABEL: Record<BillingCycle, string> = {
  Weekly: 'Weekly',
  Monthly: 'Monthly',
  Quarterly: 'Quarterly',
  Yearly: 'Yearly',
  OneTime: 'One-time',
};

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// ---- Date-only values ------------------------------------------------------------------------
// The API sends DateOnly as "yyyy-MM-dd". They stay strings everywhere: a JS Date built from one
// is UTC midnight, which is yesterday evening west of Greenwich.

const pad = (n: number) => String(n).padStart(2, '0');

export const toDateOnly = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

export function parseDateOnly(value: string): Date {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export const todayDateOnly = (now = new Date()) => toDateOnly(now);

const dayNumber = (value: string) => {
  const [y, m, d] = value.split('-').map(Number);
  return Date.UTC(y, m - 1, d) / 86_400_000;
};

/** "15 Oct", or "15 Oct 2027" outside the current year - today is 15 Oct too, possibly. */
export const formatShortDate = (value: string, today = todayDateOnly()) => {
  const [y, m, d] = value.split('-').map(Number);
  return y === Number(today.slice(0, 4)) ? `${d} ${MONTHS[m - 1]}` : `${d} ${MONTHS[m - 1]} ${y}`;
};

export const formatLongDate = (value: string) => {
  const [y, m, d] = value.split('-').map(Number);
  return `${d} ${MONTHS[m - 1]} ${y}`;
};

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** "Sun 25 Oct", or "Sun 10 Jan 2027" outside the current year: for dates people plan around. */
export const formatDayDate = (value: string, today = todayDateOnly()) =>
  `${WEEKDAYS[parseDateOnly(value).getDay()]} ${formatShortDate(value, today)}`;

/** Whole days from today to `value`: negative in the past, 0 today. */
export const daysUntil = (value: string, today = todayDateOnly()) => dayNumber(value) - dayNumber(today);

/** The date a reminder `daysAhead` before `billingDate` lands on. */
export function reminderDate(billingDate: string, daysAhead: number): string {
  const date = parseDateOnly(billingDate);
  date.setDate(date.getDate() - daysAhead);
  return toDateOnly(date);
}

/** .NET AddMonths semantics: 31 Jan + 1 month is 28/29 Feb, not 3 Mar. */
function addMonths(value: string, months: number): string {
  const [y, m, d] = value.split('-').map(Number);
  const target = new Date(y, m - 1 + months, 1);
  const lastDay = new Date(target.getFullYear(), target.getMonth() + 1, 0).getDate();
  target.setDate(Math.min(d, lastDay));
  return toDateOnly(target);
}

/**
 * One billing period after the purchase date - the end of the period the purchase started, even if
 * that date has passed. A past date is how the app says a charge is outstanding; only mark-paid
 * moves it on.
 */
export function nextBillingDate(purchaseDate: string, cycle: BillingCycle): string {
  switch (cycle) {
    case 'Weekly': {
      const date = parseDateOnly(purchaseDate);
      date.setDate(date.getDate() + 7);
      return toDateOnly(date);
    }
    case 'Monthly': return addMonths(purchaseDate, 1);
    case 'Quarterly': return addMonths(purchaseDate, 3);
    case 'Yearly': return addMonths(purchaseDate, 12);
    case 'OneTime': return purchaseDate;
  }
}

/** Compared against the device's today, so a charge is never late while it is still today here. */
export const isOverdue = (sub: { isActive: boolean; nextBillingDate: string }, today = todayDateOnly()) =>
  sub.isActive && sub.nextBillingDate < today;

/** "in 3 days" answers what is about to be charged; past a fortnight a date is easier to place. */
export function describeRelative(date: string, today = todayDateOnly()): string {
  const days = dayNumber(date) - dayNumber(today);
  if (days < 0) return `overdue since ${formatShortDate(date, today)}`;
  if (days === 0) return 'today';
  if (days === 1) return 'tomorrow';
  if (days <= 14) return `in ${days} days`;
  return formatShortDate(date, today);
}

// ---- Money -----------------------------------------------------------------------------------

/**
 * Only symbols that identify their currency on sight. Keyed on the amount's own code, never the
 * device locale - a phone set to en-US must not print rupees with a dollar sign.
 */
const SYMBOLS: Record<string, string> = {
  INR: '₹', USD: '$', EUR: '€', GBP: '£', JPY: '¥', CNY: 'CN¥', KRW: '₩', RUB: '₽', TRY: '₺',
  THB: '฿', PHP: '₱', VND: '₫', ILS: '₪', NGN: '₦', BDT: '৳', UAH: '₴', KZT: '₸', BRL: 'R$',
  AUD: 'A$', CAD: 'CA$', NZD: 'NZ$', SGD: 'S$', HKD: 'HK$', MXN: 'MX$',
};

export const currencySymbol = (code?: string | null) => {
  const trimmed = (code ?? '').trim().toUpperCase();
  return SYMBOLS[trimmed] ?? trimmed;
};

/** 1234.5 -> "1,234.50". Deterministic, so it does not depend on the engine's Intl support. */
export function formatNumber(amount: number): string {
  const [whole, fraction] = Math.abs(amount).toFixed(2).split('.');
  return `${amount < 0 ? '-' : ''}${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}.${fraction}`;
}

/** "₹1,699.50", or "CHF 42.00" when the code has no unambiguous symbol. */
export function formatMoney(amount: number, code?: string | null): string {
  const symbol = currencySymbol(code);
  if (!symbol) return formatNumber(amount);
  return /^[A-Z]{3}$/.test(symbol) ? `${symbol} ${formatNumber(amount)}` : `${symbol}${formatNumber(amount)}`;
}

/**
 * "≈ ₹1,900" beside a foreign-currency price, from the server's read-time conversion (#292). Null
 * when the price is already in the home currency or the server had no rate - never converted here.
 */
export function homeEquivalent(sub: { currency: string; homeCurrency?: string | null; homeCurrencyAmount?: number | null }): string | null {
  if (!sub.homeCurrency || sub.homeCurrencyAmount == null) return null;
  return sub.homeCurrency.toUpperCase() === sub.currency.toUpperCase() ? null : `≈ ${formatMoney(sub.homeCurrencyAmount, sub.homeCurrency)}`;
}

/** First character, surrogate-safe, for the letter tile that stands in for a provider logo. */
export const initialOf = (name?: string | null) => [...(name ?? '').trimStart()][0]?.toUpperCase() ?? '?';

// ---- Dashboard -------------------------------------------------------------------------------

const STALE_RATE_AGE_DAYS = 2;

/** Why the headline number may not be the whole story. Null when the totals are clean. */
export function burnRateWarning(unresolvedCount: number, oldestRateFetchedAt: string | null | undefined, now = new Date()): string | null {
  const warnings: string[] = [];
  if (unresolvedCount > 0) {
    const subject = unresolvedCount === 1 ? '1 subscription is' : `${unresolvedCount} subscriptions are`;
    warnings.push(`${subject} not included — no exchange rate available yet.`);
  }
  if (oldestRateFetchedAt) {
    const ageDays = Math.floor((now.getTime() - new Date(oldestRateFetchedAt).getTime()) / 86_400_000);
    if (ageDays >= STALE_RATE_AGE_DAYS) warnings.push(`Converted using exchange rates from ${ageDays} days ago.`);
  }
  return warnings.length ? warnings.join(' ') : null;
}

/** Which account carries most of the spend. Null for 0 or 1 sources - "100% on your only card" is noise. */
export function topSpendSummary(items: { paymentSourceLabel: string; monthlyAmount: number }[]): string | null {
  if (items.length < 2) return null;
  const total = items.reduce((sum, item) => sum + item.monthlyAmount, 0);
  if (total <= 0) return null;
  const top = items.reduce((best, item) => (item.monthlyAmount > best.monthlyAmount ? item : best));
  return `${Math.round((top.monthlyAmount / total) * 100)}% of your monthly spend goes to ${top.paymentSourceLabel}.`;
}

/**
 * The biggest category's share of monthly spend, for the dashboard ring. Null when there is nothing
 * to divide (no categories, or nothing spent).
 */
export function topCategoryShare(items: { categoryName: string; monthlyAmount: number }[]): { name: string; share: number } | null {
  const total = items.reduce((sum, item) => sum + item.monthlyAmount, 0);
  if (items.length === 0 || total <= 0) return null;
  const top = items.reduce((best, item) => (item.monthlyAmount > best.monthlyAmount ? item : best));
  return { name: top.categoryName, share: top.monthlyAmount / total };
}

/** When the offline mirror was saved: "today, 14:05", "yesterday, 09:30" or "12 Oct, 18:00". */
export function formatSavedAt(iso: string, now = new Date()): string {
  const saved = new Date(iso);
  const time = `${pad(saved.getHours())}:${pad(saved.getMinutes())}`;
  const days = dayNumber(toDateOnly(now)) - dayNumber(toDateOnly(saved));
  const day = days === 0 ? 'today' : days === 1 ? 'yesterday' : formatShortDate(toDateOnly(saved), toDateOnly(now));
  return `${day}, ${time}`;
}

// ---- Subscription list -----------------------------------------------------------------------

export interface ListedSubscription {
  customName: string;
  categoryName?: string | null;
  nextBillingDate: string;
  isActive: boolean;
}

export const UNCATEGORISED = 'Uncategorised';

/** Shown under every new-password field. The rule itself is the server's (PasswordRules.NewPassword); this only describes it. */
export const PASSWORD_HINT = 'At least 8 characters, with an uppercase and a lowercase letter, a number and a symbol.'; // pragma: allowlist secret

const byName = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'base' });

/**
 * One section per category; rows and sections both ordered by whichever bills soonest, so the next
 * charge is at the top. Money owed outranks money due in the summary.
 */
export function groupSubscriptions<T extends ListedSubscription>(subs: T[], today = todayDateOnly()) {
  const groups = new Map<string, T[]>();
  for (const sub of subs) {
    const key = sub.categoryName?.trim() ? sub.categoryName : UNCATEGORISED;
    groups.set(key, [...(groups.get(key) ?? []), sub]);
  }
  return [...groups.entries()]
    .map(([title, rows]) => {
      const data = [...rows].sort((a, b) => a.nextBillingDate.localeCompare(b.nextBillingDate) || byName(a.customName, b.customName));
      const overdue = data.filter((s) => isOverdue(s, today)).length;
      const count = `${data.length} ${data.length === 1 ? 'subscription' : 'subscriptions'}`;
      return {
        title,
        next: data[0].nextBillingDate,
        summary: overdue > 0 ? `${count} · ${overdue} overdue` : `${count} · next ${describeRelative(data[0].nextBillingDate, today)}`,
        data,
      };
    })
    .sort((a, b) => a.next.localeCompare(b.next) || byName(a.title, b.title));
}

/** How far ahead "due soon" looks, on Home and in the list's middle section. */
export const DUE_SOON_DAYS = 7;

/**
 * What is about to charge: overdue first (oldest first), then active subscriptions due within the
 * next `windowDays`, soonest first. Read-only - nothing here moves a date.
 */
export function dueSoon<T extends ListedSubscription>(subs: T[], today = todayDateOnly(), windowDays = DUE_SOON_DAYS) {
  const byDate = (a: T, b: T) => a.nextBillingDate.localeCompare(b.nextBillingDate) || byName(a.customName, b.customName);
  const active = subs.filter((s) => s.isActive);
  return {
    overdue: active.filter((s) => isOverdue(s, today)).sort(byDate),
    upcoming: active.filter((s) => { const d = daysUntil(s.nextBillingDate, today); return d >= 0 && d <= windowDays; }).sort(byDate),
  };
}

/** The list's default sections: Overdue, Next 7 days, Later, then anything inactive. Empty ones are left out. */
export function sectionByDue<T extends ListedSubscription>(subs: T[], today = todayDateOnly()) {
  const { overdue, upcoming } = dueSoon(subs, today);
  const placed = new Set<T>([...overdue, ...upcoming]);
  const byDate = (a: T, b: T) => a.nextBillingDate.localeCompare(b.nextBillingDate) || byName(a.customName, b.customName);
  const later = subs.filter((s) => s.isActive && !placed.has(s)).sort(byDate);
  const inactive = subs.filter((s) => !s.isActive).sort((a, b) => byName(a.customName, b.customName));
  return [
    { title: 'Overdue', data: overdue },
    { title: `Next ${DUE_SOON_DAYS} days`, data: upcoming },
    { title: 'Later', data: later },
    { title: 'Inactive', data: inactive },
  ].filter((s) => s.data.length > 0);
}

/**
 * Most expensive first, by the server's monthly figure in the home currency. Rows it could not
 * convert, and one-time purchases (no monthly figure), go last by name.
 */
export function sortByCost<T extends ListedSubscription & { homeCurrencyMonthly?: number | null }>(subs: T[]) {
  return [...subs].sort((a, b) => {
    const [x, y] = [a.homeCurrencyMonthly ?? null, b.homeCurrencyMonthly ?? null];
    if (x !== null && y !== null && x !== y) return y - x;
    if ((x === null) !== (y === null)) return x === null ? 1 : -1;
    return byName(a.customName, b.customName);
  });
}

export interface SubscriptionFilter { query?: string; categoryId?: string | null; paymentSourceId?: string | null }

/** Name search (case- and accent-blind) plus an optional category or payment-source filter. */
export function filterSubscriptions<T extends ListedSubscription & { categoryId?: string | null; paymentSourceId?: string | null }>(subs: T[], filter: SubscriptionFilter) {
  const fold = (v: string) => v.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
  const q = fold(filter.query?.trim() ?? '');
  return subs.filter((s) =>
    (!q || fold(s.customName).includes(q))
    && (!filter.categoryId || s.categoryId === filter.categoryId)
    && (!filter.paymentSourceId || s.paymentSourceId === filter.paymentSourceId));
}

// ---- Renewal reminders -----------------------------------------------------------------------

/** iOS keeps at most 64 pending local notifications; enforced here so the nearest are the ones kept. */
export const MAX_PENDING_REMINDERS = 64;
const NOTIFY_HOUR = 9;

export interface RemindableSubscription {
  customName: string;
  costAmount: number;
  currency: string;
  cycleCadence: BillingCycle;
  nextBillingDate: string;
  alertDaysAdvance: number;
  isActive: boolean;
}

/** Derived on the device and handed to the OS - there is no push service behind reminders. */
export function planReminders(subs: RemindableSubscription[], now: Date) {
  return subs
    .filter((sub) => sub.isActive)
    .map((sub) => {
      const at = parseDateOnly(sub.nextBillingDate);
      at.setDate(at.getDate() - sub.alertDaysAdvance);
      at.setHours(NOTIFY_HOUR, 0, 0, 0);
      return { sub, at };
    })
    // A reminder in the past would fire immediately or be rejected; both are wrong.
    .filter(({ at }) => at > now)
    .sort((a, b) => a.at.getTime() - b.at.getTime() || a.sub.customName.toLowerCase().localeCompare(b.sub.customName.toLowerCase()))
    .slice(0, MAX_PENDING_REMINDERS)
    .map(({ sub, at }) => {
      const oneTime = sub.cycleCadence === 'OneTime';
      const action = oneTime ? 'is due' : 'renews';
      const days = sub.alertDaysAdvance;
      const when = days <= 0 ? `${action} today` : days === 1 ? `${action} tomorrow` : `${action} in ${days} days`;
      return {
        title: oneTime ? 'Payment due soon' : 'Subscription renewing soon',
        body: `${sub.customName} ${when} - ${formatMoney(sub.costAmount, sub.currency)}`,
        notifyAt: at,
      };
    });
}

/**
 * Whether a failed `/auth/refresh` means the session is over. Only the server rejecting the token
 * (400/401/403) is a verdict. A 429 (rate limit) or 5xx (a cold start, the database waking) says
 * nothing about the token: signing out on those wiped the session and the offline mirror over a
 * transient error. Those leave the tokens alone so the next request can try again.
 */
export const refreshEndsSession = (status: number) => status === 400 || status === 401 || status === 403;


// ---- Sign-in cooldown ------------------------------------------------------------------------

/** Wrong passwords allowed for one address within the window before Sign in pauses. */
export const SIGN_IN_MAX_FAILURES = 3;
export const SIGN_IN_WINDOW_MS = 15 * 60_000;
export const SIGN_IN_COOLDOWN_MS = 15 * 60_000;

/**
 * Local to the device and the same for every address, existing or not, so it reveals nothing the
 * server's single 401 does not; the server keeps its own, later lockout. `failures` are the times
 * (ms) of rejected sign-ins for one address. Locked from the 3rd failure inside 15 minutes until
 * 15 minutes after it.
 */
export function signInCooldown(failures: number[], now: number): { locked: boolean; remainingMs: number; recent: number } {
  const recent = failures.filter((t) => t <= now && now - t < SIGN_IN_WINDOW_MS + SIGN_IN_COOLDOWN_MS).sort((a, b) => a - b);
  // The lock starts at the failure that makes three inside one window.
  for (let i = SIGN_IN_MAX_FAILURES - 1; i < recent.length; i++) {
    if (recent[i] - recent[i - SIGN_IN_MAX_FAILURES + 1] < SIGN_IN_WINDOW_MS) {
      const until = recent[i] + SIGN_IN_COOLDOWN_MS;
      if (until > now) return { locked: true, remainingMs: until - now, recent: recent.length };
    }
  }
  return { locked: false, remainingMs: 0, recent: recent.filter((t) => now - t < SIGN_IN_WINDOW_MS).length };
}

/** "14:59" for the Sign in countdown. */
export const formatCountdown = (ms: number) => {
  const total = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(total / 60)}:${pad(total % 60)}`;
};

// ---- Category ring ---------------------------------------------------------------------------

/** Each category's share of monthly spend, largest first; empty when nothing is spent. */
export function categoryShares<T extends { categoryName: string; monthlyAmount: number }>(items: T[]): (T & { share: number })[] {
  const total = items.reduce((sum, item) => sum + Math.max(0, item.monthlyAmount), 0);
  if (total <= 0) return [];
  return items
    .filter((item) => item.monthlyAmount > 0)
    .map((item) => ({ ...item, share: item.monthlyAmount / total }))
    .sort((a, b) => b.share - a.share);
}

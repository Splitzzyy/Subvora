import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  burnRateWarning, describeRelative, formatMoney, groupSubscriptions, initialOf, isOverdue,
  nextBillingDate, planReminders, refreshEndsSession, topSpendSummary,
} from '../src/lib/logic.ts';

test('next billing date steps one period and clamps month ends like .NET AddMonths', () => {
  assert.equal(nextBillingDate('2025-01-31', 'Monthly'), '2025-02-28');
  assert.equal(nextBillingDate('2024-01-31', 'Monthly'), '2024-02-29');
  assert.equal(nextBillingDate('2025-11-30', 'Quarterly'), '2026-02-28');
  assert.equal(nextBillingDate('2024-02-29', 'Yearly'), '2025-02-28');
  assert.equal(nextBillingDate('2025-12-29', 'Weekly'), '2026-01-05');
  assert.equal(nextBillingDate('2025-04-23', 'OneTime'), '2025-04-23');
});

test('relative dates', () => {
  const today = '2026-10-15';
  assert.equal(describeRelative('2026-10-14', today), 'overdue since 14 Oct');
  assert.equal(describeRelative('2026-10-15', today), 'today');
  assert.equal(describeRelative('2026-10-16', today), 'tomorrow');
  assert.equal(describeRelative('2026-10-29', today), 'in 14 days');
  assert.equal(describeRelative('2026-10-30', today), '30 Oct');
  assert.equal(describeRelative('2027-10-15', today), '15 Oct 2027');
  assert.equal(describeRelative('2025-12-01', today), 'overdue since 1 Dec 2025');
});

test('overdue only for active subscriptions with a past date', () => {
  assert.equal(isOverdue({ isActive: true, nextBillingDate: '2026-10-14' }, '2026-10-15'), true);
  assert.equal(isOverdue({ isActive: true, nextBillingDate: '2026-10-15' }, '2026-10-15'), false);
  assert.equal(isOverdue({ isActive: false, nextBillingDate: '2026-10-14' }, '2026-10-15'), false);
});

test('money keys on the amount code, falls back to the code itself', () => {
  assert.equal(formatMoney(1699.5, 'INR'), '₹1,699.50');
  assert.equal(formatMoney(1234567.891, 'usd'), '$1,234,567.89');
  assert.equal(formatMoney(42, 'CHF'), 'CHF 42.00');
  assert.equal(formatMoney(-5, 'EUR'), '€-5.00');
  assert.equal(initialOf('  netflix'), 'N');
  assert.equal(initialOf('😀 Fun'), '😀');
  assert.equal(initialOf(''), '?');
});

test('dashboard summaries', () => {
  assert.equal(topSpendSummary([{ paymentSourceLabel: 'Only', monthlyAmount: 10 }]), null);
  assert.equal(
    topSpendSummary([{ paymentSourceLabel: 'HDFC', monthlyAmount: 62 }, { paymentSourceLabel: 'Amex', monthlyAmount: 38 }]),
    '62% of your monthly spend goes to HDFC.',
  );
  const now = new Date('2026-10-15T12:00:00Z');
  assert.equal(burnRateWarning(0, '2026-10-15T00:00:00Z', now), null);
  assert.equal(
    burnRateWarning(2, '2026-10-12T00:00:00Z', now),
    '2 subscriptions are not included — no exchange rate available yet. Converted using exchange rates from 3 days ago.',
  );
});

test('groups order by soonest billing and say when something is overdue', () => {
  const sub = (customName: string, categoryName: string | null, nextBillingDate: string) =>
    ({ customName, categoryName, nextBillingDate, isActive: true });
  const groups = groupSubscriptions([
    sub('Spotify', 'Entertainment', '2026-10-20'),
    sub('Gym', 'Fitness', '2026-10-16'),
    sub('Netflix', 'Entertainment', '2026-10-10'),
    sub('Misc', null, '2026-11-01'),
  ], '2026-10-15');
  assert.deepEqual(groups.map((g) => g.title), ['Entertainment', 'Fitness', 'Uncategorised']);
  assert.deepEqual(groups[0].data.map((s) => s.customName), ['Netflix', 'Spotify']);
  assert.equal(groups[0].summary, '2 subscriptions · 1 overdue');
  assert.equal(groups[1].summary, '1 subscription · next tomorrow');
});

test('groups put inactive rows last and never call their stale date "next"', () => {
  const sub = (customName: string, categoryName: string, nextBillingDate: string, isActive = true) =>
    ({ customName, categoryName, nextBillingDate, isActive });
  const groups = groupSubscriptions([
    sub('Hotstar', 'Entertainment', '2026-09-30', false),
    sub('Spotify', 'Entertainment', '2026-10-20'),
    sub('Old gym', 'Fitness', '2026-08-01', false),
  ], '2026-10-15');
  assert.deepEqual(groups.map((g) => g.title), ['Entertainment', 'Fitness']);
  assert.deepEqual(groups[0].data.map((s) => s.customName), ['Spotify', 'Hotstar']);
  assert.equal(groups[0].summary, '2 subscriptions · next in 5 days');
  assert.equal(groups[1].summary, '1 subscription · inactive');
});

test('reminders skip the past and inactive, keep the nearest 64, fire at 09:00', () => {
  const base = { costAmount: 199, currency: 'INR', cycleCadence: 'Monthly' as const, alertDaysAdvance: 3, isActive: true };
  const now = new Date(2026, 9, 15, 10, 0);
  const planned = planReminders([
    { ...base, customName: 'Past', nextBillingDate: '2026-10-17' },
    { ...base, customName: 'Off', nextBillingDate: '2026-12-01', isActive: false },
    { ...base, customName: 'Netflix', nextBillingDate: '2026-10-20' },
    { ...base, customName: 'Once', nextBillingDate: '2026-10-20', cycleCadence: 'OneTime', alertDaysAdvance: 1 },
  ], now);
  assert.deepEqual(planned.map((p) => p.body), ['Netflix renews in 3 days - ₹199.00', 'Once is due tomorrow - ₹199.00']);
  assert.equal(planned[1].title, 'Payment due soon');
  assert.deepEqual([planned[0].notifyAt.getDate(), planned[0].notifyAt.getHours()], [17, 9]);

  const many = Array.from({ length: 70 }, (_, i) => ({ ...base, customName: `S${i}`, nextBillingDate: `2027-0${1 + (i % 9)}-10` }));
  assert.equal(planReminders(many, now).length, 64);
});

test('only a rejected refresh token ends the session', () => {
  for (const status of [400, 401, 403]) assert.equal(refreshEndsSession(status), true, `${status}`);
  // Rate-limited or a server waking from a cold start: a transient error, not a verdict.
  for (const status of [429, 500, 502, 503, 504]) assert.equal(refreshEndsSession(status), false, `${status}`);
});

// ---- Android UX review (#275, #278, #284, #290, #292) ----------------------------------------

const { categoryShares, dueSoon, filterSubscriptions, formatCountdown, formatDayDate, homeEquivalent, reminderDate, sectionByDue, signInCooldown, sortByCost } =
  await import('../src/lib/logic.ts');

const row = (customName: string, nextBillingDate: string, extra: Record<string, unknown> = {}) =>
  ({ customName, nextBillingDate, isActive: true, categoryName: null, ...extra });

test('due soon: overdue first, then the next 7 days, active only', () => {
  const today = '2026-10-22';
  const subs = [
    row('Later', '2026-11-08'), row('Netflix', '2026-10-25'), row('iCloud', '2026-10-18'),
    row('ChatGPT', '2026-10-28'), row('Paused', '2026-10-20', { isActive: false }), row('Edge', '2026-10-29'),
  ];
  const { overdue, upcoming } = dueSoon(subs, today);
  assert.deepEqual(overdue.map((s) => s.customName), ['iCloud']);
  assert.deepEqual(upcoming.map((s) => s.customName), ['Netflix', 'ChatGPT', 'Edge']);
  assert.deepEqual(sectionByDue(subs, today).map((s) => [s.title, s.data.length]), [['Overdue', 1], ['Next 7 days', 3], ['Later', 1], ['Inactive', 1]]);
});

test('cost sort puts unconverted and one-time rows last', () => {
  const sorted = sortByCost([
    row('B', '2026-11-01', { homeCurrencyMonthly: 100 }), row('None', '2026-11-01'), row('A', '2026-11-01', { homeCurrencyMonthly: 1900 }),
  ]);
  assert.deepEqual(sorted.map((s) => s.customName), ['A', 'B', 'None']);
  const paused = sortByCost([row('Off', '2026-11-01', { homeCurrencyMonthly: 5000, isActive: false }), row('On', '2026-11-01', { homeCurrencyMonthly: 10 })]);
  assert.deepEqual(paused.map((s) => s.customName), ['On', 'Off']);
});

test('search folds case and accents; filters narrow by category or source', () => {
  const subs = [row('Café Coffee', '2026-11-01', { categoryId: 'c1' }), row('Netflix', '2026-11-01', { categoryId: 'c2', paymentSourceId: 'p1' })];
  assert.deepEqual(filterSubscriptions(subs, { query: 'cafe' }).map((s) => s.customName), ['Café Coffee']);
  assert.deepEqual(filterSubscriptions(subs, { categoryId: 'c2' }).map((s) => s.customName), ['Netflix']);
  assert.deepEqual(filterSubscriptions(subs, { paymentSourceId: 'p1', query: 'NET' }).map((s) => s.customName), ['Netflix']);
});

test('reminder dates and weekday dates', () => {
  assert.equal(reminderDate('2026-10-28', 3), '2026-10-25');
  assert.equal(reminderDate('2026-03-02', 7), '2026-02-23');
  assert.equal(formatDayDate('2026-10-25', '2026-10-22'), 'Sun 25 Oct');
  assert.equal(formatDayDate('2027-01-10', '2026-10-22'), 'Sun 10 Jan 2027');
});

test('the home-currency line appears only for a converted foreign price', () => {
  assert.equal(homeEquivalent({ currency: 'USD', homeCurrency: 'INR', homeCurrencyAmount: 1900 }), '≈ ₹1,900.00');
  assert.equal(homeEquivalent({ currency: 'INR', homeCurrency: 'INR', homeCurrencyAmount: 649 }), null);
  assert.equal(homeEquivalent({ currency: 'CHF', homeCurrency: 'INR', homeCurrencyAmount: null }), null);
  assert.equal(homeEquivalent({ currency: 'USD' }), null);
});

test('sign-in cooldown: three failures inside 15 minutes pause sign-in for 15 minutes', () => {
  const min = 60_000;
  const t0 = 1_000_000_000_000;
  assert.equal(signInCooldown([t0, t0 + min], t0 + 2 * min).locked, false);
  const locked = signInCooldown([t0, t0 + min, t0 + 2 * min], t0 + 3 * min);
  assert.equal(locked.locked, true);
  assert.equal(locked.remainingMs, 14 * min);
  // Over by itself once the 15 minutes from the third failure have passed.
  assert.equal(signInCooldown([t0, t0 + min, t0 + 2 * min], t0 + 17 * min).locked, false);
  // Three failures spread over more than 15 minutes never lock.
  assert.equal(signInCooldown([t0, t0 + 10 * min, t0 + 20 * min], t0 + 21 * min).locked, false);
  assert.equal(formatCountdown(14 * min + 59_000), '14:59');
  assert.equal(formatCountdown(0), '0:00');
});

test('category shares: largest first, nothing when nothing is spent', () => {
  const shares = categoryShares([{ categoryName: 'Fitness', monthlyAmount: 25 }, { categoryName: 'Productivity', monthlyAmount: 75 }]);
  assert.deepEqual(shares.map((s) => [s.categoryName, s.share]), [['Productivity', 0.75], ['Fitness', 0.25]]);
  assert.deepEqual(categoryShares([{ categoryName: 'X', monthlyAmount: 0 }]), []);
});

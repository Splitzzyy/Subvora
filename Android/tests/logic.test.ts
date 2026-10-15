import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  burnRateWarning, describeRelative, formatMoney, groupSubscriptions, initialOf, isOverdue,
  nextBillingDate, planReminders, topSpendSummary,
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

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { asyncState, errorDetail, LOADER_DELAY_MS, LOADER_MIN_MS, loaderVisibility } from '../src/lib/loading.ts';
import { formatSavedAt, topCategoryShare } from '../src/lib/logic.ts';

test('a quick load never shows the loader', () => {
  assert.deepEqual(loaderVisibility(true, 1000, null, 1000), { visible: false, recheckIn: LOADER_DELAY_MS });
  assert.deepEqual(loaderVisibility(true, 1000, null, 1100), { visible: false, recheckIn: 50 });
  // Finished before the delay ran out: nothing was shown, nothing to keep.
  assert.deepEqual(loaderVisibility(false, null, null, 1120), { visible: false, recheckIn: null });
});

test('a slow load shows the loader after the delay', () => {
  assert.deepEqual(loaderVisibility(true, 1000, null, 1000 + LOADER_DELAY_MS), { visible: true, recheckIn: null });
  assert.deepEqual(loaderVisibility(true, 1000, 1150, 5000), { visible: true, recheckIn: null });
});

test('once shown, the loader stays for the minimum even if the load ends at once', () => {
  assert.deepEqual(loaderVisibility(false, null, 1150, 1200), { visible: true, recheckIn: LOADER_MIN_MS - 50 });
  assert.deepEqual(loaderVisibility(false, null, 1150, 1150 + LOADER_MIN_MS), { visible: false, recheckIn: null });
  assert.deepEqual(loaderVisibility(false, null, 1150, 9000), { visible: false, recheckIn: null });
});

test('screen state: content wins, then offline vs error, else loading', () => {
  assert.equal(asyncState({ hasData: false }), 'loading');
  assert.equal(asyncState({ hasData: true }), 'content');
  assert.equal(asyncState({ hasData: true, empty: true }), 'empty');
  // A failed reload with rows on screen keeps them (a banner says why).
  assert.equal(asyncState({ hasData: true, error: 'x', errorStatus: 0 }), 'content');
  assert.equal(asyncState({ hasData: false, error: 'x', errorStatus: 0 }), 'offline');
  assert.equal(asyncState({ hasData: false, error: 'x', errorStatus: 503 }), 'error');
});

test('error detail is plain language with no raw body', () => {
  assert.match(errorDetail(0), /offline/);
  assert.match(errorDetail(503), /code 503/);
  assert.match(errorDetail(404), /turned the request down \(code 404\)/);
});

test('top category share of monthly spend', () => {
  assert.equal(topCategoryShare([]), null);
  assert.equal(topCategoryShare([{ categoryName: 'Food', monthlyAmount: 0 }]), null);
  assert.deepEqual(topCategoryShare([
    { categoryName: 'Food', monthlyAmount: 25 },
    { categoryName: 'Entertainment', monthlyAmount: 75 },
  ]), { name: 'Entertainment', share: 0.75 });
});

test('mirror timestamp reads relative to today', () => {
  const now = new Date(2026, 9, 16, 18, 0);
  assert.equal(formatSavedAt(new Date(2026, 9, 16, 14, 5).toISOString(), now), 'today, 14:05');
  assert.equal(formatSavedAt(new Date(2026, 9, 15, 9, 30).toISOString(), now), 'yesterday, 09:30');
  assert.equal(formatSavedAt(new Date(2026, 9, 12, 18, 0).toISOString(), now), '12 Oct, 18:00');
  assert.equal(formatSavedAt(new Date(2025, 11, 31, 23, 59).toISOString(), now), '31 Dec 2025, 23:59');
});

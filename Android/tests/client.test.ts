import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { beforeEach, mock, test } from 'node:test';

// client.ts imports through the '@/' alias and keeps tokens in expo-secure-store; map the first
// onto src/ and stand an in-memory store in for the second.
const src = new URL('../src/', import.meta.url);
registerHooks({
  resolve: (specifier, context, next) =>
    next(specifier.startsWith('@/') ? new URL(`${specifier.slice(2)}.ts`, src).href : specifier, context),
});
const store = new Map<string, string>();
mock.module('expo-secure-store', {
  exports: {
    getItemAsync: async (key: string) => store.get(key) ?? null,
    setItemAsync: async (key: string, value: string) => { store.set(key, value); },
    deleteItemAsync: async (key: string) => { store.delete(key); },
  },
});
const { api, ApiError, errorMessage, fieldErrorMessage, setSessionExpiredHandler, tokens } = await import('../src/api/client.ts');

type Reply = number | 'unreachable';
let refreshReplies: Reply[] = [];
let refreshCalls = 0;
let expired = 0;

const pair = (n: number) => ({ accessToken: `access-${n}`, refreshToken: `refresh-${n}`, accessTokenExpiresAt: '', refreshTokenExpiresAt: '' });

// /auth/refresh answers from refreshReplies in turn (200 mints a pair); everything else accepts
// only the newest access token, so the first call always 401s into a refresh.
globalThis.fetch = (async (url: string, init: RequestInit) => {
  const reply = (status: number, body?: unknown) =>
    ({ status, ok: status >= 200 && status < 300, text: async () => (body === undefined ? '' : JSON.stringify(body)) });
  if (url.endsWith('/auth/refresh')) {
    refreshCalls++;
    const next = refreshReplies.shift() ?? 'unreachable';
    if (next === 'unreachable') throw new TypeError('Network request failed');
    return reply(next, next === 200 ? pair(refreshCalls) : undefined);
  }
  const auth = (init.headers as Record<string, string>).Authorization;
  return auth === `Bearer access-${refreshCalls}` && refreshCalls > 0 ? reply(200, { ok: true }) : reply(401);
}) as typeof fetch;

beforeEach(async () => {
  refreshCalls = 0;
  expired = 0;
  setSessionExpiredHandler(() => { expired++; });
  await tokens.save(pair(-1));
});

test('a refresh answered with 503 fails the request with 503 and keeps the session', async () => {
  refreshReplies = [503];
  await assert.rejects(api('GET', '/api/v1/subscriptions'), (e) => e instanceof ApiError && e.status === 503);
  assert.equal(expired, 0);
  assert.equal(store.get('auth_refresh_token'), 'refresh--1');
});

test('a refresh answered with 429 fails the request with 429, not "session expired"', async () => {
  refreshReplies = [429];
  await assert.rejects(api('GET', '/api/v1/subscriptions'), (e) => e instanceof ApiError && e.status === 429);
  assert.equal(expired, 0);
});

test('a refresh whose answer was lost is asked again at once, inside the server grace', async () => {
  refreshReplies = ['unreachable', 200];
  assert.deepEqual(await api('GET', '/api/v1/subscriptions'), { ok: true });
  assert.equal(refreshCalls, 2);
  assert.equal(store.get('auth_refresh_token'), 'refresh-2');
});

test('a refresh unreachable twice reports offline and keeps the session', async () => {
  refreshReplies = ['unreachable', 'unreachable'];
  await assert.rejects(api('GET', '/api/v1/subscriptions'), (e) => e instanceof ApiError && e.status === 0);
  assert.equal(expired, 0);
});

test('a refresh the server rejects ends the session', async () => {
  refreshReplies = [401];
  await assert.rejects(api('GET', '/api/v1/subscriptions'), (e) => e instanceof ApiError && e.status === 401);
  assert.equal(expired, 1);
  assert.equal(store.get('auth_refresh_token'), undefined);
});

test('a 400 naming a field is told apart from one refusing the whole request', () => {
  const tooShort = new ApiError(400, { title: 'One or more validation errors occurred.', errors: { NewPassword: ['Password must be at least 8 characters.'] } });
  const badCode = new ApiError(400, { title: 'Invalid or expired reset code.' });
  assert.equal(fieldErrorMessage(tooShort), 'Password must be at least 8 characters.');
  assert.equal(fieldErrorMessage(badCode), null);
  assert.equal(fieldErrorMessage(new ApiError(0)), null);
  // The general message still falls back to the title.
  assert.equal(errorMessage(badCode), 'Invalid or expired reset code.');
});

test('no answer reads as offline only when the phone is offline', async () => {
  const { setDeviceOffline, writeErrorMessage } = await import('../src/api/client.ts');
  setDeviceOffline(false);
  assert.match(writeErrorMessage(new ApiError(0)), /^Couldn't reach SubVora, so this change wasn't saved/);
  assert.match(errorMessage(new ApiError(0)), /^Couldn't reach SubVora/);
  setDeviceOffline(true);
  assert.match(writeErrorMessage(new ApiError(0)), /^You're offline — this change wasn't saved/);
  assert.equal(errorMessage(new ApiError(0)), 'You appear to be offline.');
  setDeviceOffline(false);
});

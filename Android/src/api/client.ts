import * as SecureStore from 'expo-secure-store';
import { refreshEndsSession } from '@/lib/logic';
import type { AuthTokenResponse } from './types';

/**
 * Backend address, baked in at bundle time; defaults to the deployed API. Point a dev
 * build at a local API with EXPO_PUBLIC_API_URL=http://localhost:8080 plus `adb reverse tcp:8080
 * tcp:8080` (Android/README.md).
 */
export const API_BASE_URL = (process.env.EXPO_PUBLIC_API_URL ?? 'https://subvora-api.onrender.com').replace(/\/+$/, '');

/** A sleeping free-tier instance swallows connections; 30s beats fetch's open-ended wait. */
const REQUEST_TIMEOUT_MS = 30_000;
/**
 * Longer, though its wait stacks on the 401'd request's: a refresh is the first query after Neon
 * suspends, and a timed-out one has usually rotated the token anyway. Both attempts timing out
 * leaves this device holding the old token, which the server reads as theft once its 2-minute
 * reuse grace has passed - the next refresh signs the user out. Two 60s attempts stay inside it.
 */
const REFRESH_TIMEOUT_MS = 60_000;

const ACCESS_KEY = 'auth_access_token';
const REFRESH_KEY = 'auth_refresh_token';

/** status 0 means no response arrived at all - offline, unreachable or timed out. */
export class ApiError extends Error {
  constructor(public status: number, public body?: unknown) {
    super(status === 0 ? 'Network request failed' : `HTTP ${status}`);
  }
}

// ---- Tokens ----------------------------------------------------------------------------------

let accessToken: string | null = null;
let onSessionExpired = () => {};

export const tokens = {
  async load() {
    accessToken = await SecureStore.getItemAsync(ACCESS_KEY);
    return accessToken;
  },
  refreshToken: () => SecureStore.getItemAsync(REFRESH_KEY),
  async save(t: AuthTokenResponse) {
    accessToken = t.accessToken;
    await SecureStore.setItemAsync(ACCESS_KEY, t.accessToken);
    await SecureStore.setItemAsync(REFRESH_KEY, t.refreshToken);
  },
  async clear() {
    accessToken = null;
    await SecureStore.deleteItemAsync(ACCESS_KEY);
    await SecureStore.deleteItemAsync(REFRESH_KEY);
  },
};

/** Called once the server has refused the session; the tokens are already gone by then. */
export const setSessionExpiredHandler = (handler: () => void) => { onSessionExpired = handler; };

// ---- Transport -------------------------------------------------------------------------------

async function send(method: string, path: string, body: unknown, token: string | null, timeoutMs: number) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers: {
        Accept: 'application/json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });
    return { status: response.status, ok: response.ok, text: await response.text() };
  } catch {
    throw new ApiError(0);
  } finally {
    clearTimeout(timer);
  }
}

const parse = (text: string) => {
  try { return text ? JSON.parse(text) : undefined; } catch { return undefined; }
};

/**
 * One request. Authenticated calls attach the bearer token and retry once after a refresh on 401;
 * a refresh the server could not answer fails the request with its own status instead.
 * Pass auth:false for login/register/refresh/reset - a 401 there is a verdict, not an expiry.
 */
export async function api<T = void>(method: string, path: string, body?: unknown, { auth = true } = {}): Promise<T> {
  const token = auth ? accessToken : null;
  let response = await send(method, path, body, token, REQUEST_TIMEOUT_MS);

  if (auth && response.status === 401) {
    const fresh = await refreshOnce(token);
    if (fresh) response = await send(method, path, body, fresh, REQUEST_TIMEOUT_MS);
  }

  const data = parse(response.text);
  if (!response.ok) throw new ApiError(response.status, data);
  return data as T;
}

// ---- Single-flight refresh -------------------------------------------------------------------
// The server rotates refresh tokens and treats a replayed one as theft (revoking the chain) once
// its short grace for lost responses has passed, so two requests refreshing off the same token
// could sign the user out. One refreshes; the rest wait.

let inflight: Promise<string | null> | null = null;

function refreshOnce(staleToken: string | null): Promise<string | null> {
  // Someone else already handled this expiry: retry with their token, or give up if they ended it.
  if (accessToken !== staleToken) return Promise.resolve(accessToken);
  return (inflight ??= refresh().finally(() => { inflight = null; }));
}

async function refresh(): Promise<string | null> {
  const refreshToken = await tokens.refreshToken();
  if (!refreshToken) return expire();

  const attempt = () => send('POST', '/api/v1/auth/refresh', { refreshToken }, null, REFRESH_TIMEOUT_MS);
  let response;
  try {
    response = await attempt();
  } catch {
    // A refresh that timed out has usually still rotated the token server-side; only the answer
    // was lost. Asked again now, the server is inside its short grace for exactly this and hands
    // out a fresh pair. Left for the next request - maybe minutes later - the same token reads as
    // stolen and signs the user out of every device.
    response = await attempt();
  }
  // Still unreachable (the ApiError(0) above), or a 429/5xx: not a verdict on the session. Signing
  // out would wipe the offline mirror precisely when the server is the problem, so the tokens stay,
  // and the request fails with the refresh's own status rather than a 401 that says "log in again".
  if (!response.ok) {
    if (refreshEndsSession(response.status)) return expire();
    throw new ApiError(response.status, parse(response.text));
  }

  const refreshed = parse(response.text) as AuthTokenResponse | undefined;
  if (!refreshed?.accessToken) return expire();

  await tokens.save(refreshed);
  return refreshed.accessToken;
}

async function expire() {
  await tokens.clear();
  onSessionExpired();
  return null;
}

// ---- Messages --------------------------------------------------------------------------------

function firstFieldMessage(body: unknown): string | null {
  const { errors } = (body ?? {}) as { errors?: Record<string, unknown> };
  for (const messages of Object.values(errors ?? {})) {
    if (Array.isArray(messages)) {
      const text = messages.find((m) => typeof m === 'string' && m.trim());
      if (text) return text;
    }
  }
  return null;
}

/**
 * First field-level message from a ValidationProblemDetails body. With no field errors, the title:
 * change-password reports a wrong current password that way.
 */
function firstValidationMessage(body: unknown): string | null {
  const { title } = (body ?? {}) as { title?: unknown };
  return firstFieldMessage(body) ?? (typeof title === 'string' && title.trim() ? title : null);
}

/**
 * The message of a 400 that names a field (a new password too short), else
 * null - so a screen can tell that apart from a 400 refusing the request as a whole.
 */
export const fieldErrorMessage = (error: unknown) =>
  error instanceof ApiError && error.status === 400 ? firstFieldMessage(error.body) : null;

/**
 * What to tell the user about a failed read. Anything that is not an ApiError is a defect in this
 * app and is rethrown rather than dressed up as a network problem.
 */
export function errorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) throw error;
  switch (error.status) {
    case 0: return 'You appear to be offline.';
    case 400: return firstValidationMessage(error.body) ?? 'Please check your input and try again.';
    case 401: return 'Session expired, please log in again.';
    case 404: return 'Not found.';
    case 429: return "You're doing that too fast, try again shortly.";
    default: return 'Something went wrong. Please try again.';
  }
}

/** Same, for a write. Nothing is queued for later, so an offline failure must say the change was lost. */
export function writeErrorMessage(error: unknown): string {
  return error instanceof ApiError && error.status === 0
    ? "You're offline — this change wasn't saved. Try again once you're connected."
    : errorMessage(error);
}

export const isStatus = (error: unknown, status: number) => error instanceof ApiError && error.status === status;

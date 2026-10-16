/**
 * Pure rules for loading states. No imports, so `node --test` runs them directly
 * (tests/loading.test.ts) - keep it that way.
 */

/** A load that finishes sooner than this never shows a loader at all: no flash. */
export const LOADER_DELAY_MS = 150;
/** Once shown, a loader stays at least this long, so it never blinks. */
export const LOADER_MIN_MS = 300;

/**
 * Whether the loader is visible at `now`, and in how many ms the answer could next change (null:
 * not until `loading` does). `startedAt` is when the current load began (null when idle);
 * `shownAt` is when the loader appeared (null when hidden).
 */
export function loaderVisibility(loading: boolean, startedAt: number | null, shownAt: number | null, now: number) {
  if (loading && startedAt !== null) {
    if (shownAt !== null || now - startedAt >= LOADER_DELAY_MS) return { visible: true, recheckIn: null };
    return { visible: false, recheckIn: LOADER_DELAY_MS - (now - startedAt) };
  }
  if (shownAt === null) return { visible: false, recheckIn: null };
  const left = LOADER_MIN_MS - (now - shownAt);
  return left > 0 ? { visible: true, recheckIn: left } : { visible: false, recheckIn: null };
}

export type AsyncState = 'loading' | 'content' | 'empty' | 'error' | 'offline';

/**
 * What a screen shows. Anything on screen wins - a failed reload keeps the old content and says so
 * in a banner. With nothing to show, a failure that got no response at all (status 0) is the
 * offline state; any other failure is the error state.
 */
export function asyncState({ hasData, empty, error, errorStatus }: {
  hasData: boolean; empty?: boolean; error?: string | null; errorStatus?: number | null;
}): AsyncState {
  if (hasData) return empty ? 'empty' : 'content';
  if (error) return errorStatus === 0 ? 'offline' : 'error';
  return 'loading';
}

/** Plain-language detail for "More info": what happened, never a stack trace or a body. */
export function errorDetail(status: number | null | undefined): string {
  if (status === 0 || status == null) return 'No response arrived: the phone is offline, or the server did not answer in time.';
  if (status >= 500) return `The server had a problem (code ${status}). It is usually temporary.`;
  return `The server turned the request down (code ${status}).`;
}

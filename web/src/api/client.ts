import type { DetailLevel, Mascot, PrefectureCollection } from './types';

/**
 * The two GET calls this app makes.
 *
 * No base URL and no HTTP client library. Paths are relative, so the same code
 * works against the Vite dev proxy and against a production origin that serves
 * the API and the bundle together. See vite.config.ts.
 */

/** Thrown for any non-2xx response, so callers have one thing to catch. */
export class ApiError extends Error {
  readonly status: number;
  readonly url: string;

  constructor(status: number, url: string) {
    super(`GET ${url} failed with HTTP ${status}.`);
    this.name = 'ApiError';
    this.status = status;
    this.url = url;
  }
}

/**
 * Delay before the single retry below, in milliseconds. Long enough for the
 * request that failed us to have finished and populated the output cache.
 */
const RETRY_DELAY_MS = 300;

async function getJson<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await getWithOneRetry(url, signal);

  if (!response.ok) {
    throw new ApiError(response.status, url);
  }

  return (await response.json()) as T;
}

/**
 * One retry on a 5xx, and none on a 4xx.
 *
 * This is not general-purpose resilience. It is here for a specific and
 * reproducible server behaviour: output caching on `/api/prefectures` collapses
 * concurrent requests for the same cache key, so only the first executes the
 * endpoint and the rest wait for its response. If that first client disconnects,
 * its cancellation token fires, the endpoint throws, and the *waiting* requests
 * are handed a 500 they did nothing to cause. React's StrictMode starts and
 * immediately aborts one request per effect in development, which is exactly the
 * pattern that triggers it, on a cold cache, every time.
 *
 * A retry is the right thing on this side regardless of the cause — these are
 * idempotent GETs of static data — but the bug is on the server and is recorded
 * in NOTES for the API, not hidden by this.
 */
async function getWithOneRetry(url: string, signal: AbortSignal): Promise<Response> {
  const request: RequestInit = {
    signal,
    // The boundaries endpoint answers with application/geo+json. The +json
    // structured-syntax suffix means it is still JSON, but the Accept header says
    // both so a proxy has no reason to think otherwise.
    headers: { Accept: 'application/geo+json, application/json' }
  };

  const first = await fetch(url, request);

  if (first.status < 500) {
    return first;
  }

  await new Promise((resolve) => setTimeout(resolve, RETRY_DELAY_MS));

  // An abort during the delay must not become a second request.
  signal.throwIfAborted();

  return fetch(url, request);
}

/**
 * `GET /api/prefectures?detail=low|high` — all 47 prefectures as GeoJSON.
 *
 * The detail level is chosen from the viewport width, which is the only thing
 * that knows it. `low` simplifies harder and drops the small islands entirely;
 * that is a deliberate loss at phone zoom, where those islands are smaller than a
 * pixel. See DetailLevel.cs for how the two tolerances were sized.
 */
export function fetchPrefectures(
  detail: DetailLevel,
  signal: AbortSignal
): Promise<PrefectureCollection> {
  return getJson<PrefectureCollection>(`/api/prefectures?detail=${detail}`, signal);
}

/**
 * `GET /api/mascots` — every mascot, unfiltered.
 *
 * The API can filter by motif and debut year, and this app deliberately does not
 * use that. There are 35 records; fetching them once and searching in memory
 * makes the list view respond to each keystroke without a request, and it is the
 * same data the map needs to colour by verification level. Filtering server-side
 * would be the right call at a thousand mascots, which is v2's problem.
 *
 * Fetching this list is also why the app never calls `/api/prefectures/{id}`:
 * that endpoint returns a prefecture with its mascots, and by the time a visitor
 * taps a prefecture this app already holds both.
 */
export function fetchMascots(signal: AbortSignal): Promise<Mascot[]> {
  return getJson<Mascot[]>('/api/mascots', signal);
}

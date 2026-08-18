import { useEffect, useMemo, useState } from 'react';
import { fetchMascots, fetchPrefectures } from '../api/client';
import type { DetailLevel, Mascot, PrefectureCollection } from '../api/types';
import { buildEntries, type PrefectureEntry } from './prefectures';

/**
 * Everything the app knows: the boundaries at the requested detail level, the
 * mascots, and the two joined.
 *
 * Two requests, not 48. The mascot list is fetched whole and searched in memory,
 * and it is also what tells the map which prefectures are hand-verified — so the
 * one request serves the list view, the detail view and the map colouring alike.
 * Nothing calls `/api/prefectures/{id}`, because by the time a visitor taps a
 * prefecture this hook already holds its mascots.
 */
export interface Atlas {
  readonly status: 'loading' | 'ready' | 'error';
  /** The GeoJSON, for the map. Null until the first response arrives. */
  readonly collection: PrefectureCollection | null;
  /** All 47 prefectures with their mascots, in JIS code order. */
  readonly entries: readonly PrefectureEntry[];
  readonly error: Error | null;
  /** Refetches both endpoints. Bound to the retry button on the error screen. */
  readonly reload: () => void;
}

export function useAtlas(detail: DetailLevel): Atlas {
  const [collection, setCollection] = useState<PrefectureCollection | null>(null);
  const [mascots, setMascots] = useState<readonly Mascot[] | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [attempt, setAttempt] = useState(0);

  // The mascots do not depend on the viewport, so rotating a phone across the
  // breakpoint must not refetch them. Separate effect, separate dependencies.
  useEffect(() => {
    const controller = new AbortController();

    fetchMascots(controller.signal)
      .then(setMascots)
      .catch((cause: unknown) => reportUnlessAborted(cause, setError));

    return () => controller.abort();
  }, [attempt]);

  useEffect(() => {
    const controller = new AbortController();

    fetchPrefectures(detail, controller.signal)
      .then(setCollection)
      .catch((cause: unknown) => reportUnlessAborted(cause, setError));

    return () => controller.abort();
  }, [detail, attempt]);

  // The join is over 47 features and 35 mascots, so it is cheap — but it runs on
  // every render without this, and it allocates the arrays the list view then
  // treats as stable.
  const entries = useMemo(
    () => (collection && mascots ? buildEntries(collection, mascots) : []),
    [collection, mascots]
  );

  const status = error ? 'error' : collection && mascots ? 'ready' : 'loading';

  return {
    status,
    collection,
    entries,
    error,
    reload: () => {
      setError(null);
      setAttempt((previous) => previous + 1);
    }
  };
}

/**
 * An aborted fetch is this app cancelling its own request — a viewport change or
 * a StrictMode double-effect — and not something to show the visitor.
 */
function reportUnlessAborted(cause: unknown, report: (error: Error) => void): void {
  if (cause instanceof DOMException && cause.name === 'AbortError') {
    return;
  }

  report(cause instanceof Error ? cause : new Error(String(cause)));
}

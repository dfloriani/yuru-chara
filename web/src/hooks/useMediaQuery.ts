import { useSyncExternalStore } from 'react';

/**
 * Subscribes to a CSS media query and returns whether it currently matches.
 *
 * `useSyncExternalStore` rather than `useState` plus an effect, because
 * `matchMedia` is exactly the external mutable source it is built for: the first
 * render reads the real value instead of a placeholder that an effect corrects a
 * frame later. On this app that frame would be visible — it decides whether the
 * first boundaries request asks for `low` or `high` detail.
 */
export function useMediaQuery(query: string): boolean {
  const list = matchMediaFor(query);

  return useSyncExternalStore(
    (onChange) => {
      list.addEventListener('change', onChange);
      return () => list.removeEventListener('change', onChange);
    },
    () => list.matches
  );
}

// One MediaQueryList per query string, kept for the life of the page.
// useSyncExternalStore compares snapshots by identity and re-subscribes whenever
// the subscribe function changes, so handing it a new MediaQueryList on every
// render would tear down and rebuild the listener on every render.
const lists = new Map<string, MediaQueryList>();

function matchMediaFor(query: string): MediaQueryList {
  const existing = lists.get(query);

  if (existing) {
    return existing;
  }

  const created = window.matchMedia(query);
  lists.set(query, created);
  return created;
}

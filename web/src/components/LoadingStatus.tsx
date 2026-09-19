import { useEffect, useState } from 'react';

/** How long a load runs before the message explains the wait. */
const SLOW_LOAD_MS = 3000;

/**
 * The loading message, with an explanation added once the load has run for
 * three seconds.
 *
 * The API's hosting plan stops it after a period with no requests, and the first
 * request after that waits for it to start again. Without the explanation, a
 * visitor who waits that long has no way to tell a slow start from a broken page.
 *
 * The timer starts when this component mounts, which is each time a load starts,
 * the load started by "Try again" included.
 */
export function LoadingStatus() {
  const [slow, setSlow] = useState(false);

  useEffect(() => {
    const timer = window.setTimeout(() => setSlow(true), SLOW_LOAD_MS);
    return () => window.clearTimeout(timer);
  }, []);

  return (
    // role="status" makes a screen reader announce the explanation when it
    // appears, so the explanation reaches a visitor who cannot see the page.
    <div className="app__status" role="status">
      <p>Loading Japan’s 47 prefectures…</p>
      {slow && (
        <p>
          The server is starting. The first visit after a quiet period can take up to 20 seconds.
        </p>
      )}
    </div>
  );
}

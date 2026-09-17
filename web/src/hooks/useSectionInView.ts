import { useEffect, useState } from 'react';

/**
 * Which of the two stacked sections is on screen on a narrow viewport: `'list'`
 * once the top of the list section is above the middle of the viewport, and
 * `'map'` before that.
 *
 * An IntersectionObserver rather than a scroll listener. The root margin shrinks
 * the observed area to a line across the middle of the viewport, so the browser
 * calls back only when the list's edge crosses that line, not on every scroll
 * frame.
 *
 * `listId` is the id of the list section. The observer is attached only while
 * `enabled` is true, which the caller sets when the sections are stacked and the
 * list element exists.
 */
export function useSectionInView(listId: string, enabled: boolean): 'map' | 'list' {
  const [section, setSection] = useState<'map' | 'list'>('map');

  useEffect(() => {
    const list = enabled ? document.getElementById(listId) : null;

    if (!list) {
      return;
    }

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry) {
          return;
        }

        const middle = entry.rootBounds?.top ?? window.innerHeight / 2;
        setSection(entry.boundingClientRect.top <= middle ? 'list' : 'map');
      },
      { rootMargin: '-50% 0px -50% 0px' }
    );

    observer.observe(list);
    return () => observer.disconnect();
  }, [listId, enabled]);

  return section;
}

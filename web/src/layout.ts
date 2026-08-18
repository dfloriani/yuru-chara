/**
 * The one viewport breakpoint in this app, and everything that hangs off it.
 *
 * Narrow-first, per CLAUDE.md: the styles below this width are the base case and
 * the wide ones are the enhancement. 768px is where a side panel starts to fit
 * beside a map without squeezing either.
 *
 * Three separate decisions read this single query, which is why it is a constant
 * rather than three media queries that could drift apart:
 *
 *  - `?detail=low` or `?detail=high` on the boundaries request.
 *  - Bottom sheet or side panel for the detail view.
 *  - Whether the map and the list are shown together or one at a time.
 */
export const WIDE_VIEWPORT_QUERY = '(min-width: 768px)';

/**
 * How much of the map a bottom sheet covers, in pixels. The map uses it to keep a
 * selected prefecture out from under the sheet when it pans.
 *
 * A number here and a `max-height` in the stylesheet is duplication, and the
 * alternative — measuring the sheet element and feeding its height back in — adds
 * a ResizeObserver and a render pass to place a pan by a few pixels either way.
 */
export const BOTTOM_SHEET_HEIGHT = 320;

/**
 * The zoom at which the map starts drawing all 47 prefecture name labels. Below
 * it, only the selected prefecture is labelled.
 *
 * At the zoom that fits Japan into a 375px-wide phone the whole country is about
 * 200px across, and 47 names in that space overlap into something unreadable. So
 * the labels are drawn when there is room for them, and the list view carries the
 * names when there is not.
 *
 * 5 is the value that puts the split in the right place, and it was measured
 * rather than guessed. Leaflet's default `zoomSnap` of 1 makes the initial fit
 * land on a whole zoom level: that level is 4 while the map pane is narrower than
 * roughly 500px, and 5 above it. The wide layout gives 360px of the window to the
 * sidebar, so in practice a window from about 900px across opens with all 47
 * labels drawn and anything narrower opens with none. Counted in a browser at
 * 375, 768, 1280 and 1920 pixels wide; at 6 no width a browser actually opens at
 * would show a label at all.
 */
export const LABEL_MIN_ZOOM = 5;

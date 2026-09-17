/**
 * The one viewport breakpoint in this app, and everything that hangs off it.
 *
 * Narrow-first, per CLAUDE.md: the styles below this width are the base case and
 * the wide ones are the enhancement. 48rem (768px at the default font size) is
 * where a side panel starts to fit beside a map without squeezing either.
 *
 * Three separate decisions read this single query, which is why it is a constant
 * rather than three media queries that could drift apart:
 *
 *  - `?detail=low` or `?detail=high` on the boundaries request.
 *  - Bottom sheet or side panel for the detail view.
 *  - Whether the map and the list are shown together or one at a time.
 */
export const WIDE_VIEWPORT_QUERY = '(min-width: 48rem)';

/**
 * The most of the map a bottom sheet covers, in rem: the same 20rem as the sheet's
 * `max-height` in detail-panel.css.
 *
 * A number here and a `max-height` in the stylesheet is duplication, and the
 * alternative — measuring the sheet element and feeding its height back in — adds
 * a ResizeObserver and a render pass to place a pan by a few pixels either way.
 */
const BOTTOM_SHEET_MAX_HEIGHT_REM = 20;

/**
 * The bottom sheet's maximum height in CSS pixels, which is the unit Leaflet pans
 * in. The map uses it to keep a selected prefecture out from under the sheet.
 *
 * 1rem is the computed font size of the root element.
 */
export function bottomSheetHeightPx(): number {
  const rootFontSizePx = Number.parseFloat(getComputedStyle(document.documentElement).fontSize);
  return BOTTOM_SHEET_MAX_HEIGHT_REM * rootFontSizePx;
}

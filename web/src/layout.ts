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
 *  - Whether the map and the list are stacked or side by side.
 */
export const WIDE_VIEWPORT_QUERY = '(min-width: 48rem)';

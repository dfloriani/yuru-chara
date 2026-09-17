import type { ComponentType } from 'react';
import type { PrefectureCollection } from '../api/types';
import type { Coverage } from '../data/coverage';
import { LeafletPrefectureMap } from './LeafletPrefectureMap';

/**
 * The narrow interface between this app and whatever draws the map.
 *
 * CLAUDE.md requires the map implementation to be swappable: Leaflet over PostGIS
 * was chosen against Google's data-driven styling for boundaries, and the cost of
 * that choice is meant to be one component, not a rewrite. This file is the seam.
 * Everything Leaflet-shaped lives behind it — no other module in `src` imports
 * `leaflet` or `react-leaflet`, and nothing here mentions either.
 *
 * What crosses the seam is deliberately small:
 *
 *  - GeoJSON in, because that is what the API serves and what every map library
 *    reads.
 *  - JIS prefecture codes out, because that is the identifier the rest of the app
 *    and the whole Japanese dataset are keyed on. Notably *not* a library's
 *    internal layer id, and not a Google Place ID — keying on those is one of the
 *    reasons data-driven styling was rejected. See DECISIONS.md 15.
 *  - Coverage, not colours. The implementation resolves a coverage state to a
 *    fill through the shared table in data/coverage.ts, so a replacement map
 *    cannot quietly draw a different palette from the legend.
 *
 * One rule does not cross the seam as a prop, because it has no value to pass,
 * and a replacement must keep it: prefecture name labels never overlap. A label
 * that would overlap one already drawn is not drawn, the selected prefecture is
 * always labelled, and larger prefectures are labelled before smaller ones. See
 * DECISIONS.md 22.
 */
export interface PrefectureMapProps {
  /** All 47 prefectures, as `GET /api/prefectures` returned them. */
  readonly collection: PrefectureCollection;

  /** The coverage state to draw a prefecture in, by JIS code. */
  readonly coverageFor: (jisCode: number) => Coverage;

  /** The selected prefecture's JIS code, or null when nothing is selected. */
  readonly selectedJisCode: number | null;

  /** Called with a JIS code on tap, or with null when the background is tapped. */
  readonly onSelect: (jisCode: number | null) => void;

  /**
   * Pixels at the bottom of the map that something else is covering — the bottom
   * sheet on a narrow viewport. The map keeps the selected prefecture out of that
   * strip when it pans to it.
   */
  readonly obscuredBottomPx: number;
}

/**
 * The map implementation in use.
 *
 * Replacing Leaflet means changing this one line and adding a file next to
 * LeafletPrefectureMap.tsx that satisfies `PrefectureMapProps`. Nothing else in
 * the app has to be touched, and no API code at all.
 */
export const PrefectureMap: ComponentType<PrefectureMapProps> = LeafletPrefectureMap;

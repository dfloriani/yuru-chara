import type { Mascot } from '../api/types';

/**
 * How complete a prefecture's mascot data is. This is the thing the map is
 * coloured by, and it is the app's answer to a question a visitor should not have
 * to tap to find out: is there anything here?
 *
 * Three states rather than two, because coverage and confidence are separate
 * facts (DECISIONS.md 6):
 *
 *  - `none`      — no mascot record at all. 14 of the 47 prefectures, and a
 *                  recorded gap in the automated source rather than missing work.
 *  - `automated` — a record exists, taken from Wikidata and not hand-checked.
 *  - `verified`  — at least one mascot was checked field by field against the
 *                  owning body's own website.
 *
 * Collapsing the last two would lose exactly the distinction the data model was
 * built to carry.
 */
export type Coverage = 'none' | 'automated' | 'verified';

/** Least to most complete. The order the legend and the list filters use. */
export const COVERAGE_ORDER: readonly Coverage[] = ['verified', 'automated', 'none'];

export interface CoverageStyle {
  /** Polygon fill on the map, and the legend swatch. */
  readonly fill: string;
  /** Polygon outline. Darker than the fill so adjacent prefectures separate. */
  readonly stroke: string;
  /** Text sitting on `fill`, for the list badges. */
  readonly onFill: string;
  /** Short name, for a badge. */
  readonly label: string;
  /** What the state means, in plain words. The legend prints this. */
  readonly description: string;
}

/**
 * One table for the map fills, the legend swatches and the list badges, so the
 * three cannot drift apart. The colours are literal strings rather than CSS
 * custom properties because Leaflet sets SVG fills from JavaScript and would have
 * to read the computed style back out of the document to use a variable.
 *
 * The three fills differ mainly in lightness: dark teal, light teal, and a grey
 * with almost no hue. The states are ordered, and more confidence gets a darker
 * fill. A reader with a colour-vision deficiency, or a reader of a phone screen
 * in daylight, can see a difference in lightness when two hues look the same.
 */
export const COVERAGE: Record<Coverage, CoverageStyle> = {
  verified: {
    fill: '#17796e',
    stroke: '#0d4e46',
    onFill: '#ffffff',
    label: 'Verified',
    description: 'Checked against the owning body’s own website.'
  },
  automated: {
    fill: '#7cc4ba',
    stroke: '#3f8e83',
    onFill: '#0d3f39',
    label: 'Automated',
    description: 'From Wikidata, not hand-checked.'
  },
  none: {
    fill: '#b5b0a7',
    stroke: '#8b867d',
    onFill: '#2b2925',
    label: 'No data',
    description: 'No verified mascot data yet.'
  }
};

/**
 * The coverage of one prefecture, from its mascots.
 *
 * A prefecture counts as verified when *any* of its mascots is
 * `ManuallyVerified`. Some prefectures carry a popular unofficial mascot next to
 * the official one, and one hand-checked record is enough for the map to say
 * there is something reliable here.
 */
export function coverageOf(mascots: readonly Mascot[]): Coverage {
  if (mascots.length === 0) {
    return 'none';
  }

  return mascots.some((mascot) => mascot.verificationLevel === 'ManuallyVerified')
    ? 'verified'
    : 'automated';
}

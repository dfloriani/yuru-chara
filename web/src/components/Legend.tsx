import { COVERAGE, COVERAGE_ORDER, type Coverage } from '../data/coverage';
import './legend.css';

/**
 * What the map's colours mean, and how many prefectures are in each state.
 *
 * The counts are the point as much as the colours are. "No data — 14" says that a
 * third of the map being grey is a known gap in the source rather than a loading
 * failure, which is the reading a visitor would otherwise be left to guess at.
 */
export function Legend({ counts }: { readonly counts: Readonly<Record<Coverage, number>> }) {
  return (
    <div className="legend">
      <h2 className="legend__title">Mascot data</h2>
      <ul className="legend__items">
        {COVERAGE_ORDER.map((coverage) => (
          <li key={coverage} className="legend__item">
            <span
              className="legend__swatch"
              // Inline, from the same table the map draws from. A CSS class per
              // state would be a second place for the palette to live in.
              style={{
                background: COVERAGE[coverage].fill,
                borderColor: COVERAGE[coverage].stroke
              }}
              aria-hidden="true"
            />
            <span className="legend__label">{COVERAGE[coverage].label}</span>
            <span className="legend__count">{counts[coverage]}</span>
          </li>
        ))}
      </ul>
      <p className="legend__note">
        Grey prefectures have no verified mascot data yet.
        {/* The second sentence is hidden on a narrow viewport, where three more
            lines of small type would cover map. The first one is not: without it
            a third of the map being grey reads as a loading failure. */}
        <span className="legend__note-detail">
          {' '}
          Verified means checked against the owning body’s own site; automated means taken from
          Wikidata and not hand-checked.
        </span>
      </p>
    </div>
  );
}

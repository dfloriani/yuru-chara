import { useCallback, useMemo, useState } from 'react';
import { AttributionFooter } from './components/AttributionFooter';
import { DetailPanel } from './components/DetailPanel';
import { Legend } from './components/Legend';
import { PrefectureList } from './components/PrefectureList';
import { COVERAGE_ORDER, type Coverage } from './data/coverage';
import { useAtlas } from './data/useAtlas';
import { useMediaQuery } from './hooks/useMediaQuery';
import { BOTTOM_SHEET_HEIGHT, LABEL_MIN_ZOOM, WIDE_VIEWPORT_QUERY } from './layout';
import { PrefectureMap } from './map/PrefectureMap';
import './app.css';

/**
 * The app shell, and the only place the viewport width is read.
 *
 * Three things hang off that one query, all of them decided here rather than
 * discovered further down: which detail level the boundaries are requested at,
 * whether the detail view is a bottom sheet or a side panel, and whether the map
 * and the list are shown together or one at a time.
 */
export function App() {
  const wide = useMediaQuery(WIDE_VIEWPORT_QUERY);

  // Narrow viewports ask for coarser geometry. The API defaults to `high` when
  // the parameter is missing, so this is an explicit opt-in — see
  // DetailLevelExtensions.TryParse for why the default is that way round.
  const atlas = useAtlas(wide ? 'high' : 'low');

  const [selectedJisCode, setSelectedJisCode] = useState<number | null>(null);
  // Narrow only. On a wide viewport the map and the list are both on screen.
  const [narrowView, setNarrowView] = useState<'map' | 'list'>('map');

  const byJisCode = useMemo(
    () => new Map(atlas.entries.map((entry) => [entry.jisCode, entry])),
    [atlas.entries]
  );

  // Passed to the map, which calls it once per prefecture on every restyle.
  // useCallback because a new function identity would restyle all 47 layers on
  // every render of this component.
  const coverageFor = useCallback(
    (jisCode: number): Coverage => byJisCode.get(jisCode)?.coverage ?? 'none',
    [byJisCode]
  );

  const counts = useMemo(() => countByCoverage(atlas.entries.map((entry) => entry.coverage)), [atlas.entries]);

  const selected = selectedJisCode === null ? null : (byJisCode.get(selectedJisCode) ?? null);
  const closeDetail = useCallback(() => setSelectedJisCode(null), []);

  return (
    <div className="app">
      <header className="app__header">
        <h1 className="app__title">
          Yuru-Chara Map
          <span className="app__title-note">Japan’s 47 prefecture mascots</span>
        </h1>

        {/* Narrow only: on a wide viewport both views are on screen at once, so
            there is nothing to switch between. */}
        {!wide && (
          <div className="app__views" role="group" aria-label="Choose a view">
            <ViewButton current={narrowView} value="map" onSelect={setNarrowView}>
              Map
            </ViewButton>
            <ViewButton current={narrowView} value="list" onSelect={setNarrowView}>
              List
            </ViewButton>
          </div>
        )}
      </header>

      <main className="app__main">
        {atlas.status === 'error' && (
          <div className="app__status">
            <p>Could not load the map data.</p>
            <p className="app__status-detail">{atlas.error?.message}</p>
            <button type="button" className="app__retry" onClick={atlas.reload}>
              Try again
            </button>
          </div>
        )}

        {atlas.status === 'loading' && <p className="app__status">Loading Japan’s 47 prefectures…</p>}

        {atlas.status === 'ready' && atlas.collection && (
          <>
            {/* Both views stay mounted on a narrow viewport and one is hidden.
                Unmounting the map would throw away its pan and zoom every time
                the visitor looked at the list, and unmounting the list would
                throw away the search they had typed. `hidden` also keeps the
                hidden view out of the accessibility tree, which display:none
                alone in a stylesheet would not guarantee. */}
            <section className="app__map" hidden={!wide && narrowView !== 'map'}>
              <PrefectureMap
                collection={atlas.collection}
                coverageFor={coverageFor}
                selectedJisCode={selectedJisCode}
                onSelect={setSelectedJisCode}
                labelMinZoom={LABEL_MIN_ZOOM}
                // The map only needs to know about the sheet, which is the only
                // thing that covers it. The wide layout puts the panel beside the
                // map instead of over it.
                obscuredBottomPx={!wide && selected !== null ? BOTTOM_SHEET_HEIGHT : 0}
              />
              <Legend counts={counts} />
            </section>

            <section className="app__sidebar" hidden={!wide && narrowView !== 'list'}>
              {wide && selected !== null && (
                <DetailPanel entry={selected} variant="panel" onClose={closeDetail} />
              )}
              <PrefectureList
                entries={atlas.entries}
                selectedJisCode={selectedJisCode}
                onSelect={setSelectedJisCode}
              />
            </section>

            {/* A sibling of both views rather than a child of the map, so it
                overlays whichever one is showing. Selecting from the list is the
                likelier path on a phone, and a sheet inside the hidden map
                section would not appear at all. */}
            {!wide && selected !== null && (
              <DetailPanel entry={selected} variant="sheet" onClose={closeDetail} />
            )}
          </>
        )}
      </main>

      <AttributionFooter expanded={wide} />
    </div>
  );
}

function ViewButton({
  current,
  value,
  onSelect,
  children
}: {
  readonly current: 'map' | 'list';
  readonly value: 'map' | 'list';
  readonly onSelect: (view: 'map' | 'list') => void;
  readonly children: string;
}) {
  return (
    <button
      type="button"
      className="app__view"
      aria-pressed={current === value}
      onClick={() => onSelect(value)}
    >
      {children}
    </button>
  );
}

function countByCoverage(coverages: readonly Coverage[]): Record<Coverage, number> {
  const counts = Object.fromEntries(COVERAGE_ORDER.map((coverage) => [coverage, 0])) as Record<
    Coverage,
    number
  >;

  for (const coverage of coverages) {
    counts[coverage] += 1;
  }

  return counts;
}

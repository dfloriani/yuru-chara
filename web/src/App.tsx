import { useCallback, useMemo, useRef, useState } from 'react';
import { AttributionFooter } from './components/AttributionFooter';
import { DetailPanel } from './components/DetailPanel';
import { Legend } from './components/Legend';
import { PrefectureList } from './components/PrefectureList';
import { COVERAGE_ORDER, type Coverage } from './data/coverage';
import { useAtlas } from './data/useAtlas';
import { useMediaQuery } from './hooks/useMediaQuery';
import { useSectionInView } from './hooks/useSectionInView';
import { WIDE_VIEWPORT_QUERY } from './layout';
import { PrefectureMap } from './map/PrefectureMap';
import './app.css';

/**
 * The app shell, and the only place the viewport width is read.
 *
 * Three things hang off that one query, all of them decided here rather than
 * discovered further down: which detail level the boundaries are requested at,
 * whether the detail view is a bottom sheet or a side panel, and whether the map
 * and the list are stacked or side by side.
 */
export function App() {
  const wide = useMediaQuery(WIDE_VIEWPORT_QUERY);

  // Narrow viewports ask for coarser geometry. The API defaults to `high` when
  // the parameter is missing, so this is an explicit opt-in — see
  // DetailLevelExtensions.TryParse for why the default is that way round.
  const atlas = useAtlas(wide ? 'high' : 'low');

  const [selectedJisCode, setSelectedJisCode] = useState<number | null>(null);

  // Narrow only: the section on screen, so the jump link for it can show that the
  // visitor is already there.
  const sectionInView = useSectionInView('list', !wide && atlas.status === 'ready');

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

  const counts = useMemo(
    () => countByCoverage(atlas.entries.map((entry) => entry.coverage)),
    [atlas.entries]
  );

  const selected = selectedJisCode === null ? null : (byJisCode.get(selectedJisCode) ?? null);
  const closeDetail = useCallback(() => setSelectedJisCode(null), []);

  const headerRef = useRef<HTMLElement>(null);
  const mapSectionRef = useRef<HTMLElement>(null);
  const sheetRef = useRef<HTMLElement>(null);
  // Narrow only: where the bottom edge of the map will be, from the top of the
  // viewport, once a scroll started by a list selection ends. Null when the page
  // is not being scrolled to the map, and the map's current position applies.
  const mapBottomAfterScrollRef = useRef<number | null>(null);

  const selectFromMap = useCallback((jisCode: number | null) => {
    mapBottomAfterScrollRef.current = null;
    setSelectedJisCode(jisCode);
  }, []);

  // Narrow only: a selection from the list scrolls the page to the map, so the
  // visitor sees the map move to the prefecture while the sheet shows its
  // details. The map section ends with the map, and after the scroll its top is
  // at the bottom of the sticky header (its scroll-margin-top), which gives where
  // the map's bottom edge will be.
  const selectFromList = useCallback(
    (jisCode: number) => {
      mapBottomAfterScrollRef.current = null;
      setSelectedJisCode(jisCode);

      const section = mapSectionRef.current;
      const header = headerRef.current;

      if (wide || !section || !header) {
        return;
      }

      mapBottomAfterScrollRef.current =
        header.getBoundingClientRect().height + section.getBoundingClientRect().height;
      section.scrollIntoView({ block: 'start' });
    },
    [wide]
  );

  // How many pixels at the bottom of the map the bottom sheet covers. The map
  // calls this when it pans to a selection, which is after React has added the
  // sheet to the page, so the sheet's real top edge can be measured: its height
  // depends on how much text the selected prefecture has.
  const mapCoveredPx = useCallback(() => {
    const sheet = sheetRef.current;
    const section = mapSectionRef.current;

    if (wide || !sheet || !section) {
      return 0;
    }

    const mapBottom = mapBottomAfterScrollRef.current ?? section.getBoundingClientRect().bottom;
    return Math.max(0, mapBottom - sheet.getBoundingClientRect().top);
  }, [wide]);

  return (
    <div className="app">
      <header ref={headerRef} className="app__header">
        <h1 className="app__title">
          Yuru-Chara Map
          <span className="app__title-note">Japan’s 47 prefecture mascots</span>
        </h1>

        {/* Narrow only. The map and the list are stacked on one scrolling page,
            and these links move to each of them. They are links, not toggle
            buttons, because they go to a place on the same page. On a wide
            viewport both are on screen, so there is nowhere to go. */}
        {!wide && (
          <nav className="app__jump" aria-label="Jump to">
            <JumpLink target="map" current={sectionInView === 'map'}>
              Map
            </JumpLink>
            <JumpLink target="list" current={sectionInView === 'list'}>
              List
            </JumpLink>
          </nav>
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

        {atlas.status === 'loading' && (
          <p className="app__status">Loading Japan’s 47 prefectures…</p>
        )}

        {atlas.status === 'ready' && atlas.collection && (
          <>
            <section ref={mapSectionRef} id="map" className="app__map">
              {/* Above the map at every width: a visitor looks at the map
                  first. "Select" is correct for a mouse, a finger and a
                  keyboard. */}
              <p className="app__instruction">
                Select a prefecture on the map or in the list to see its mascot.
              </p>
              <div className="app__map-frame">
                <PrefectureMap
                  collection={atlas.collection}
                  coverageFor={coverageFor}
                  selectedJisCode={selectedJisCode}
                  onSelect={selectFromMap}
                  // The map only needs to know about the sheet, which is the only
                  // thing that covers it. The wide layout puts the panel beside the
                  // map instead of over it.
                  obscuredBottomPx={mapCoveredPx}
                />
                <Legend counts={counts} />
              </div>
            </section>

            <section
              id="list"
              className={`app__sidebar${!wide && selected !== null ? ' app__sidebar--under-sheet' : ''}`}
            >
              {wide && selected !== null && (
                <DetailPanel entry={selected} variant="panel" onClose={closeDetail} />
              )}
              <PrefectureList
                entries={atlas.entries}
                selectedJisCode={selectedJisCode}
                onSelect={selectFromList}
              />
            </section>

            {/* Fixed to the bottom of the viewport, so it overlays whichever part
                of the page is on screen. Selecting from the list is the likelier
                path on a phone, and the map can be scrolled out of view then. */}
            {!wide && selected !== null && (
              <DetailPanel ref={sheetRef} entry={selected} variant="sheet" onClose={closeDetail} />
            )}
          </>
        )}
      </main>

      <AttributionFooter />
    </div>
  );
}

/**
 * A link to one of the two stacked sections.
 *
 * For the section already on screen it is disabled: a click does nothing, and
 * `aria-disabled` makes a screen reader say it is unavailable. It keeps its
 * `href`, so it stays focusable. Without the `href`, a keyboard user who
 * activates the List link would lose focus as soon as the list scrolled into view
 * and that link became the current one. `aria-current="location"` makes a screen
 * reader also say "current location".
 */
function JumpLink({
  target,
  current,
  children
}: {
  readonly target: 'map' | 'list';
  readonly current: boolean;
  readonly children: string;
}) {
  return (
    <a
      className="app__jump-link"
      href={`#${target}`}
      aria-current={current ? 'location' : undefined}
      aria-disabled={current || undefined}
      onClick={current ? (event) => event.preventDefault() : undefined}
    >
      {children}
    </a>
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

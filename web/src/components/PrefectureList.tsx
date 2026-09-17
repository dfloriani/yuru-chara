import { useDeferredValue, useId, useMemo, useRef, useState } from 'react';
import type { Region } from '../api/types';
import { COVERAGE } from '../data/coverage';
import { filterEntries, groupByRegion, type PrefectureEntry } from '../data/prefectures';
import './prefecture-list.css';

/**
 * All 47 prefectures as a searchable list, grouped by region.
 *
 * CLAUDE.md treats this as a first-class view rather than a fallback for the map,
 * and on a phone it is likely the better way in: tapping Kagawa on a full-Japan
 * map is hard however wide the hit stroke is, and typing four letters is not.
 *
 * The search runs over the mascots too, so "bear" finds Kumamoto and "くまモン"
 * finds it as well. See `buildEntries` for what goes into the haystack.
 *
 * Each region opens and closes, at every viewport width. `defaultExpanded` sets
 * only the state the regions start in.
 */
export function PrefectureList({
  entries,
  selectedJisCode,
  onSelect,
  defaultExpanded
}: {
  readonly entries: readonly PrefectureEntry[];
  readonly selectedJisCode: number | null;
  readonly onSelect: (jisCode: number) => void;
  /** True to start with every region open, false to start with every region closed. */
  readonly defaultExpanded: boolean;
}) {
  const [query, setQuery] = useState('');
  const searchId = useId();
  const regionIdPrefix = useId();

  // The list re-renders 47 rows per keystroke. useDeferredValue lets the input
  // paint the typed character immediately and the rows catch up, which is the
  // difference between a responsive field and a laggy one on a slow phone.
  const deferredQuery = useDeferredValue(query);
  const matches = useMemo(() => filterEntries(entries, deferredQuery), [entries, deferredQuery]);
  const groups = useMemo(() => groupByRegion(matches), [matches]);
  const allRegions = useMemo(() => groupByRegion(entries).map((group) => group.region), [entries]);

  // Read once, on the first render. A later change of viewport width does not
  // reset the regions that the visitor opened or closed.
  const [openRegions, setOpenRegions] = useState<ReadonlySet<Region>>(
    () => new Set(defaultExpanded ? allRegions : [])
  );

  // A prefecture selected on the map opens its region, so the row is there when
  // the visitor moves to the list. This is state adjusted during render, the
  // pattern React documents for "a prop changed", rather than an effect: an
  // effect would render the list closed first and then open it.
  const [previousSelection, setPreviousSelection] = useState(selectedJisCode);

  if (selectedJisCode !== previousSelection) {
    setPreviousSelection(selectedJisCode);
    const region = entries.find((entry) => entry.jisCode === selectedJisCode)?.region;

    if (region !== undefined && !openRegions.has(region)) {
      setOpenRegions(new Set([...openRegions, region]));
    }
  }

  // While a search is active, every region with a match is shown open and has no
  // toggle. Otherwise a search on a phone, where every region starts closed,
  // would find matches and show none of them. The saved open and closed state is
  // not changed, and it applies again when the search is cleared.
  const searching = deferredQuery.trim() !== '';
  const allOpen = allRegions.every((region) => openRegions.has(region));

  const searchRef = useRef<HTMLDivElement>(null);
  const groupsRef = useRef<HTMLUListElement>(null);

  const toggleAllRegions = () => {
    setOpenRegions(new Set(allOpen ? [] : allRegions));

    if (!allOpen) {
      scrollGroupsIntoView();
    }
  };

  // On a narrow viewport the button can be at the bottom of the screen with the
  // regions below it, so opening them would change nothing the visitor can see.
  // This scrolls the page until the first region is directly under the sticky
  // search block. The button is in that block, so it stays on screen and keeps
  // focus. On a wide viewport the page does not scroll, so this changes nothing.
  const scrollGroupsIntoView = () => {
    const search = searchRef.current;
    const groups = groupsRef.current;

    if (!search || !groups) {
      return;
    }

    // Where the search block's bottom edge is once it sticks: its sticky `top`
    // plus its own height.
    const stuckBottom =
      Number.parseFloat(getComputedStyle(search).top) + search.getBoundingClientRect().height;
    const distance = groups.getBoundingClientRect().top - stuckBottom;

    if (distance > 0) {
      window.scrollBy({ top: distance });
    }
  };

  const toggleRegion = (region: Region) => {
    const next = new Set(openRegions);

    if (next.has(region)) {
      next.delete(region);
    } else {
      next.add(region);
    }

    setOpenRegions(next);
  };

  return (
    <div className="prefecture-list">
      <div ref={searchRef} className="prefecture-list__search">
        <label className="visually-hidden" htmlFor={searchId}>
          Search prefectures and mascots
        </label>
        <input
          id={searchId}
          className="prefecture-list__input"
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search a prefecture, mascot or motif"
          autoComplete="off"
          // Latin and kana both, so an iOS keyboard does not capitalise a romaji
          // name into something the search will not match.
          autoCapitalize="none"
          spellCheck={false}
        />
        <div className="prefecture-list__status">
          {/* aria-live so a screen reader hears the result count change as the
              query narrows, rather than only on demand. */}
          <p className="prefecture-list__count" aria-live="polite">
            {matches.length} of {entries.length} prefectures
          </p>
          {!searching && (
            <button type="button" className="prefecture-list__all" onClick={toggleAllRegions}>
              {allOpen ? 'Hide all regions' : 'Show all regions'}
            </button>
          )}
        </div>
      </div>

      {groups.length === 0 ? (
        <p className="prefecture-list__empty">
          Nothing matches “{deferredQuery}”. Try a prefecture name, a mascot name, or a motif such
          as “bear” or “pear”.
        </p>
      ) : (
        <ul ref={groupsRef} className="prefecture-list__groups">
          {groups.map((group) => {
            const open = searching || openRegions.has(group.region);
            const rowsId = `${regionIdPrefix}-${group.region}`;
            const count = `${group.entries.length} ${group.entries.length === 1 ? 'prefecture' : 'prefectures'}`;

            return (
              <li key={group.region}>
                <h3 className="prefecture-list__region">
                  {searching ? (
                    <span className="prefecture-list__region-label">
                      <span className="prefecture-list__region-name">{group.label}</span>
                      <span className="prefecture-list__region-count">{count}</span>
                    </span>
                  ) : (
                    // A button inside the heading, as in the WAI-ARIA accordion
                    // pattern. The heading keeps its role, so a screen reader
                    // moves between regions with the heading key, and
                    // aria-expanded makes it say "expanded" or "collapsed".
                    <button
                      type="button"
                      className="prefecture-list__region-label prefecture-list__toggle"
                      aria-expanded={open}
                      aria-controls={rowsId}
                      onClick={() => toggleRegion(group.region)}
                    >
                      <span className="prefecture-list__region-name">{group.label}</span>
                      <span className="prefecture-list__region-count">{count}</span>
                      {/* The word is the main signal and the chevron is extra.
                          Both are hidden from a screen reader, which gets the
                          state from aria-expanded instead. */}
                      <span className="prefecture-list__toggle-word" aria-hidden="true">
                        {open ? 'Hide' : 'Show'}
                      </span>
                      <Chevron />
                    </button>
                  )}
                </h3>
                <ul id={rowsId} className="prefecture-list__rows" hidden={!open}>
                  {group.entries.map((entry) => (
                    <li key={entry.jisCode}>
                      <PrefectureRow
                        entry={entry}
                        selected={entry.jisCode === selectedJisCode}
                        onSelect={onSelect}
                      />
                    </li>
                  ))}
                </ul>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

/**
 * Points down. The stylesheet turns it to point up when the region is open, from
 * the button's aria-expanded, so the icon cannot show a state the button does
 * not have.
 */
function Chevron() {
  return (
    <svg
      className="prefecture-list__chevron"
      viewBox="0 0 16 16"
      aria-hidden="true"
      focusable="false"
    >
      <path
        d="M3.5 6 8 10.5 12.5 6"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

function PrefectureRow({
  entry,
  selected,
  onSelect
}: {
  readonly entry: PrefectureEntry;
  readonly selected: boolean;
  readonly onSelect: (jisCode: number) => void;
}) {
  const coverage = COVERAGE[entry.coverage];
  const [lead] = entry.mascots;

  return (
    <button
      type="button"
      className="prefecture-row"
      // aria-pressed rather than aria-selected: this is a toggle button in a
      // plain list, not an option in a listbox, and claiming to be a listbox
      // would promise arrow-key navigation this does not implement.
      aria-pressed={selected}
      onClick={() => onSelect(entry.jisCode)}
    >
      <span className="prefecture-row__names">
        <span className="prefecture-row__name-en">{entry.nameEn}</span>
        <span className="prefecture-row__name-ja" lang="ja">
          {entry.nameJa}
        </span>
      </span>

      <span
        className="prefecture-row__badge"
        style={{ background: coverage.fill, color: coverage.onFill, borderColor: coverage.stroke }}
      >
        {coverage.label}
      </span>

      <span className="prefecture-row__mascot">
        {lead ? (
          <>
            <span lang="ja">{lead.nameJa}</span>
            {lead.nameRomaji !== null && (
              <span className="prefecture-row__romaji"> {lead.nameRomaji}</span>
            )}
            {lead.motif !== null && <span className="prefecture-row__motif"> · {lead.motif}</span>}
            {/* Some prefectures have more than one recorded mascot. Saying how
                many is more useful than silently showing the first. */}
            {entry.mascots.length > 1 && (
              <span className="prefecture-row__more"> +{entry.mascots.length - 1} more</span>
            )}
          </>
        ) : (
          // The plain sentence CLAUDE.md asks for, in the list as well as the
          // detail panel. An empty row would read as a bug.
          <span className="prefecture-row__none">No verified mascot data yet</span>
        )}
      </span>
    </button>
  );
}

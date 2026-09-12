import { useDeferredValue, useId, useMemo, useState } from 'react';
import { COVERAGE } from '../data/coverage';
import { filterEntries, groupByRegion, type PrefectureEntry } from '../data/prefectures';
import './prefecture-list.css';

/**
 * All 47 prefectures as a searchable list.
 *
 * CLAUDE.md treats this as a first-class view rather than a fallback for the map,
 * and on a phone it is likely the better way in: tapping Kagawa on a full-Japan
 * map is hard however wide the hit stroke is, and typing four letters is not.
 *
 * The search runs over the mascots too, so "bear" finds Kumamoto and "くまモン"
 * finds it as well. See `buildEntries` for what goes into the haystack.
 */
export function PrefectureList({
  entries,
  selectedJisCode,
  onSelect
}: {
  readonly entries: readonly PrefectureEntry[];
  readonly selectedJisCode: number | null;
  readonly onSelect: (jisCode: number) => void;
}) {
  const [query, setQuery] = useState('');
  const searchId = useId();

  // The list re-renders 47 rows per keystroke. useDeferredValue lets the input
  // paint the typed character immediately and the rows catch up, which is the
  // difference between a responsive field and a laggy one on a slow phone.
  const deferredQuery = useDeferredValue(query);
  const matches = useMemo(() => filterEntries(entries, deferredQuery), [entries, deferredQuery]);
  const groups = useMemo(() => groupByRegion(matches), [matches]);

  return (
    <div className="prefecture-list">
      <div className="prefecture-list__search">
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
        {/* aria-live so a screen reader hears the result count change as the
            query narrows, rather than only on demand. */}
        <p className="prefecture-list__count" aria-live="polite">
          {matches.length} of {entries.length} prefectures
        </p>
      </div>

      {groups.length === 0 ? (
        <p className="prefecture-list__empty">
          Nothing matches “{deferredQuery}”. Try a prefecture name, a mascot name, or a motif such
          as “bear” or “pear”.
        </p>
      ) : (
        <ul className="prefecture-list__groups">
          {groups.map((group) => (
            <li key={group.region}>
              <h3 className="prefecture-list__region">{group.label}</h3>
              <ul className="prefecture-list__rows">
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
          ))}
        </ul>
      )}
    </div>
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

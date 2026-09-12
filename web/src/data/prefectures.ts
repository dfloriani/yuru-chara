import type { Mascot, PrefectureCollection, PrefectureFeature, Region } from '../api/types';
import { coverageOf, type Coverage } from './coverage';

/**
 * One prefecture with everything the UI needs about it: the facts from the
 * boundaries endpoint, the mascots from the mascots endpoint, and the coverage
 * state derived from those mascots.
 *
 * The geometry is deliberately *not* here. It stays in the GeoJSON collection
 * that goes to the map component, so that the list view, the search and the
 * detail panel never touch a polygon.
 */
export interface PrefectureEntry {
  /** JIS X 0401 code, 1–47. The key everywhere in this app. */
  readonly jisCode: number;
  readonly nameEn: string;
  readonly nameJa: string;
  readonly nameRomaji: string;
  readonly region: Region;
  readonly regionLabel: string;
  readonly mascots: readonly Mascot[];
  readonly coverage: Coverage;
  /**
   * Every searchable word about this prefecture, lower-cased and joined.
   * Precomputed once at load rather than rebuilt per keystroke over 47 entries.
   */
  readonly searchText: string;
}

/** Joins the two responses on the JIS code. */
export function buildEntries(
  collection: PrefectureCollection,
  mascots: readonly Mascot[]
): readonly PrefectureEntry[] {
  // Grouped first so the join is one pass over the mascots rather than a scan of
  // all 35 for each of the 47 prefectures.
  const byPrefecture = new Map<number, Mascot[]>();

  for (const mascot of mascots) {
    const existing = byPrefecture.get(mascot.prefectureId);

    if (existing) {
      existing.push(mascot);
    } else {
      byPrefecture.set(mascot.prefectureId, [mascot]);
    }
  }

  return collection.features.map((feature) =>
    toEntry(feature, byPrefecture.get(feature.properties.jisCode) ?? [])
  );
}

function toEntry(feature: PrefectureFeature, mascots: Mascot[]): PrefectureEntry {
  const { jisCode, nameEn, nameJa, nameRomaji, region, regionLabel } = feature.properties;

  // Official mascots first, then by Japanese name — the same order the API's
  // prefecture detail response uses, applied here because the flat mascot list
  // arrives sorted by prefecture rather than within it.
  const sorted = [...mascots].sort(
    (left, right) =>
      Number(right.isOfficial) - Number(left.isOfficial) ||
      left.nameJa.localeCompare(right.nameJa, 'ja')
  );

  const searchable = [
    nameEn,
    nameJa,
    nameRomaji,
    regionLabel,
    // Searching for "Kumamon" or for "pear" should find the prefecture, not just
    // the mascot. The list view is the primary interface on a phone, so it has to
    // answer the question the visitor actually has.
    ...sorted.flatMap((mascot) => [
      mascot.nameJa,
      mascot.nameRomaji,
      mascot.motif,
      mascot.owningBody
    ])
  ];

  return {
    jisCode,
    nameEn,
    nameJa,
    nameRomaji,
    region,
    regionLabel,
    mascots: sorted,
    coverage: coverageOf(sorted),
    searchText: searchable
      .filter((value): value is string => value !== null)
      .join(' ')
      .toLowerCase()
  };
}

/**
 * The entries whose text contains every whitespace-separated term in the query.
 *
 * Every term, not any: typing "kumamoto bear" should narrow, which is what a
 * visitor means by adding a word. Substring rather than prefix matching, because
 * "chara" should find "yuru-chara" and because a Japanese query has no spaces to
 * anchor a prefix to.
 */
export function filterEntries(
  entries: readonly PrefectureEntry[],
  query: string
): readonly PrefectureEntry[] {
  const terms = query
    .toLowerCase()
    .split(/\s+/)
    .filter((term) => term.length > 0);

  if (terms.length === 0) {
    return entries;
  }

  return entries.filter((entry) => terms.every((term) => entry.searchText.includes(term)));
}

/** Entries grouped by region, in JIS code order — which is already north to south. */
export function groupByRegion(entries: readonly PrefectureEntry[]): readonly {
  readonly region: Region;
  readonly label: string;
  readonly entries: readonly PrefectureEntry[];
}[] {
  const groups: { region: Region; label: string; entries: PrefectureEntry[] }[] = [];

  for (const entry of entries) {
    const current = groups.at(-1);

    // The entries arrive in JIS code order, and JIS codes run north to south
    // region by region, so a region's prefectures are always contiguous. That
    // makes grouping a single pass with no sort and no lookup table.
    if (current?.region === entry.region) {
      current.entries.push(entry);
    } else {
      groups.push({ region: entry.region, label: entry.regionLabel, entries: [entry] });
    }
  }

  return groups;
}

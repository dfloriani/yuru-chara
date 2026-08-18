import type { Feature, FeatureCollection, MultiPolygon } from 'geojson';

/**
 * The wire types of the YuruChara API, hand-written to mirror the C# records in
 * src/YuruChara.Api. There is no code generation step: the API has seven
 * endpoints and three DTOs, and a generator would be more moving parts than the
 * thing it generates.
 *
 * Two serialiser settings from Program.cs shape everything here:
 *
 *  - System.Text.Json's default camelCase policy, so `NameJa` arrives as `nameJa`.
 *  - `JsonStringEnumConverter`, so enums arrive as their member names rather than
 *    numbers. That is why the enum types below are string unions: the names are
 *    the contract, and a union catches a typo at compile time where a bare
 *    `string` would not.
 */

/** The eight conventional regions. ASCII names, as the C# enum declares them. */
export type Region =
  | 'Hokkaido'
  | 'Tohoku'
  | 'Kanto'
  | 'Chubu'
  | 'Kansai'
  | 'Chugoku'
  | 'Shikoku'
  | 'Kyushu';

/**
 * Confidence in a mascot record. Separate from whether a record exists at all —
 * see DECISIONS.md 6. The UI shows the difference rather than hiding it.
 */
export type VerificationLevel = 'Automated' | 'ManuallyVerified';

/**
 * Whether an image of this mascot could lawfully be shown. This project ships no
 * mascot images under any status; the field exists so the UI can say why there is
 * no picture. See CLAUDE.md, "Image licensing".
 */
export type ImageLicenseStatus =
  | 'Unknown'
  | 'ApplicationRequired'
  | 'OfficialMaterialsPublished'
  | 'CommonsFreeLicense'
  | 'NoReuseGranted';

/** How much a source is trusted, least to most. */
export type SourceReliability = 'Community' | 'Aggregated' | 'Official';

/** Where one fact about a mascot came from. Attached per field, not per record. */
export interface SourceCitation {
  /** Name of the C# property this citation backs, e.g. "DebutYear". */
  readonly field: string;
  readonly sourceName: string;
  readonly url: string;
  /** ISO date, e.g. "2026-08-13". Serialised from a .NET DateOnly. */
  readonly retrievedOn: string;
  readonly reliability: SourceReliability;
}

/** A mascot as `GET /api/mascots` and the prefecture detail response return it. */
export interface Mascot {
  readonly id: string;
  readonly prefectureId: number;
  /** Denormalised by the API so the list view needs one request, not 48. */
  readonly prefectureNameEn: string;
  readonly nameJa: string;
  readonly nameRomaji: string | null;
  readonly motif: string | null;
  readonly debutYear: number | null;
  readonly owningBody: string | null;
  readonly officialUrl: string | null;
  readonly isOfficial: boolean;
  readonly imageLicenseStatus: ImageLicenseStatus;
  readonly licenseNotes: string | null;
  readonly verificationLevel: VerificationLevel;
  readonly sourceCitations: readonly SourceCitation[];
}

/**
 * The GeoJSON "properties" member of one prefecture feature, as built by
 * `PrefectureEndpoints.BuildFeatureCollectionAsync`.
 */
export interface PrefectureProperties {
  /** JIS X 0401 code, 1–47. The identifier everywhere in this app. */
  readonly jisCode: number;
  readonly nameEn: string;
  readonly nameJa: string;
  readonly nameRomaji: string;
  readonly region: Region;
  /** The same region name with its macrons, e.g. "Kantō". For display. */
  readonly regionLabel: string;
  /**
   * The stored `ST_PointOnSurface`, which is where a map label is drawn. This is
   * not the centroid: a centroid can fall outside a concave or island-heavy
   * prefecture, and four of the 47 are. See DECISIONS.md 5.
   *
   * Nullable because the column is, so the map has to cope with a prefecture it
   * cannot label rather than assume one is always there.
   */
  readonly labelLat: number | null;
  readonly labelLng: number | null;
  /**
   * How many mascots the prefecture has. Counted in SQL so the map can colour by
   * coverage without downloading the mascots — see the boundaries query.
   */
  readonly mascotCount: number;
}

/**
 * One prefecture. `MultiPolygon` and not `Polygon`: every Japanese prefecture is
 * stored as a MultiPolygon, because even the landlocked ones come from a dataset
 * that models them that way, and Nagasaki has hundreds of islands.
 */
export type PrefectureFeature = Feature<MultiPolygon, PrefectureProperties>;

/** What `GET /api/prefectures` returns. */
export type PrefectureCollection = FeatureCollection<MultiPolygon, PrefectureProperties>;

/** The `?detail=` query values the boundaries endpoint accepts. */
export type DetailLevel = 'low' | 'high';

using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using YuruChara.Domain.Mascots;
using YuruChara.Domain.Prefectures;
using YuruChara.Infrastructure;

namespace YuruChara.Ingestion.Seeding;

/// <summary>
/// Loads the committed seed into PostGIS: 47 boundaries, their mascots, and the
/// stored label points.
/// <para>
/// It uses <see cref="YuruCharaDbContext"/> directly. There is no repository layer
/// to go through — see DECISIONS.md 7 — and the seeder is one of the places that
/// makes that choice easy to read, because the raw SQL it issues for the label
/// points sits next to the LINQ that does everything else.
/// </para>
/// <para>
/// The seed is <b>idempotent</b>. Prefectures are matched on their JIS code and
/// mascots on the Guid committed in the seed file, so running it twice updates rows
/// rather than duplicating them, and a re-run after editing the seed file is the
/// normal way to apply a data change.
/// </para>
/// </summary>
public sealed class DatabaseSeeder(YuruCharaDbContext db)
{
    public async Task<SeedOutcome> SeedAsync(
        SeedDocument seed,
        IReadOnlyDictionary<int, MultiPolygon> boundaries,
        CancellationToken cancellationToken)
    {
        // Checks the two inputs agree before writing anything. They are produced by
        // different processes from different sources, and a mismatch here means one of
        // them is stale.
        ValidateInputs(seed, boundaries);

        var existingPrefectures = await db.Prefectures
            .Include(prefecture => prefecture.Mascots)
            .ToDictionaryAsync(prefecture => prefecture.Id, cancellationToken);

        var mascotsWritten = 0;

        foreach (var seedPrefecture in seed.Prefectures.OrderBy(prefecture => prefecture.JisCode))
        {
            var reference = JisPrefectures.ByCode[seedPrefecture.JisCode];
            var boundary = boundaries[seedPrefecture.JisCode];

            if (!existingPrefectures.TryGetValue(reference.JisCode, out var prefecture))
            {
                prefecture = new Prefecture
                {
                    Id = reference.JisCode,
                    NameEn = reference.NameEn,
                    NameJa = reference.NameJa,
                    NameRomaji = reference.NameRomaji,
                    Boundary = boundary
                };
                db.Prefectures.Add(prefecture);
            }
            else
            {
                prefecture.NameEn = reference.NameEn;
                prefecture.NameJa = reference.NameJa;
                prefecture.NameRomaji = reference.NameRomaji;
                prefecture.Boundary = boundary;
            }

            prefecture.Region = reference.Region;

            // LabelPoint and Centroid are left null here on purpose. They are derived
            // from Boundary, and PostGIS computes them in one statement after the
            // boundaries are in — see ComputeLabelPointsAsync.
            prefecture.LabelPoint = null;
            prefecture.Centroid = null;

            mascotsWritten += SyncMascots(prefecture, seedPrefecture);
        }

        await db.SaveChangesAsync(cancellationToken);

        var labelPointsComputed = await ComputeLabelPointsAsync(cancellationToken);

        return new SeedOutcome(
            Prefectures: seed.Prefectures.Count,
            Mascots: mascotsWritten,
            PrefecturesWithoutMascot: seed.Prefectures.Count(prefecture => prefecture.Mascots.Count == 0),
            ManuallyVerified: seed.Prefectures
                .SelectMany(prefecture => prefecture.Mascots)
                .Count(mascot => mascot.VerificationLevel is VerificationLevel.ManuallyVerified),
            LabelPointsComputed: labelPointsComputed);
    }

    /// <summary>
    /// Computes and stores <see cref="Prefecture.LabelPoint"/> and
    /// <see cref="Prefecture.Centroid"/> with one UPDATE.
    /// <para>
    /// <b>Raw SQL, and why EF Core LINQ was not enough.</b> Not because the functions
    /// are unreachable — both are. The Npgsql plugin maps <c>Geometry.Centroid</c> to
    /// <c>ST_Centroid</c>, and it maps <c>Geometry.InteriorPoint</c> to
    /// <c>ST_PointOnSurface</c>, which is easy to miss only because NetTopologySuite
    /// gives the operation a different name from the one PostGIS uses.
    /// <c>PostGisTranslationTests</c> pins both.
    /// </para>
    /// <para>
    /// The reason is that this is a set-based UPDATE of all 47 rows, from an
    /// expression over another column of the same row. Writing it in LINQ means
    /// <c>ExecuteUpdate</c>, whose <c>SetProperty</c> would have to assign a
    /// <c>Geometry</c>-typed expression to a <c>Point</c>-typed property, and so needs
    /// a cast that the provider would then have to translate. Loading the entities and
    /// assigning in .NET is the other option, and it means transferring every
    /// full-resolution boundary into memory to derive two points from them. One
    /// statement of plain SQL is shorter and is what actually happens.
    /// </para>
    /// <para>
    /// <b>Why ST_PointOnSurface and not ST_Centroid for the label.</b> A centroid is a
    /// centre of mass and is not guaranteed to lie inside its own polygon.
    /// <c>ST_PointOnSurface</c> always returns a point on the geometry, so labels land
    /// on the prefecture they name. The centroid is stored as well because it remains
    /// the right value for "which prefecture is nearest".
    /// </para>
    /// <para>
    /// Measured on the committed geometry, 4 of the 47 centroids fall outside their
    /// prefecture: Okinawa (58.9 km out to sea), Tokyo (34.7 km), Kagoshima (17.1 km,
    /// in Kinkō Bay) and Kōchi (0.2 km, off its concave coast). Kōchi is the
    /// instructive one — it has no distant islands, so being non-convex is enough on
    /// its own. Nagasaki, despite having more islands than any other prefecture, is
    /// <em>not</em> affected, because <c>ST_Centroid</c> is area-weighted and its
    /// mainland peninsulas dominate. "Many islands" is the wrong intuition here and
    /// "the shape is not convex" is the right one. See DECISIONS.md 5.
    /// </para>
    /// <para>
    /// <b>Why at seed time.</b> Both values are functions of a boundary that never
    /// changes between seed runs. Recomputing them on every read of all 47 boundaries
    /// would be repeated work for an unchanging answer, and it would make the
    /// output-cached boundaries endpoint pay for it on every cache miss.
    /// </para>
    /// </summary>
    private async Task<int> ComputeLabelPointsAsync(CancellationToken cancellationToken)
    {
        // ExecuteSqlRawAsync rather than an interpolated call: there are no parameters,
        // and the interpolated overload would treat the braces-free string the same way
        // while looking as though it were parameterising something.
        return await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prefectures
            SET "LabelPoint" = ST_PointOnSurface("Boundary"),
                "Centroid"   = ST_Centroid("Boundary")
            """,
            cancellationToken);
    }

    /// <summary>
    /// Adds, updates and removes this prefecture's mascots to match the seed file.
    /// Returns how many the file specified.
    /// </summary>
    private int SyncMascots(Prefecture prefecture, SeedPrefecture seedPrefecture)
    {
        var existing = prefecture.Mascots.ToDictionary(mascot => mascot.Id);

        foreach (var seedMascot in seedPrefecture.Mascots)
        {
            if (existing.Remove(seedMascot.Id, out var mascot))
            {
                Apply(seedMascot, mascot);
            }
            else
            {
                mascot = new Mascot
                {
                    // From the file, not generated. This is what keeps
                    // /api/mascots/{id} stable across re-seeds.
                    Id = seedMascot.Id,
                    PrefectureId = prefecture.Id,
                    NameJa = seedMascot.NameJa
                };
                Apply(seedMascot, mascot);
                prefecture.Mascots.Add(mascot);
            }
        }

        // Anything still here is in the database but no longer in the seed file, which
        // means it was deliberately removed. The cascade on the relationship would not
        // catch this, because the prefecture itself is not being deleted.
        foreach (var removed in existing.Values)
        {
            db.Mascots.Remove(removed);
        }

        return seedPrefecture.Mascots.Count;
    }

    /// <summary>
    /// Copies one seed record onto an entity. Written out by hand rather than mapped
    /// automatically, per the CLAUDE.md convention: every field is visible, and the
    /// two type conversions this needs are visible with it.
    /// </summary>
    private static void Apply(SeedMascot seed, Mascot mascot)
    {
        mascot.NameJa = seed.NameJa;
        mascot.NameRomaji = seed.NameRomaji;
        mascot.Motif = seed.Motif;
        mascot.DebutYear = seed.DebutYear;
        mascot.OwningBody = seed.OwningBody;
        mascot.OfficialUrl = ParseUrl(seed, nameof(SeedMascot.OfficialUrl), seed.OfficialUrl);
        mascot.IsOfficial = seed.IsOfficial;
        mascot.ImageLicenseStatus = seed.ImageLicenseStatus;
        mascot.LicenseNotes = seed.LicenseNotes;
        mascot.LicenseTermsUrl = ParseUrl(seed, nameof(SeedMascot.LicenseTermsUrl), seed.LicenseTermsUrl);
        mascot.VerificationLevel = seed.VerificationLevel;

        mascot.SourceCitations = seed.SourceCitations
            .Select(citation => new SourceCitation(
                citation.Field,
                citation.SourceName,
                Uri.TryCreate(citation.Url, UriKind.Absolute, out var citationUrl)
                    ? citationUrl
                    : throw new InvalidOperationException(
                        $"Mascot '{seed.NameJa}' ({seed.Id}) has a citation for '{citation.Field}' " +
                        $"whose Url is not an absolute URI: '{citation.Url}'."),
                citation.RetrievedOn,
                citation.Reliability))
            .ToList();
    }

    /// <summary>
    /// The seed file holds URLs as strings so that a malformed one is a seed-file
    /// error with a readable message rather than a deserialiser exception.
    /// Uri.TryCreate is where that message comes from.
    /// </summary>
    private static Uri? ParseUrl(SeedMascot seed, string field, string? value)
    {
        if (value is null)
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var url)
            ? url
            : throw new InvalidOperationException(
                $"Mascot '{seed.NameJa}' ({seed.Id}) has a {field} that is not an absolute URI: '{value}'.");
    }

    private static void ValidateInputs(
        SeedDocument seed,
        IReadOnlyDictionary<int, MultiPolygon> boundaries)
    {
        if (seed.Prefectures.Count != JisPrefectures.All.Count)
        {
            throw new InvalidOperationException(
                $"Seed file lists {seed.Prefectures.Count} prefectures; expected {JisPrefectures.All.Count}. " +
                "Every prefecture needs an entry, including the ones with no mascot.");
        }

        foreach (var seedPrefecture in seed.Prefectures)
        {
            if (!JisPrefectures.ByCode.TryGetValue(seedPrefecture.JisCode, out var reference))
            {
                throw new InvalidOperationException(
                    $"Seed file has JIS code {seedPrefecture.JisCode}, which is not 1–47.");
            }

            // Catches a seed file whose codes have been shifted relative to its names —
            // the failure that would otherwise put every mascot in the wrong prefecture
            // and still look plausible.
            if (!string.Equals(seedPrefecture.NameJa, reference.NameJa, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Seed file names JIS code {seedPrefecture.JisCode} as '{seedPrefecture.NameJa}', " +
                    $"but it is '{reference.NameJa}'.");
            }

            if (!boundaries.ContainsKey(seedPrefecture.JisCode))
            {
                throw new InvalidOperationException(
                    $"No boundary geometry for JIS code {seedPrefecture.JisCode} ({reference.NameJa}).");
            }
        }

        var duplicateIds = seed.Prefectures
            .SelectMany(prefecture => prefecture.Mascots)
            .GroupBy(mascot => mascot.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Seed file reuses mascot Guid(s): " + string.Join(", ", duplicateIds) +
                ". Each mascot needs its own, because the Guid is the identity the seeder matches on.");
        }
    }
}

/// <param name="Prefectures">Prefectures written. Always 47.</param>
/// <param name="Mascots">Mascot records written.</param>
/// <param name="PrefecturesWithoutMascot">Coverage gaps — a real state, not an error.</param>
/// <param name="ManuallyVerified">
/// Of those mascots, how many were hand-checked. Reported separately from the total
/// because coverage and confidence are separate things. See DECISIONS.md 6.
/// </param>
/// <param name="LabelPointsComputed">Rows the ST_PointOnSurface update touched.</param>
public sealed record SeedOutcome(
    int Prefectures,
    int Mascots,
    int PrefecturesWithoutMascot,
    int ManuallyVerified,
    int LabelPointsComputed);

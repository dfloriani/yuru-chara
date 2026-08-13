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
    /// <b>Raw SQL, and why EF Core LINQ was not enough.</b> The Npgsql provider maps
    /// <c>ST_Centroid</c> (as <c>Geometry.Centroid</c>) but has no translation for
    /// <c>ST_PointOnSurface</c> — NetTopologySuite calls the equivalent operation
    /// <c>InteriorPoint</c>, and that property is not in the provider's function
    /// mapping table, so a LINQ query using it throws at translation time. It could
    /// be computed client-side by NTS instead, but that means pulling all 47
    /// full-resolution boundaries into memory to derive two points from them, and
    /// PostGIS's own answer is the one that should be stored in a PostGIS column.
    /// </para>
    /// <para>
    /// <b>Why ST_PointOnSurface and not ST_Centroid for the label.</b> A centroid is
    /// a centre of mass and is not guaranteed to lie inside its own polygon. Several
    /// Japanese prefectures are cases where it does not: Nagasaki is hundreds of
    /// islands around a concave peninsula and its centroid falls in open water, and
    /// Tokyo includes the Izu and Ogasawara chains, which drag its centroid roughly a
    /// thousand kilometres out into the Pacific. <c>ST_PointOnSurface</c> always
    /// returns a point on the geometry, so labels land on the prefecture they name.
    /// The centroid is stored as well because it remains the right value for "which
    /// prefecture is nearest". See DECISIONS.md 5.
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

        // The seed file holds the URL as a string so that a malformed one is a
        // seed-file error with a readable message rather than a deserialiser
        // exception. Uri.TryCreate is where that message comes from.
        if (seed.OfficialUrl is null)
        {
            mascot.OfficialUrl = null;
        }
        else if (Uri.TryCreate(seed.OfficialUrl, UriKind.Absolute, out var url))
        {
            mascot.OfficialUrl = url;
        }
        else
        {
            throw new InvalidOperationException(
                $"Mascot '{seed.NameJa}' ({seed.Id}) has an OfficialUrl that is not an absolute URI: " +
                $"'{seed.OfficialUrl}'.");
        }

        mascot.IsOfficial = seed.IsOfficial;
        mascot.ImageLicenseStatus = seed.ImageLicenseStatus;
        mascot.LicenseNotes = seed.LicenseNotes;
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

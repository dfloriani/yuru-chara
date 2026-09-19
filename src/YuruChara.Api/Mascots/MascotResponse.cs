using YuruChara.Domain.Mascots;

namespace YuruChara.Api.Mascots;

/// <summary>
/// A mascot as the API returns it. Used both by <c>GET /api/mascots</c> and
/// inside a prefecture's detail response.
/// </summary>
/// <param name="PrefectureId">JIS code of the owning prefecture, so a flat mascot list can link back to the map.</param>
/// <param name="PrefectureNameEn">Denormalised onto the mascot so the searchable list view needs one request, not 48.</param>
/// <param name="ImageLicenseStatus">
/// Why there is no image field: mascot designs are copyrighted and this project
/// does not host or link images of them. The licence status is the data, and the
/// UI renders the motif instead. See CLAUDE.md, "Image licensing".
/// </param>
/// <param name="VerificationLevel">
/// Confidence, which is a separate question from whether a record exists at all.
/// Surfaced rather than hidden — see DECISIONS.md 6.
/// </param>
public sealed record MascotResponse(
    Guid Id,
    int PrefectureId,
    string PrefectureNameEn,
    string NameJa,
    string? NameRomaji,
    string? Motif,
    int? DebutYear,
    string? OwningBody,
    Uri? OfficialUrl,
    bool IsOfficial,
    ImageLicenseStatus ImageLicenseStatus,
    string? LicenseNotes,
    Uri? LicenseTermsUrl,
    VerificationLevel VerificationLevel,
    // The domain's SourceCitation record is used as the wire type unmodified.
    // It has no storage concerns in it and its shape is already exactly what a
    // client should see — it exists in order to be shown. A DTO with the same
    // five properties would be duplication, and the mapping between them would
    // be the kind of code that is only ever wrong. The cost is that renaming a
    // property on SourceCitation is a breaking API change; that is recorded here
    // so it is a known cost rather than a surprise.
    IReadOnlyList<SourceCitation> SourceCitations)
{
    /// <summary>
    /// Maps one entity. Written out by hand per the CLAUDE.md convention: no
    /// AutoMapper, so every field that crosses into the response is visible in
    /// one place.
    /// <para>
    /// This runs in memory rather than as a LINQ projection translated to SQL.
    /// That is a deliberate choice at this size — v1 has 35 mascot records, so
    /// the whole table is smaller than the query plan for a projection of it —
    /// and it keeps <see cref="Mascot.SourceCitations"/> simple, because that
    /// property is a value-converted <c>jsonb</c> column and reshaping it inside
    /// a translated projection is the sort of thing that stops working for
    /// unobvious reasons. The boundaries endpoint does the opposite and projects
    /// in SQL, because there the whole point is to not transfer the column.
    /// </para>
    /// </summary>
    public static MascotResponse From(Mascot mascot, string prefectureNameEn) =>
        new(
            mascot.Id,
            mascot.PrefectureId,
            prefectureNameEn,
            mascot.NameJa,
            mascot.NameRomaji,
            mascot.Motif,
            mascot.DebutYear,
            mascot.OwningBody,
            mascot.OfficialUrl,
            mascot.IsOfficial,
            mascot.ImageLicenseStatus,
            mascot.LicenseNotes,
            mascot.LicenseTermsUrl,
            mascot.VerificationLevel,
            [.. mascot.SourceCitations]);
}

/// <summary>
/// What the mascot queries select: the whole entity, plus one column from the
/// prefecture it belongs to.
/// <para>
/// The point of the pairing is what it leaves out. <c>Include(m =&gt; m.Prefecture)</c>
/// would be the obvious way to reach the prefecture's name, but it materialises the
/// whole prefecture, and that means reading the <c>Boundary</c> column — hundreds of
/// kilobytes of coastline per row — to put one short string in the response. Naming
/// the single property in a projection makes EF Core join and select just that
/// column.
/// </para>
/// </summary>
internal sealed record MascotRow(Mascot Mascot, string PrefectureNameEn)
{
    public MascotResponse ToResponse() => MascotResponse.From(Mascot, PrefectureNameEn);
}

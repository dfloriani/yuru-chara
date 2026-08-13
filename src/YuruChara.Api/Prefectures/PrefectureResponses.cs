using NetTopologySuite.Geometries;
using YuruChara.Api.Mascots;
using YuruChara.Domain.Mascots;
using YuruChara.Domain.Prefectures;

namespace YuruChara.Api.Prefectures;

/// <summary>
/// A prefecture without its geometry. Returned by the point-in-polygon lookup, and
/// the head of the detail response.
/// </summary>
/// <param name="Region">The ASCII enum name, e.g. <c>Kanto</c>. Stable; safe to key and group on.</param>
/// <param name="RegionLabel">The same name spelled properly, e.g. <c>Kantō</c>. For display.</param>
/// <param name="LabelLat">
/// Latitude of the stored <c>ST_PointOnSurface</c> — where a map label is drawn.
/// Not the centroid; see DECISIONS.md 5. Null only if the seeder has not run.
/// </param>
public sealed record PrefectureSummaryResponse(
    int Id,
    string NameEn,
    string NameJa,
    string NameRomaji,
    Region Region,
    string RegionLabel,
    double? LabelLat,
    double? LabelLng);

/// <summary>
/// A prefecture and its mascots.
/// <para>
/// <b>No geometry.</b> A client showing a detail panel already holds the polygon,
/// because it drew the map from <c>GET /api/prefectures</c>. Repeating it would make
/// the API's smallest response one of its largest, carrying data the caller has.
/// </para>
/// </summary>
/// <param name="Mascots">
/// Empty is a real answer, not an error: 14 of the 47 prefectures have no verified
/// mascot, and the API says so rather than guessing. See CLAUDE.md, "Do not invent
/// mascot data".
/// </param>
public sealed record PrefectureDetailResponse(
    int Id,
    string NameEn,
    string NameJa,
    string NameRomaji,
    Region Region,
    string RegionLabel,
    double? LabelLat,
    double? LabelLng,
    IReadOnlyList<MascotResponse> Mascots);

/// <summary>
/// The shape the prefecture queries select, which is every column except
/// <c>Boundary</c>. A projection type, not a response type — it exists so the two
/// non-geometry endpoints read the same columns and neither of them reads the
/// coastline.
/// <para>
/// It is separate from <see cref="PrefectureSummaryResponse"/> because the two are
/// not the same thing: this one holds an NTS <see cref="Point"/>, which is what the
/// database returns, and the response holds two doubles, which is what a client
/// wants. Turning one into the other is where the (X, Y) = (lng, lat) axis order is
/// resolved, once.
/// </para>
/// </summary>
internal sealed record PrefectureRow(
    int Id,
    string NameEn,
    string NameJa,
    string NameRomaji,
    Region Region,
    Point? LabelPoint)
{
    /// <summary>
    /// Hand-written mapping, per the CLAUDE.md convention — no AutoMapper, so every
    /// field that reaches the response is visible here.
    /// </summary>
    public PrefectureSummaryResponse ToSummary() =>
        new(
            Id,
            NameEn,
            NameJa,
            NameRomaji,
            Region,
            Region.ToDisplayName(),
            // NetTopologySuite's Point is (X, Y), which under SRID 4326 is
            // (longitude, latitude). Reading them into named fields here is what stops
            // the axis order being a question anywhere else in the stack.
            LabelPoint?.Y,
            LabelPoint?.X);

    public PrefectureDetailResponse ToDetail(IEnumerable<Mascot> mascots) =>
        new(
            Id,
            NameEn,
            NameJa,
            NameRomaji,
            Region,
            Region.ToDisplayName(),
            LabelPoint?.Y,
            LabelPoint?.X,
            // Official mascots first, then by Japanese name. Some prefectures have a
            // popular unofficial mascot alongside the official one, and a detail panel
            // should lead with the official answer. Ordinal rather than a culture-aware
            // comparison: these are kana, and the ordering only has to be stable.
            [.. mascots
                .OrderByDescending(mascot => mascot.IsOfficial)
                .ThenBy(mascot => mascot.NameJa, StringComparer.Ordinal)
                .Select(mascot => MascotResponse.From(mascot, NameEn))]);
}

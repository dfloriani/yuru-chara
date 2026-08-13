using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using YuruChara.Domain.Prefectures;
using YuruChara.Infrastructure;

namespace YuruChara.Api.Prefectures;

/// <summary>
/// Everything under <c>/api/prefectures</c>. One static class per
/// <c>MapGroup</c>, per the CLAUDE.md convention.
/// </summary>
public static class PrefectureEndpoints
{
    /// <summary>Output-cache policy for the boundaries endpoint. Defined in Program.cs.</summary>
    public const string BoundariesCachePolicy = "prefecture-boundaries";

    public static RouteGroupBuilder MapPrefectureEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/prefectures");

        group.MapGet("/", GetBoundariesAsync)
            .CacheOutput(BoundariesCachePolicy)
            .WithName("GetPrefectureBoundaries");

        // Registered before "/{id:int}" for readability only. There is no ambiguity
        // to resolve: the :int constraint means "at" cannot match that route, which
        // is as much the reason for the constraint as validation is.
        group.MapGet("/at", GetPrefectureAtAsync)
            .WithName("GetPrefectureAtPoint");

        group.MapGet("/{id:int}", GetPrefectureAsync)
            .WithName("GetPrefecture");

        return group;
    }

    /// <summary>
    /// <c>GET /api/prefectures?detail=low|high</c> — all 47 prefectures as a GeoJSON
    /// FeatureCollection, boundaries simplified by <c>ST_Simplify</c>.
    /// </summary>
    private static async Task<IResult> GetBoundariesAsync(
        YuruCharaDbContext db,
        string? detail,
        CancellationToken cancellationToken)
    {
        if (!DetailLevelExtensions.TryParse(detail, out var level))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["detail"] = [$"'{detail}' is not a detail level. Use 'low' or 'high', or omit it for 'high'."]
            });
        }

        var collection = await BuildFeatureCollectionAsync(db, level.Tolerance(), cancellationToken);

        // application/geo+json is GeoJSON's registered media type (RFC 7946 §12).
        // The +json structured-syntax suffix means every JSON client still treats it
        // as JSON, HttpClient's ReadFromJsonAsync included.
        return Results.Json(collection, contentType: "application/geo+json");
    }

    /// <summary>
    /// Reads the prefectures and builds the FeatureCollection.
    /// </summary>
    /// <param name="toleranceInDegrees">
    /// <c>ST_Simplify</c> tolerance, or <see langword="null"/> for unsimplified
    /// geometry. No HTTP request produces null — the endpoint always resolves a
    /// <see cref="DetailLevel"/> — but the payload-size test does, so that the raw
    /// figure it reports comes from this same query and this same serialiser as the
    /// two it is compared against.
    /// </param>
    internal static async Task<FeatureCollection> BuildFeatureCollectionAsync(
        YuruCharaDbContext db,
        double? toleranceInDegrees,
        CancellationToken cancellationToken)
    {
        // Projected in SQL rather than mapped in memory, and here that matters in a
        // way it does not for mascots: selecting the simplified geometry means
        // PostGIS does the simplifying and returns roughly 600 KB, where
        // materialising the entities and simplifying afterwards would transfer all
        // 2.4 MB of full-resolution boundaries and discard three quarters of it.
        //
        // The GIST index is not involved. This query reads every row and has no
        // spatial predicate to filter on. The index earns its keep in
        // GetPrefectureAtAsync below.
        var rows = await db.Prefectures
            .AsNoTracking()
            .OrderBy(prefecture => prefecture.Id)
            .Select(prefecture => new
            {
                prefecture.Id,
                prefecture.NameEn,
                prefecture.NameJa,
                prefecture.NameRomaji,
                prefecture.Region,
                prefecture.LabelPoint,
                // PostGis.Simplify is a function mapping onto ST_Simplify, which the
                // Npgsql provider does not translate on its own. See PostGis.cs for
                // why, and for what the alternatives would have cost.
                Boundary = toleranceInDegrees.HasValue
                    ? PostGis.Simplify(prefecture.Boundary, toleranceInDegrees.Value)
                    : prefecture.Boundary,
                // Counted in SQL. The map colours a prefecture by whether it has a
                // mascot, and a count is far cheaper to send than the mascots.
                MascotCount = prefecture.Mascots.Count
            })
            .ToListAsync(cancellationToken);

        var collection = new FeatureCollection();

        foreach (var row in rows)
        {
            // The GeoJSON "properties" member. A string-keyed AttributesTable rather
            // than one of this project's typed records, because that is the shape
            // GeoJSON defines and what NetTopologySuite.IO.GeoJSON4STJ writes.
            var properties = new AttributesTable
            {
                // The JIS X 0401 code, named for what it is rather than "id".
                //
                // GeoJSON does define an optional top-level "id" member on a feature,
                // and GeoJSON4STJ has an idPropertyName setting that looks like the
                // way to populate it. It is not: that setting is applied when
                // *reading*, to lift a top-level "id" into the attributes table.
                // Writing an attribute called "id" was tried and it stays inside
                // "properties" like any other. Nothing here needs the top-level
                // member — Leaflet hands a click handler feature.properties — so the
                // key is spelled "jisCode", which says what the number actually is.
                { "jisCode", row.Id },
                { "nameEn", row.NameEn },
                { "nameJa", row.NameJa },
                { "nameRomaji", row.NameRomaji },
                { "region", row.Region.ToString() },
                { "regionLabel", row.Region.ToDisplayName() },
                // Where to draw the prefecture's name: the stored ST_PointOnSurface,
                // which is always inside the polygon. The centroid is not, for four
                // of the 47. See DECISIONS.md 5. NTS Point is (X, Y) = (lng, lat).
                { "labelLat", row.LabelPoint?.Y },
                { "labelLng", row.LabelPoint?.X },
                { "mascotCount", row.MascotCount }
            };

            collection.Add(new Feature(row.Boundary, properties));
        }

        return collection;
    }

    /// <summary>
    /// <c>GET /api/prefectures/{id}</c> — one prefecture and its mascots.
    /// </summary>
    private static async Task<IResult> GetPrefectureAsync(
        YuruCharaDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        var prefecture = await SelectWithoutGeometry(db.Prefectures.Where(candidate => candidate.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        if (prefecture is null)
        {
            return TypedResults.NotFound();
        }

        // A second round trip rather than a nested collection projection. At 47
        // prefectures and 35 mascots the difference is not measurable, and two short
        // queries read more plainly than one with a sub-select in it.
        var mascots = await db.Mascots
            .AsNoTracking()
            .Where(mascot => mascot.PrefectureId == id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(prefecture.ToDetail(mascots));
    }

    /// <summary>
    /// <c>GET /api/prefectures/at?lat=&amp;lng=</c> — which prefecture contains this
    /// point.
    /// <para>
    /// v1 has no UI for this. It is here because it is the question people ask of a
    /// spatial database, and because it is the one endpoint whose cost depends on the
    /// GIST index.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetPrefectureAtAsync(
        YuruCharaDbContext db,
        double? lat,
        double? lng,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (lat is null or < -90 or > 90)
        {
            errors["lat"] = ["A latitude in degrees, between -90 and 90, is required."];
        }

        if (lng is null or < -180 or > 180)
        {
            errors["lng"] = ["A longitude in degrees, between -180 and 180, is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // NetTopologySuite's Point constructor takes (x, y), which under SRID 4326 is
        // (longitude, latitude) — the reverse of how coordinates are normally written
        // and of this endpoint's own query string. Getting it backwards puts Tokyo in
        // the Indian Ocean, so the arguments are named at the call site.
        var point = new Point(x: lng!.Value, y: lat!.Value)
        {
            // Must match the column's SRID. A new NTS geometry defaults to SRID 0, and
            // PostGIS refuses to compare geometries in two different spatial reference
            // systems rather than assuming they agree.
            SRID = 4326
        };

        // Geometry.Contains translates to ST_Contains. This is the one endpoint where
        // a PostGIS predicate runs as a filter rather than a function applied to a
        // projection.
        //
        // ST_Contains is index-assisted: PostGIS rewrites it as a bounding-box test
        // using the && operator, which the GIST index on Boundary answers, followed by
        // an exact test on only the rows that survive. So it reads one or two polygons
        // in full instead of doing exact point-in-polygon against all 47 — which at
        // full resolution means all 736 rings.
        //
        // FirstOrDefault, not Single: a point on the shared edge of two prefectures is
        // contained by neither, because ST_Contains excludes the boundary itself, and
        // a point at sea is contained by none. Both give 404, which is the honest
        // answer.
        // Ordered before the projection, not after. EF Core cannot translate an
        // OrderBy over a property of an already-projected record — it sees
        // `new PrefectureRow(...).Id` and has nothing to turn that into — so sorting
        // has to happen while the query is still over the entity.
        var match = await SelectWithoutGeometry(
                db.Prefectures
                    .Where(candidate => candidate.Boundary.Contains(point))
                    .OrderBy(candidate => candidate.Id))
            .FirstOrDefaultAsync(cancellationToken);

        return match is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(match.ToSummary());
    }

    /// <summary>
    /// Every prefecture column except <c>Boundary</c>.
    /// <para>
    /// Shared by the two endpoints that return prefecture facts rather than shapes.
    /// Neither response contains geometry, and loading the entity would read the
    /// column anyway — so the projection is what keeps up to a megabyte of coastline
    /// out of a response that is a few hundred bytes.
    /// </para>
    /// </summary>
    private static IQueryable<PrefectureRow> SelectWithoutGeometry(IQueryable<Prefecture> query) =>
        query
            .AsNoTracking()
            .Select(prefecture => new PrefectureRow(
                prefecture.Id,
                prefecture.NameEn,
                prefecture.NameJa,
                prefecture.NameRomaji,
                prefecture.Region,
                prefecture.LabelPoint));
}

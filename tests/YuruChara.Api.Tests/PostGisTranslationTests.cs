using Microsoft.EntityFrameworkCore;
using YuruChara.Api.Tests.TestSupport;
using YuruChara.Infrastructure;

namespace YuruChara.Api.Tests;

/// <summary>
/// Which PostGIS functions this project can reach from LINQ, and which it cannot.
/// <para>
/// These are the facts the comments in <c>PostGis.cs</c> and <c>DatabaseSeeder.cs</c>
/// rest on, and they are properties of a third-party translator rather than of this
/// codebase — so they can change under a package upgrade without anything else
/// failing. Pinning them means a Npgsql release that starts translating
/// <c>ST_Simplify</c> shows up as a test telling us the <c>HasDbFunction</c>
/// declaration is no longer needed, instead of going unnoticed for a year.
/// </para>
/// </summary>
public class PostGisTranslationTests(PostGisApiFixture fixture)
{
    /// <summary>
    /// <c>Geometry.Contains</c> is translated, which is what lets the point-in-polygon
    /// endpoint be ordinary LINQ.
    /// </summary>
    [Fact]
    public async Task GeometryContains_TranslatesToStContains()
    {
        await using var db = fixture.CreateDbContext();

        var sql = db.Prefectures
            .Where(prefecture => prefecture.Boundary.Contains(prefecture.LabelPoint!))
            .Select(prefecture => prefecture.Id)
            .ToQueryString();

        Assert.Contains("ST_Contains", sql, StringComparison.Ordinal);

        // And it runs. A query string proves translation; executing proves the SQL is
        // valid against the real server.
        var matched = await db.Prefectures
            .CountAsync(prefecture => prefecture.Boundary.Contains(prefecture.LabelPoint!),
                PostGisApiFixture.Cancellation);

        Assert.Equal(47, matched);
    }

    /// <summary>
    /// <c>ST_PointOnSurface</c> <b>is</b> reachable from LINQ, as NetTopologySuite's
    /// <c>Geometry.InteriorPoint</c>. Recorded because it is easy to assume otherwise:
    /// NTS gives the operation a different name from the one PostGIS uses, so it does
    /// not turn up by searching for "PointOnSurface".
    /// <para>
    /// The seeder still computes the stored label points with raw SQL, and this test
    /// is what keeps that comment honest about why — the reason is that it is a
    /// set-based UPDATE of all 47 rows from an expression over another column, not
    /// that the function is unreachable.
    /// </para>
    /// </summary>
    [Fact]
    public async Task InteriorPoint_TranslatesToStPointOnSurface()
    {
        await using var db = fixture.CreateDbContext();

        var query = db.Prefectures
            .Where(prefecture => prefecture.Id == 13)
            .Select(prefecture => prefecture.Boundary.InteriorPoint);

        Assert.Contains("ST_PointOnSurface", query.ToQueryString(), StringComparison.Ordinal);

        var computed = await query.SingleAsync(PostGisApiFixture.Cancellation);
        var stored = await db.Prefectures
            .Where(prefecture => prefecture.Id == 13)
            .Select(prefecture => prefecture.LabelPoint)
            .SingleAsync(PostGisApiFixture.Cancellation);

        // The stored value is what the seeder wrote with ST_PointOnSurface. Computing
        // it again through LINQ has to give the same point, or the two routes disagree
        // about what the label position is.
        Assert.NotNull(stored);
        Assert.Equal(computed.Coordinate.X, stored.X, precision: 9);
        Assert.Equal(computed.Coordinate.Y, stored.Y, precision: 9);
    }

    /// <summary>
    /// <c>ST_Simplify</c> is <b>not</b> reachable through the Npgsql plugin, which is
    /// the whole reason <see cref="PostGis.Simplify"/> exists. This asserts that the
    /// declared function reaches the database under the right name.
    /// </summary>
    [Fact]
    public void Simplify_TranslatesToStSimplifyThroughTheDeclaredFunction()
    {
        using var db = fixture.CreateDbContext();

        var sql = db.Prefectures
            .Select(prefecture => PostGis.Simplify(prefecture.Boundary, 0.02))
            .ToQueryString();

        Assert.Contains("ST_Simplify", sql, StringComparison.Ordinal);

        // Unquoted and unqualified, which is what IsBuiltIn() produces. Were it
        // emitted as public."ST_Simplify", PostgreSQL would not fold the quoted
        // identifier and would fail to resolve it against the real function, which
        // is named st_simplify.
        Assert.DoesNotContain("\"ST_Simplify\"", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Simplification discards whole polygons, measured in the database rather than
    /// through the serialised response.
    /// <para>
    /// This is the property that separates <c>ST_Simplify</c> from
    /// <c>ST_SimplifyPreserveTopology</c>, and the reason this project uses the
    /// former: a ring that collapses is dropped rather than kept in a degenerate
    /// form. That is what makes the phone-sized payload possible, because Nagasaki
    /// and Okinawa are mostly islands too small to draw at that zoom.
    /// </para>
    /// <para>
    /// Polygons rather than vertices, because <c>Geometry.NumPoints</c> maps to
    /// <c>ST_NumPoints</c>, which is defined for LineStrings only and returns NULL
    /// for a polygon — PostGIS spells the one that counts every vertex
    /// <c>ST_NPoints</c>, and the Npgsql plugin does not map it. The payload test
    /// counts vertices from the GeoJSON instead.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Simplify_DiscardsPolygonsAtBothServedTolerances()
    {
        await using var db = fixture.CreateDbContext();

        var counts = await db.Prefectures
            .Select(prefecture => new
            {
                Raw = prefecture.Boundary.NumGeometries,
                High = PostGis.Simplify(prefecture.Boundary, 0.005).NumGeometries,
                Low = PostGis.Simplify(prefecture.Boundary, 0.02).NumGeometries
            })
            .ToListAsync(PostGisApiFixture.Cancellation);

        Assert.Equal(736, counts.Sum(count => count.Raw));
        Assert.True(counts.Sum(count => count.High) < counts.Sum(count => count.Raw));
        Assert.True(counts.Sum(count => count.Low) < counts.Sum(count => count.High));
    }

    /// <summary>
    /// Calling the declared function outside a query throws rather than quietly
    /// simplifying in .NET. A client-side fallback would mean a query that failed to
    /// translate still returned the right answer, having transferred every
    /// full-resolution boundary to do it — a correctness-preserving performance
    /// collapse, which is the hardest kind to notice.
    /// </summary>
    [Fact]
    public void Simplify_CannotBeCalledOutsideAQuery()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => PostGis.Simplify(new NetTopologySuite.Geometries.Point(0, 0), 0.1));

        Assert.Contains("ST_Simplify", exception.Message, StringComparison.Ordinal);
    }
}

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YuruChara.Api.Tests.TestSupport;

namespace YuruChara.Api.Tests;

/// <summary>
/// <c>GET /api/prefectures/at?lat=&amp;lng=</c> — the <c>ST_Contains</c> lookup.
/// <para>
/// These are the tests that need a real database. The coordinates are real
/// landmarks and the polygons are the real committed boundaries, so a pass means
/// the query genuinely resolved a point against Japanese geometry rather than
/// against a fixture that was built to agree with it.
/// </para>
/// </summary>
public class PrefectureAtPointTests(PostGisApiFixture fixture)
{
    /// <summary>
    /// Real landmarks, with their JIS prefecture codes. Chosen to cover the cases
    /// that are actually hard rather than just to repeat the same shape:
    /// <list type="bullet">
    ///   <item>Tokyo Station is deep inside a small, densely bordered prefecture.</item>
    ///   <item>Naha is on Okinawa, an island 400 km from the mainland — the prefecture whose centroid is furthest out to sea.</item>
    ///   <item>Takamatsu is in Kagawa, the smallest prefecture in the country.</item>
    ///   <item>Nagasaki tests geometry with hundreds of rings in it.</item>
    ///   <item>Cape Ashizuri is on the tip of Kōchi, whose concave coast is what puts its centroid offshore.</item>
    /// </list>
    /// </summary>
    public static TheoryData<string, double, double, int, string> Landmarks => new()
    {
        { "Tokyo Station", 35.681236, 139.767125, 13, "Tokyo" },
        { "Osaka Castle", 34.687315, 135.526201, 27, "Osaka" },
        { "Sapporo TV Tower", 43.061111, 141.356389, 1, "Hokkaido" },
        { "Kumamoto Castle", 32.806111, 130.705833, 43, "Kumamoto" },
        { "Naha, Shuri Castle", 26.217000, 127.719400, 47, "Okinawa" },
        { "Takamatsu Station", 34.350556, 134.046944, 37, "Kagawa" },
        { "Nagasaki Peace Park", 32.773500, 129.863100, 42, "Nagasaki" },
        { "Cape Ashizuri", 32.724722, 133.016389, 39, "Kochi" }
    };

    [Theory]
    [MemberData(nameof(Landmarks))]
    public async Task KnownLandmark_ResolvesToItsPrefecture(
        string landmark, double lat, double lng, int expectedId, string expectedNameEn)
    {
        using var response = await fixture.GetAsync($"/api/prefectures/at?lat={lat}&lng={lng}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(PostGisApiFixture.Cancellation));
        var root = document.RootElement;

        Assert.Equal(expectedId, root.GetProperty("id").GetInt32());
        Assert.Equal(expectedNameEn, root.GetProperty("nameEn").GetString());
        Assert.False(string.IsNullOrEmpty(landmark));
    }

    /// <summary>
    /// The axis-order test, stated as its own case because it is the failure this
    /// endpoint is most likely to have. NetTopologySuite's Point is (X, Y), which is
    /// (longitude, latitude) — the reverse of the query string. Swapping them turns
    /// Tokyo Station into 139.7°N 35.7°E, which is in the Arctic Ocean and belongs to
    /// no prefecture, so a 404 here is what a swap looks like.
    /// </summary>
    [Fact]
    public async Task SwappedCoordinates_DoNotResolve()
    {
        using var correct = await fixture.GetAsync("/api/prefectures/at?lat=35.681236&lng=139.767125");
        using var swapped = await fixture.GetAsync("/api/prefectures/at?lat=139.767125&lng=35.681236");

        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);

        // 139.77 is not a latitude, so this is rejected before any query runs.
        Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
    }

    [Fact]
    public async Task PointAtSea_Returns404RatherThanTheNearestPrefecture()
    {
        // Roughly 400 km east of Honshū, in the Pacific.
        using var response = await fixture.GetAsync("/api/prefectures/at?lat=38.0&lng=146.0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PointOnTheOtherSideOfTheWorld_Returns404()
    {
        // São Paulo. Inside the valid range for both parameters, outside every polygon.
        using var response = await fixture.GetAsync("/api/prefectures/at?lat=-23.55&lng=-46.63");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("lat=91&lng=139")]
    [InlineData("lat=-91&lng=139")]
    [InlineData("lat=35&lng=181")]
    [InlineData("lat=35&lng=-181")]
    [InlineData("lng=139")]
    [InlineData("lat=35")]
    [InlineData("")]
    public async Task OutOfRangeOrMissingCoordinates_AreRejected(string query)
    {
        using var response = await fixture.GetAsync($"/api/prefectures/at?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The lookup has to use the GIST index rather than scanning all 47 polygons. The
    /// index is what makes ST_Contains cheap, and it is invisible in the response —
    /// so the only way to test it is to ask the database how it ran the query.
    /// </summary>
    [Fact]
    public async Task Lookup_UsesTheGistIndex()
    {
        await using var db = fixture.CreateDbContext();

        // Raw SQL because EXPLAIN has no LINQ equivalent — there is no entity to
        // query and nothing to translate. This asks PostgreSQL what plan it chose
        // for the same predicate the endpoint issues.
        var plan = await db.Database
            .SqlQuery<string>($"""
                EXPLAIN SELECT p."Id" FROM prefectures p
                WHERE ST_Contains(p."Boundary", ST_SetSRID(ST_Point(139.767125, 35.681236), 4326))
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        var explained = string.Join('\n', plan);

        Assert.Contains("ix_prefectures_boundary_gist", explained, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan", explained, StringComparison.Ordinal);
    }
}

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YuruChara.Api.Tests.TestSupport;

namespace YuruChara.Api.Tests;

/// <summary>
/// <c>GET /api/prefectures</c> — the GeoJSON FeatureCollection the map is drawn
/// from.
/// </summary>
public class PrefectureBoundariesTests(PostGisApiFixture fixture)
{
    [Fact]
    public async Task Boundaries_AreAGeoJsonFeatureCollectionOfAll47Prefectures()
    {
        using var response = await fixture.GetAsync("/api/prefectures?detail=high");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // GeoJSON's registered media type, not application/json. RFC 7946 §12.
        Assert.Equal("application/geo+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(PostGisApiFixture.Cancellation));
        var root = document.RootElement;

        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());

        var features = root.GetProperty("features");
        Assert.Equal(47, features.GetArrayLength());

        foreach (var feature in features.EnumerateArray())
        {
            Assert.Equal("Feature", feature.GetProperty("type").GetString());

            // Always a MultiPolygon, never a Polygon. Even mainland prefectures have
            // offshore islands, and a consumer that switched on the geometry type
            // would otherwise have to handle both. See Prefecture.Boundary.
            Assert.Equal("MultiPolygon", feature.GetProperty("geometry").GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task EachFeature_CarriesTheJisCodeNamesAndLabelPoint()
    {
        var features = await GetFeaturesAsync("high");

        // Tokyo. Checked by JIS code rather than by array position, for the same
        // reason the seeder joins the boundary file by name — see DECISIONS.md 17.
        var tokyo = features.Single(feature => feature.GetProperty("properties").GetProperty("jisCode").GetInt32() == 13);
        var properties = tokyo.GetProperty("properties");

        Assert.Equal("Tokyo", properties.GetProperty("nameEn").GetString());
        Assert.Equal("東京都", properties.GetProperty("nameJa").GetString());
        Assert.Equal("Tōkyō", properties.GetProperty("nameRomaji").GetString());

        // The ASCII enum name for keying, and the properly spelled form for display.
        Assert.Equal("Kanto", properties.GetProperty("region").GetString());
        Assert.Equal("Kantō", properties.GetProperty("regionLabel").GetString());

        // Every prefecture must carry a label point, because that is what the map
        // draws its name at. A null here means the seeder's ST_PointOnSurface pass
        // did not run.
        foreach (var feature in features)
        {
            var name = feature.GetProperty("properties").GetProperty("nameEn").GetString();

            Assert.False(
                feature.GetProperty("properties").GetProperty("labelLat").ValueKind is JsonValueKind.Null,
                $"{name} has no labelLat, so its name cannot be placed on the map.");
        }
    }

    /// <summary>
    /// The stored label points are <c>ST_PointOnSurface</c>, not <c>ST_Centroid</c>,
    /// and this is the property that distinguishes them: a point on the surface is
    /// always inside the polygon, and a centroid is not. Four of Japan's 47 centroids
    /// fall in the sea. See DECISIONS.md 5.
    /// </summary>
    [Fact]
    public async Task EveryLabelPoint_IsInsideItsOwnPrefecture()
    {
        await using var db = fixture.CreateDbContext();

        var outside = await db.Prefectures
            .Where(prefecture => prefecture.LabelPoint != null
                                 && !prefecture.Boundary.Contains(prefecture.LabelPoint))
            .Select(prefecture => prefecture.NameEn)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(outside);
    }

    /// <summary>
    /// The point of <c>?detail=</c>: low must be materially smaller than high. If
    /// ST_Simplify silently stopped being applied — the DbFunction mapping is the
    /// fragile part — both would come back at raw size and every other test here
    /// would still pass.
    /// </summary>
    [Fact]
    public async Task LowDetail_IsSubstantiallySmallerThanHigh()
    {
        var high = await GetPayloadSizeAsync("high");
        var low = await GetPayloadSizeAsync("low");

        Assert.True(low < high / 2,
            $"Expected low detail to be less than half of high. low={low:N0} bytes, high={high:N0} bytes.");
    }

    /// <summary>
    /// Low detail drops whole rings, which is how it gets small: ST_Simplify removes
    /// a ring that collapses rather than keeping a degenerate one. That is the
    /// behaviour ST_SimplifyPreserveTopology would not have, and the reason it is not
    /// used here. See PostGis.Simplify.
    /// </summary>
    [Fact]
    public async Task LowDetail_DropsTheSmallIslands()
    {
        var highRings = await CountPolygonsAsync("high");
        var lowRings = await CountPolygonsAsync("low");

        Assert.True(lowRings < highRings,
            $"Expected low detail to contain fewer polygons than high. low={lowRings}, high={highRings}.");
    }

    [Fact]
    public async Task NoDetailParameter_IsTreatedAsHigh()
    {
        var withoutParameter = await GetPayloadSizeAsync(detail: null);
        var high = await GetPayloadSizeAsync("high");

        Assert.Equal(high, withoutParameter);
    }

    [Fact]
    public async Task UnrecognisedDetailLevel_IsRejectedRatherThanIgnored()
    {
        using var response = await fixture.GetAsync("/api/prefectures?detail=medium");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The message has to name the acceptable values, because the failure it is
        // most likely to describe is a caller who guessed at the vocabulary.
        var body = await response.Content.ReadAsStringAsync(PostGisApiFixture.Cancellation);
        Assert.Contains("'low' or 'high'", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LOW")]
    [InlineData("High")]
    public async Task DetailLevel_IsCaseInsensitive(string detail)
    {
        using var response = await fixture.GetAsync($"/api/prefectures?detail={detail}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement[]> GetFeaturesAsync(string detail)
    {
        var json = await fixture.GetStringAsync($"/api/prefectures?detail={detail}");

        // Cloned because the JsonDocument that owns these elements is pooled, and the
        // elements stop being readable once it is disposed.
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("features").EnumerateArray().Select(feature => feature.Clone())];
    }

    private async Task<int> CountPolygonsAsync(string detail)
    {
        var features = await GetFeaturesAsync(detail);

        return features.Sum(feature => feature.GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
    }

    private async Task<long> GetPayloadSizeAsync(string? detail)
    {
        var path = detail is null ? "/api/prefectures" : $"/api/prefectures?detail={detail}";

        var bytes = await fixture.GetBytesAsync(path);
        return bytes.LongLength;
    }
}

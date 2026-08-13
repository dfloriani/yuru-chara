using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YuruChara.Api.Prefectures;
using YuruChara.Api.Tests.TestSupport;
using YuruChara.Infrastructure;

namespace YuruChara.Api.Tests;

/// <summary>
/// Reports what <c>/api/prefectures</c> actually costs at each detail level, and
/// asserts the two served levels stay within budget.
/// <para>
/// <b>Why raw is measured here and not served.</b> The API offers <c>low</c> and
/// <c>high</c> and nothing else, so there is no request that returns unsimplified
/// geometry — but the raw figure is the one that makes the other two mean anything.
/// It is produced by calling the endpoint's own query with no tolerance and
/// serialising the result with the host's own JSON options, so all three numbers
/// come from the same code path and the same writer and are honestly comparable.
/// </para>
/// </summary>
public class PayloadSizeTests(PostGisApiFixture fixture)
{
    /// <summary>
    /// Ceilings, not measurements. They are set well above the current figures so
    /// that ordinary variation does not fail the build, and low enough that losing
    /// simplification altogether — the realistic regression, since the ST_Simplify
    /// mapping is the fragile part — does.
    /// </summary>
    private const long HighDetailBudgetBytes = 900_000;
    private const long LowDetailBudgetBytes = 250_000;

    [Fact]
    public async Task PayloadSizes_AreReportedAndWithinBudget()
    {
        var raw = await MeasureRawAsync();
        var high = await MeasureServedAsync("high");
        var low = await MeasureServedAsync("low");

        // Written to the test output so `dotnet test` reports the three numbers
        // rather than only whether they passed. The point of this test is as much
        // the figures as the assertions.
        TestContext.Current.TestOutputHelper?.WriteLine(Report(raw, high, low));

        Assert.True(high.Bytes < HighDetailBudgetBytes,
            $"detail=high is {high.Bytes:N0} bytes, over the {HighDetailBudgetBytes:N0} budget.");
        Assert.True(low.Bytes < LowDetailBudgetBytes,
            $"detail=low is {low.Bytes:N0} bytes, over the {LowDetailBudgetBytes:N0} budget.");

        // Both served levels must be real reductions on raw, or ST_Simplify is not
        // being applied.
        Assert.True(high.Bytes < raw.Bytes);
        Assert.True(low.Bytes < high.Bytes);
    }

    /// <summary>
    /// The unsimplified FeatureCollection: the same query the endpoint runs, with no
    /// tolerance, serialised through the host's configured JsonSerializerOptions —
    /// which is where GeoJSON4STJ is registered.
    /// </summary>
    private async Task<Measurement> MeasureRawAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<YuruCharaDbContext>();
        var jsonOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        var collection = await PrefectureEndpoints.BuildFeatureCollectionAsync(
            db, toleranceInDegrees: null, TestContext.Current.CancellationToken);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(collection, jsonOptions);

        return Measure(bytes);
    }

    private async Task<Measurement> MeasureServedAsync(string detail)
    {
        var bytes = await fixture.GetBytesAsync($"/api/prefectures?detail={detail}");

        return Measure(bytes);
    }

    /// <summary>
    /// Counts the coordinate pairs and polygon rings alongside the byte count.
    /// Vertices are the figure that explains the bytes, and the ring count is what
    /// shows ST_Simplify discarding small islands rather than merely thinning them.
    /// </summary>
    private static Measurement Measure(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);

        var vertices = 0;
        var rings = 0;

        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            // MultiPolygon coordinates nest as polygon -> ring -> position.
            foreach (var polygon in feature.GetProperty("geometry").GetProperty("coordinates").EnumerateArray())
            {
                rings++;

                foreach (var ring in polygon.EnumerateArray())
                {
                    vertices += ring.GetArrayLength();
                }
            }
        }

        return new Measurement(payload.LongLength, vertices, rings);
    }

    private static string Report(Measurement raw, Measurement high, Measurement low)
    {
        var report = new System.Text.StringBuilder()
            .AppendLine()
            .AppendLine("GET /api/prefectures — payload size by detail level")
            .AppendLine()
            .AppendLine("  level  tolerance        bytes        KB   vertices   polygons   % of raw")
            .AppendLine("  -----  ---------  -----------  --------  ---------  ---------  ---------");

        Append(report, "raw", null, raw, raw);
        Append(report, "high", DetailLevel.High.Tolerance(), high, raw);
        Append(report, "low", DetailLevel.Low.Tolerance(), low, raw);

        return report.ToString();
    }

    private static void Append(
        System.Text.StringBuilder report, string level, double? tolerance, Measurement value, Measurement raw)
    {
        var culture = CultureInfo.InvariantCulture;

        report.AppendLine(string.Create(culture,
            $"  {level,-5}  {(tolerance is null ? "—" : tolerance.Value.ToString("0.###°", culture)),9}  " +
            $"{value.Bytes,11:N0}  {value.Bytes / 1024.0,8:N0}  {value.Vertices,9:N0}  {value.Rings,9:N0}  " +
            $"{100.0 * value.Bytes / raw.Bytes,8:N1}%"));
    }

    private sealed record Measurement(long Bytes, int Vertices, int Rings);
}

using System.Net;
using System.Text.Json;
using YuruChara.Api.Tests.TestSupport;

namespace YuruChara.Api.Tests;

/// <summary>
/// <c>GET /api/prefectures/{id}</c> — what the detail panel is built from.
/// </summary>
public class PrefectureDetailTests(PostGisApiFixture fixture)
{
    /// <summary>
    /// Kumamoto, because Kumamon is the one record in this dataset whose every field
    /// was checked against the owning body's own site. If the mapping drops a field,
    /// this is the row where it shows.
    /// </summary>
    [Fact]
    public async Task Detail_ReturnsThePrefectureAndItsMascots()
    {
        using var response = await fixture.GetAsync("/api/prefectures/43");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(PostGisApiFixture.Cancellation));
        var root = document.RootElement;

        Assert.Equal(43, root.GetProperty("id").GetInt32());
        Assert.Equal("Kumamoto", root.GetProperty("nameEn").GetString());
        Assert.Equal("熊本県", root.GetProperty("nameJa").GetString());
        Assert.Equal("Kyushu", root.GetProperty("region").GetString());
        Assert.Equal("Kyūshū", root.GetProperty("regionLabel").GetString());

        var mascot = root.GetProperty("mascots").EnumerateArray().Single();

        Assert.Equal("くまモン", mascot.GetProperty("nameJa").GetString());
        Assert.Equal("Kumamon", mascot.GetProperty("nameRomaji").GetString());
        Assert.Equal(2011, mascot.GetProperty("debutYear").GetInt32());
        Assert.True(mascot.GetProperty("isOfficial").GetBoolean());

        // Enums are written as names, not numbers. "ApplicationRequired" is the fact
        // that matters about Kumamon's licence and "1" is not, and a name also means
        // inserting an enum member is not a breaking change on the wire.
        Assert.Equal("ApplicationRequired", mascot.GetProperty("imageLicenseStatus").GetString());
        Assert.Equal("ManuallyVerified", mascot.GetProperty("verificationLevel").GetString());

        // Per-field provenance, which is the point of the citations. See DECISIONS.md 19.
        var citedFields = mascot.GetProperty("sourceCitations")
            .EnumerateArray()
            .Select(citation => citation.GetProperty("field").GetString())
            .ToList();

        Assert.Contains("DebutYear", citedFields);
        Assert.Contains("Motif", citedFields);
    }

    /// <summary>
    /// The detail response deliberately carries no geometry: the caller drew the map
    /// from <c>GET /api/prefectures</c> and already holds the polygon. Asserting the
    /// absence is worth a test, because adding a boundary field back would look like
    /// a convenience and would quietly make this the largest response in the API.
    /// </summary>
    [Fact]
    public async Task Detail_DoesNotRepeatTheGeometry()
    {
        var json = await fixture.GetStringAsync("/api/prefectures/1");

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        Assert.DoesNotContain("boundary", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("geometry", names, StringComparer.OrdinalIgnoreCase);

        // Hokkaidō is the largest boundary in the set; its detail response is a few
        // hundred bytes.
        Assert.True(json.Length < 2_000, $"Expected a small response, got {json.Length:N0} characters.");
    }

    /// <summary>
    /// A prefecture with no verified mascot returns an empty list and a 200, not a
    /// 404 and not an invented record. 14 of the 47 are in this state. See CLAUDE.md,
    /// "Do not invent mascot data".
    /// </summary>
    [Fact]
    public async Task PrefectureWithNoVerifiedMascot_ReturnsAnEmptyListNotAnError()
    {
        // Tokyo has no prefecture-level mascot in the committed seed.
        using var response = await fixture.GetAsync("/api/prefectures/13");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(PostGisApiFixture.Cancellation));

        Assert.Equal("Tokyo", document.RootElement.GetProperty("nameEn").GetString());
        Assert.Empty(document.RootElement.GetProperty("mascots").EnumerateArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    [InlineData(999)]
    public async Task IdOutsideTheJisRange_Returns404(int id)
    {
        using var response = await fixture.GetAsync($"/api/prefectures/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The <c>:int</c> route constraint, which is the only thing this request's status
    /// code depends on. With the constraint the segment matches no route and the answer
    /// is 404. Without it the segment binds to the <c>int id</c> parameter, binding
    /// fails, and the answer is 400 — a message about the framework rather than about
    /// the prefecture that does not exist.
    /// </summary>
    [Fact]
    public async Task NonIntegerId_Returns404()
    {
        using var response = await fixture.GetAsync("/api/prefectures/notanumber");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// All 47 JIS codes resolve. This is coverage rather than behaviour, and it is
    /// worth having because a gap would otherwise only appear as a blank detail panel
    /// for one prefecture.
    /// </summary>
    [Fact]
    public async Task AllFortySevenJisCodes_Resolve()
    {
        var missing = new List<int>();

        for (var id = 1; id <= 47; id++)
        {
            using var response = await fixture.GetAsync($"/api/prefectures/{id}");

            if (response.StatusCode is not HttpStatusCode.OK)
            {
                missing.Add(id);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task SeededData_MatchesWhatTheSeederReported()
    {
        using var document = JsonDocument.Parse(await fixture.GetStringAsync("/api/mascots"));
        var mascots = document.RootElement;

        // Coverage and confidence are separate numbers, and the API has to be able to
        // report both. See DECISIONS.md 6.
        Assert.Equal(fixture.SeedOutcome.Mascots, mascots.GetArrayLength());

        var verified = mascots.EnumerateArray()
            .Count(mascot => mascot.GetProperty("verificationLevel").GetString() == "ManuallyVerified");

        Assert.Equal(fixture.SeedOutcome.ManuallyVerified, verified);
    }
}

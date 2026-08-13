using System.Net;
using System.Text.Json;
using YuruChara.Api.Tests.TestSupport;

namespace YuruChara.Api.Tests;

/// <summary>
/// <c>GET /api/mascots</c> and <c>GET /api/mascots/{id}</c> — the flat list behind
/// the searchable list view, and one record.
/// </summary>
public class MascotEndpointTests(PostGisApiFixture fixture)
{
    [Fact]
    public async Task List_ReturnsEveryMascotWithItsPrefecture()
    {
        var mascots = await GetMascotsAsync("/api/mascots");

        Assert.Equal(fixture.SeedOutcome.Mascots, mascots.Length);

        foreach (var mascot in mascots)
        {
            var prefectureId = mascot.GetProperty("prefectureId").GetInt32();

            Assert.InRange(prefectureId, 1, 47);

            // Denormalised onto the mascot so the list view needs one request rather
            // than one per row. An empty string here would mean the projection that
            // avoids loading the prefecture's geometry stopped joining.
            Assert.False(
                string.IsNullOrEmpty(mascot.GetProperty("prefectureNameEn").GetString()),
                $"Mascot {mascot.GetProperty("nameJa").GetString()} has no prefecture name.");
        }
    }

    [Fact]
    public async Task List_CarriesNoImageField()
    {
        // A hard rule of the project rather than a gap: mascot designs are
        // copyrighted, so licence status is modelled as data and no image is served.
        // A test because the field would be an easy thing to add without checking
        // terms. See CLAUDE.md, "Image licensing".
        var json = await fixture.GetStringAsync("/api/mascots");

        Assert.DoesNotContain("imageUrl", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"image\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thumbnail", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MotifFilter_MatchesSubstringsAndIgnoresCase()
    {
        var lower = await GetMascotsAsync("/api/mascots?motif=bear");
        var upper = await GetMascotsAsync("/api/mascots?motif=BEAR");

        Assert.NotEmpty(lower);
        Assert.Equal(lower.Length, upper.Length);

        foreach (var mascot in lower)
        {
            Assert.Contains("bear", mascot.GetProperty("motif").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A motif containing a LIKE wildcard must be matched literally. Without escaping,
    /// <c>?motif=%</c> would match every mascot that has any motif at all, which is
    /// the sort of thing that looks like a working search until someone types a
    /// percent sign.
    /// </summary>
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task MotifFilter_TreatsLikeWildcardsAsLiteralText(string wildcard)
    {
        var everything = await GetMascotsAsync("/api/mascots");
        var matches = await GetMascotsAsync($"/api/mascots?motif={Uri.EscapeDataString(wildcard)}");

        Assert.True(matches.Length < everything.Length,
            $"'{wildcard}' was treated as a wildcard: it matched {matches.Length} of {everything.Length} mascots.");
    }

    [Fact]
    public async Task DebutBeforeFilter_IsExclusiveAndExcludesUnknownYears()
    {
        var mascots = await GetMascotsAsync("/api/mascots?debutBefore=2000");

        Assert.NotEmpty(mascots);

        foreach (var mascot in mascots)
        {
            var debutYear = mascot.GetProperty("debutYear");

            // An unrecorded debut year is not "before 2000". Including nulls here
            // would be asserting something the seed data does not know.
            Assert.NotEqual(JsonValueKind.Null, debutYear.ValueKind);
            Assert.True(debutYear.GetInt32() < 2000);
        }
    }

    [Fact]
    public async Task Filters_Combine()
    {
        var mascots = await GetMascotsAsync("/api/mascots?motif=bear&debutBefore=2015");

        foreach (var mascot in mascots)
        {
            Assert.Contains("bear", mascot.GetProperty("motif").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.True(mascot.GetProperty("debutYear").GetInt32() < 2015);
        }
    }

    [Fact]
    public async Task SingleMascot_IsFetchableByTheGuidFromTheSeedFile()
    {
        var first = (await GetMascotsAsync("/api/mascots"))[0];
        var id = first.GetProperty("id").GetGuid();

        using var response = await fixture.GetAsync($"/api/mascots/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(PostGisApiFixture.Cancellation));

        Assert.Equal(id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(first.GetProperty("nameJa").GetString(), document.RootElement.GetProperty("nameJa").GetString());
    }

    [Fact]
    public async Task UnknownMascotId_Returns404()
    {
        using var response = await fixture.GetAsync($"/api/mascots/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonGuidMascotId_Returns404RatherThanBinding()
    {
        // The :guid route constraint means this matches no route at all.
        using var response = await fixture.GetAsync("/api/mascots/kumamon");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<JsonElement[]> GetMascotsAsync(string path)
    {
        var json = await fixture.GetStringAsync(path);

        // Cloned because the elements stop being readable once the pooled
        // JsonDocument that owns them is disposed.
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(mascot => mascot.Clone())];
    }
}

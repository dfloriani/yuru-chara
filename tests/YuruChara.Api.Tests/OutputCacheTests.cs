using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using YuruChara.Api.Prefectures;
using YuruChara.Api.Tests.TestSupport;

namespace YuruChara.Api.Tests;

/// <summary>
/// Output caching on <c>/api/prefectures</c>.
/// <para>
/// A cache hit is normally invisible to the caller, which is what makes this awkward
/// to test — the body is identical either way. It turns out to be observable through
/// two standard headers: on a hit the middleware replays the <c>Date</c> it stored
/// with the response and serves an <c>Age</c> counting the seconds since. So a
/// frozen <c>Date</c> plus a present <c>Age</c> is a hit, and no <c>Age</c> at all is
/// a miss.
/// </para>
/// <para>
/// These tests evict by tag before measuring, because they need to start from a
/// known-cold cache and every other test class in this assembly warms it. That is
/// also the only exercise <see cref="PrefectureEndpoints.BoundariesCacheTag"/> gets
/// — see DECISIONS.md 14 for why it exists.
/// </para>
/// </summary>
public class OutputCacheTests(PostGisApiFixture fixture)
{
    [Fact]
    public async Task SecondRequest_IsServedFromTheCache()
    {
        await EvictAsync();

        using var first = await fixture.GetAsync("/api/prefectures?detail=low");
        using var second = await fixture.GetAsync("/api/prefectures?detail=low");

        // A miss is served fresh, so there is nothing for it to be an age since.
        Assert.Null(first.Headers.Age);

        // A hit replays the stored response, and the middleware reports how old it is.
        Assert.NotNull(second.Headers.Age);
        Assert.Equal(first.Headers.Date, second.Headers.Date);

        Assert.Equal(
            await first.Content.ReadAsByteArrayAsync(PostGisApiFixture.Cancellation),
            await second.Content.ReadAsByteArrayAsync(PostGisApiFixture.Cancellation));
    }

    /// <summary>
    /// The failure output caching would introduce if <c>SetVaryByQuery("detail")</c>
    /// were missing: one cache entry for the endpoint, so whichever detail level was
    /// requested first would be served to everyone. A phone asking for <c>low</c>
    /// would get a desktop's <c>high</c> — several times the payload, which is the
    /// whole thing the parameter exists to avoid.
    /// </summary>
    [Fact]
    public async Task CacheEntries_AreSeparatePerDetailLevel()
    {
        await EvictAsync();

        var low = await fixture.GetBytesAsync("/api/prefectures?detail=low");
        var high = await fixture.GetBytesAsync("/api/prefectures?detail=high");

        Assert.True(low.Length < high.Length,
            $"low={low.LongLength:N0} bytes, high={high.LongLength:N0} bytes — the cache served one for the other.");

        // And still correct when the order is reversed against a warm cache.
        var lowAgain = await fixture.GetBytesAsync("/api/prefectures?detail=low");

        Assert.Equal(low, lowAgain);
    }

    /// <summary>
    /// The cache varies by <c>detail</c> and by nothing else, so an unrecognised
    /// query parameter does not create an entry. Worth pinning: a policy that varied
    /// by the whole query string would let any caller fill the cache with unbounded
    /// distinct keys just by appending a counter.
    /// </summary>
    [Fact]
    public async Task UnknownQueryParameters_DoNotCreateSeparateEntries()
    {
        await EvictAsync();

        using var first = await fixture.GetAsync("/api/prefectures?detail=low");
        using var withNoise = await fixture.GetAsync("/api/prefectures?detail=low&cacheBuster=12345");

        Assert.Null(first.Headers.Age);
        Assert.NotNull(withNoise.Headers.Age);
    }

    [Fact]
    public async Task Eviction_ByTagDropsTheStoredResponses()
    {
        await EvictAsync();

        using var miss = await fixture.GetAsync("/api/prefectures?detail=low");
        using var hit = await fixture.GetAsync("/api/prefectures?detail=low");

        await EvictAsync();

        using var afterEviction = await fixture.GetAsync("/api/prefectures?detail=low");

        Assert.Null(miss.Headers.Age);
        Assert.NotNull(hit.Headers.Age);
        Assert.Null(afterEviction.Headers.Age);
    }

    /// <summary>
    /// Empties the cache through the tag the boundaries policy applies. This is the
    /// mechanism a v2 ingestion endpoint would use to publish new boundaries without
    /// a restart; nothing in v1 calls it outside these tests, and that is stated in
    /// DECISIONS.md 14 rather than implied.
    /// </summary>
    private async Task EvictAsync()
    {
        var store = fixture.Factory.Services.GetRequiredService<IOutputCacheStore>();

        await store.EvictByTagAsync(
            PrefectureEndpoints.BoundariesCacheTag, TestContext.Current.CancellationToken);
    }
}

namespace YuruChara.Api.Caching;

/// <summary>
/// The <c>CDN-Cache-Control</c> header, which tells a CDN in front of this API how
/// long it may serve a stored copy of a response.
/// </summary>
public static class CdnCache
{
    /// <summary>
    /// Fresh for one hour, matching the output-cache expiry in Program.cs. The two
    /// answer different callers — the CDN and this process — and a re-seed shows up
    /// after whichever is longer, so they are kept equal.
    /// <para>
    /// <c>stale-while-revalidate</c>: for one week after the hour ends, the CDN
    /// answers with the stored copy at once and fetches a new copy from this app in
    /// the background. The request that finds an expired copy therefore does not
    /// wait for this app, which on the free hosting plan has usually stopped after a
    /// period with no requests and must start again. See DECISIONS.md 30.
    /// </para>
    /// </summary>
    public const string OneHourThenStaleForAWeek = "max-age=3600, stale-while-revalidate=604800";

    /// <summary>
    /// Declares how long a CDN may reuse this response.
    /// <para>
    /// <c>CDN-Cache-Control</c> rather than <c>Cache-Control</c>: only the CDN reads
    /// it, so browsers keep asking and a visitor never holds boundaries from a
    /// previous seed run. It is read by Vercel, which serves the frontend and
    /// forwards <c>/api/*</c> here; a request it answers from its own store costs
    /// this app no CPU time and no outbound data, which is what keeps the deployment
    /// inside the free daily quotas.
    /// </para>
    /// </summary>
    public static void SetCdnCacheControl(this HttpResponse response, string value) =>
        response.Headers["CDN-Cache-Control"] = value;
}

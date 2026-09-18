namespace YuruChara.Api.Caching;

/// <summary>
/// The <c>CDN-Cache-Control</c> header, which tells a CDN in front of this API how
/// long it may serve a stored copy of a response.
/// </summary>
public static class CdnCache
{
    /// <summary>
    /// One hour, matching the output-cache expiry in Program.cs. The two answer
    /// different callers — the CDN and this process — and a re-seed shows up after
    /// whichever is longer, so they are kept equal.
    /// </summary>
    public const string OneHour = "max-age=3600";

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

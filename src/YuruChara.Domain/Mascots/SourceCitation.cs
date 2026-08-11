namespace YuruChara.Domain.Mascots;

/// <summary>
/// Records where one fact about a mascot came from.
/// <para>
/// The seed data is stitched together from sources of very different reliability —
/// a prefecture's own press page, Wikidata, and a community wiki are not equally
/// trustworthy — so a citation is attached per field rather than per mascot.
/// Being able to answer "where did you get that debut year?" is part of the point
/// of the project.
/// </para>
/// </summary>
/// <param name="Field">
/// Name of the <see cref="Mascot"/> property this citation backs, e.g. "DebutYear".
/// A plain string rather than an enum: the set of cited fields changes more often
/// than the seed format, and a wrong value here is a data-quality problem, not a
/// crash.
/// </param>
/// <param name="SourceName">Human-readable source, e.g. "Kumamoto Prefecture — Kumamon official site".</param>
/// <param name="Url">Where the claim can be read.</param>
/// <param name="RetrievedOn">Date the page was actually read. Pages change; this dates the claim.</param>
/// <param name="Reliability">How much weight the source carries.</param>
public sealed record SourceCitation(
    string Field,
    string SourceName,
    Uri Url,
    DateOnly RetrievedOn,
    SourceReliability Reliability);

/// <summary>
/// How much a source is trusted. Ordered least to most reliable so it can be
/// compared, e.g. to pick the best citation for a field.
/// </summary>
public enum SourceReliability
{
    /// <summary>Community-maintained and unreviewed, e.g. the Fandom Yuru-chara wiki.</summary>
    Community = 0,

    /// <summary>Aggregated third-party data, e.g. Wikidata or Wikipedia.</summary>
    Aggregated = 1,

    /// <summary>Published by the body that owns the mascot.</summary>
    Official = 2
}

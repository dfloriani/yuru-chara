using System.Globalization;
using System.Reflection;
using System.Text.Json;
using YuruChara.Domain.Mascots;
using YuruChara.Ingestion.Seeding;

namespace YuruChara.Ingestion.Wikidata;

/// <summary>
/// The automated pass: runs <c>prefecture-mascots.rq</c> against the Wikidata Query
/// Service and writes a draft seed file plus a coverage report.
/// <para>
/// It writes to <c>data/raw/</c>, which is excluded by .gitignore, and never touches
/// <c>data/prefecture-mascots.json</c>. That separation is the point: the committed
/// seed contains hand-checked corrections and promotions to
/// <see cref="VerificationLevel.ManuallyVerified"/>, and a re-run of an automated
/// pass must not be able to silently overwrite them. Regenerating the draft and
/// diffing it against the committed file is the intended workflow.
/// </para>
/// </summary>
public sealed class WikidataMascotPass(HttpClient http)
{
    private const string Endpoint = "https://query.wikidata.org/sparql";

    /// <summary>Separators used by the GROUP_CONCATs in the query.</summary>
    private const string LinkSeparator = ",";
    private const string WebsiteSeparator = " | ";
    private const string ClassSeparator = "; ";

    /// <summary>
    /// Everything from this pass is <see cref="SourceReliability.Aggregated"/> and
    /// <see cref="VerificationLevel.Automated"/>. Coverage, not confidence — the two
    /// are separate fields for exactly this reason. See DECISIONS.md 6.
    /// </summary>
    private const string SourceName = "Wikidata Query Service (SPARQL)";

    /// <summary>
    /// The query text, read from the embedded copy of the .rq file, so the file in
    /// the repository is the query that runs. Editing the .rq and rebuilding is the
    /// only way to change it; there is no second copy in a string literal to fall
    /// out of step.
    /// </summary>
    public static string QueryText { get; } = ReadEmbeddedQuery();

    public async Task<WikidataPassResult> RunAsync(DateOnly retrievedOn, CancellationToken cancellationToken)
    {
        var response = await PostQueryAsync(cancellationToken);

        var mascots = new List<(int JisCode, SeedMascot Mascot)>();
        var skipped = new List<string>();

        foreach (var row in response.Results.Bindings)
        {
            var jisCode = int.Parse(row.Required("jisCode"), CultureInfo.InvariantCulture);
            var qid = row.Required("mascot").Split('/')[^1];

            // The Japanese label is OPTIONAL in the query for performance reasons
            // (see the note in the .rq file), so the guarantee that it exists has to
            // be enforced here instead. A mascot with no Japanese name has nothing to
            // identify it by and is reported rather than seeded.
            if (row.Value("mascotJa") is not { } nameJa)
            {
                skipped.Add($"{qid} (JIS {jisCode}): no Japanese label");
                continue;
            }

            var websites = row.Concatenated("officialWebsites", WebsiteSeparator);
            var debutYear = ReadDebutYear(row);

            var citations = new List<SeedCitation>();
            var citationUrl = $"http://www.wikidata.org/entity/{qid}";

            void Cite(string field) => citations.Add(new SeedCitation(
                field, SourceName, citationUrl, retrievedOn, SourceReliability.Aggregated));

            Cite(nameof(SeedMascot.NameJa));
            if (row.Value("mascotEn") is not null) Cite(nameof(SeedMascot.NameRomaji));
            if (debutYear is not null) Cite(nameof(SeedMascot.DebutYear));
            if (websites.Count > 0) Cite(nameof(SeedMascot.OfficialUrl));

            // IsOfficial is set from the link itself, not guessed. A P822 statement on
            // the prefecture is Wikidata asserting "this is that prefecture's mascot",
            // and a P6291 statement is the character asserting that it advertises the
            // prefecture. Both are claims of officialness at Aggregated reliability,
            // which is what the citation records.
            Cite(nameof(SeedMascot.IsOfficial));

            mascots.Add((jisCode, new SeedMascot(
                // A fresh Guid per draft run. The reviewer carries over the Guid
                // already in the committed file for a mascot that is already there,
                // so /api/mascots/{id} stays stable; a genuinely new mascot keeps the
                // new one. Deriving it from the QID was considered and rejected: it
                // would produce a different id for a record that has no QID, which is
                // exactly the hand-added case that most needs a stable one.
                Id: Guid.CreateVersion7(),
                NameJa: nameJa,
                NameRomaji: row.Value("mascotEn"),

                // Motif is deliberately left null by the automated pass. Wikidata's
                // P31 classes are the closest thing it has, and "anthropomorphic
                // bird" is a taxonomy of the character, not the thing the character
                // is a picture of. They are reported alongside for a human to read.
                Motif: null,

                DebutYear: debutYear,

                // Wikidata has no property that reliably gives the owning body for
                // these — P137 (operator) is unset on all 35 — so this stays null
                // rather than being inferred from the website's domain.
                OwningBody: null,

                // Where a mascot has several recorded websites, the first is taken and
                // the rest are reported. Kōchi's くろしおくん has three, which are the
                // Japanese, English and Chinese versions of one page.
                OfficialUrl: websites.Count > 0 ? websites[0] : null,

                IsOfficial: true,
                ImageLicenseStatus: ImageLicenseStatus.Unknown,
                LicenseNotes: null,
                VerificationLevel: VerificationLevel.Automated,
                WikidataId: qid,
                ReviewNotes: BuildAutomatedNote(row, websites),
                SourceCitations: citations)));
        }

        return new WikidataPassResult(
            Document: BuildDocument(retrievedOn, mascots),
            RowCount: response.Results.Bindings.Count,
            Skipped: skipped);
    }

    /// <summary>
    /// Builds a full 47-entry document. Every prefecture gets a slot, including the
    /// ones with no mascot, so the gaps are recorded in the file rather than being
    /// the absence of a record.
    /// </summary>
    private static SeedDocument BuildDocument(
        DateOnly retrievedOn,
        List<(int JisCode, SeedMascot Mascot)> mascots)
    {
        var byPrefecture = mascots
            .GroupBy(entry => entry.JisCode)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Mascot).ToList());

        var prefectures = JisPrefectures.All
            .Select(prefecture => new SeedPrefecture(
                prefecture.JisCode,
                prefecture.NameJa,
                byPrefecture.TryGetValue(prefecture.JisCode, out var found)
                    ? found.OrderBy(mascot => mascot.NameJa, StringComparer.Ordinal).ToList()
                    : []))
            .ToList();

        return new SeedDocument(
            new SeedMetadata(
                GeneratedOn: retrievedOn,
                WikidataQuery: "src/YuruChara.Ingestion/Wikidata/prefecture-mascots.rq",
                BoundaryFile: "data/prefectures.geojson",
                Notes:
                    "DRAFT, produced by `dotnet run --project src/YuruChara.Ingestion -- wikidata`. " +
                    "Every record is VerificationLevel.Automated and ImageLicenseStatus.Unknown. " +
                    "Do not commit this file as the seed: diff it against " +
                    "data/prefecture-mascots.json, which holds the hand-checked corrections."),
            prefectures);
    }

    /// <summary>
    /// Reads the inception year, honouring the precision the query asked for.
    /// <para>
    /// Wikidata stores "2010" and "12 March 2011" in the same field and distinguishes
    /// them only by <c>timePrecision</c>: 9 is year, 10 is month, 11 is day. Anything
    /// coarser than a year — 8 is a decade, 7 a century — cannot answer "what year",
    /// so it is dropped rather than rounded into one. Only the year is kept in either
    /// case, because <see cref="Mascot.DebutYear"/> is an <see cref="int"/>: a debut
    /// is an announcement, and the year is the part every source agrees on.
    /// </para>
    /// </summary>
    private static int? ReadDebutYear(Dictionary<string, SparqlValue> row)
    {
        if (row.Value("inception") is not { } inception) return null;

        if (row.Value("inceptionPrecision") is { } precisionText
            && int.TryParse(precisionText, CultureInfo.InvariantCulture, out var precision)
            && precision < 9)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            inception, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.Year
            : null;
    }

    /// <summary>
    /// Records the things a reviewer needs and the seed schema has nowhere to put:
    /// which link direction found this mascot, what Wikidata thinks it is, how
    /// precise the date was, and any extra websites that were dropped.
    /// </summary>
    private static string BuildAutomatedNote(
        Dictionary<string, SparqlValue> row,
        IReadOnlyList<string> websites)
    {
        var parts = new List<string>
        {
            $"Wikidata link: {string.Join(" and ", row.Concatenated("links", LinkSeparator))}"
        };

        if (row.Concatenated("wikidataClasses", ClassSeparator) is { Count: > 0 } classes)
        {
            parts.Add($"P31 classes: {string.Join(", ", classes)}");
        }

        if (row.Value("inception") is { } inception)
        {
            var precision = row.Value("inceptionPrecision") switch
            {
                "11" => "day",
                "10" => "month",
                "9" => "year",
                var other => $"precision {other ?? "unknown"}"
            };
            parts.Add($"P571 inception: {inception} ({precision} precision)");
        }

        if (websites.Count > 1)
        {
            parts.Add($"Additional P856 values not used: {string.Join(", ", websites.Skip(1))}");
        }

        return string.Join(". ", parts) + ".";
    }

    private async Task<SparqlResponse> PostQueryAsync(CancellationToken cancellationToken)
    {
        // POST, not GET. The query is over 5 kB once its comments are included, which
        // is past what some proxies allow in a URL, and WDQS accepts a form-encoded
        // POST for exactly this case.
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", QueryText)])
        };
        request.Headers.Accept.ParseAdd("application/sparql-results+json");

        using var response = await http.SendAsync(request, cancellationToken);

        // WDQS answers a query that exceeds its 60-second limit with 504 rather than
        // an error document, so a failure here usually means the query got slower, not
        // that the service is down. Worth saying, because the difference decides
        // whether to retry or to go and look at the query plan.
        if (response.StatusCode is System.Net.HttpStatusCode.GatewayTimeout)
        {
            throw new InvalidOperationException(
                "Wikidata Query Service returned 504: the query exceeded its 60-second limit. " +
                "This is a query-plan problem rather than an outage — see the note about " +
                "constant-object property paths in prefecture-mascots.rq.");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SparqlResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Wikidata Query Service returned an empty response body.");
    }

    private static string ReadEmbeddedQuery()
    {
        // Namespace + folder + filename. Kept as one lookup with a clear failure
        // message, because a mistyped resource name otherwise surfaces as a null
        // stream much later.
        const string ResourceName = "YuruChara.Ingestion.Wikidata.prefecture-mascots.rq";

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <param name="Document">The draft seed document.</param>
/// <param name="RowCount">Rows the query returned, before any were skipped.</param>
/// <param name="Skipped">Rows that could not be turned into a record, and why.</param>
public sealed record WikidataPassResult(
    SeedDocument Document,
    int RowCount,
    IReadOnlyList<string> Skipped);

using System.Text.Json;
using System.Text.Json.Serialization;
using YuruChara.Domain.Mascots;

namespace YuruChara.Ingestion.Seeding;

/// <summary>
/// The shape of <c>data/prefecture-mascots.json</c>.
/// <para>
/// One model, used in both directions: the Wikidata pass writes a draft in this
/// shape, and the database seeder reads the reviewed file back. Sharing it means
/// the draft and the committed seed cannot drift into two formats, and a field
/// added here has to be handled at both ends before it compiles.
/// </para>
/// <para>
/// These are <c>record</c>s because they are DTOs, per the CLAUDE.md convention.
/// They are separate types from <see cref="Mascot"/> rather than serialising the
/// entity directly: the entity has a <c>Prefecture</c> navigation that would
/// recurse, and the file needs fields the entity does not have — the Wikidata QID
/// and the review notes.
/// </para>
/// </summary>
public sealed record SeedDocument(
    SeedMetadata Metadata,
    IReadOnlyList<SeedPrefecture> Prefectures)
{
    /// <summary>
    /// One shared serialiser configuration, so the draft the CLI writes is
    /// byte-comparable with the committed file and a diff shows real data changes
    /// rather than formatting churn.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,

        // Enums as their names. "ApplicationRequired" in a committed data file that
        // a person is expected to review is worth more than "1", and it does not
        // silently change meaning if a member is ever inserted into the enum.
        Converters = { new JsonStringEnumConverter() },

        // Default. Stated explicitly because it is load-bearing here: a null motif
        // means "not established from any source", which is a fact this file is
        // supposed to record, so nulls must be written rather than omitted.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Japanese text is most of this file. Without this, every kana character is
        // written as a \uXXXX escape, and the file becomes unreadable and
        // unreviewable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <param name="GeneratedOn">Date this file's automated pass was run.</param>
/// <param name="WikidataQuery">
/// Path to the query that produced the automated records, so the file names the
/// thing that can regenerate it.
/// </param>
/// <param name="BoundaryFile">Companion boundary file the seeder expects.</param>
/// <param name="Notes">Free text for whoever reads the file first.</param>
public sealed record SeedMetadata(
    DateOnly GeneratedOn,
    string WikidataQuery,
    string BoundaryFile,
    string Notes);

/// <summary>
/// One prefecture's slot in the seed file. All 47 are always present, including
/// the ones with no mascot.
/// </summary>
/// <param name="JisCode">JIS X 0401 code, 1–47.</param>
/// <param name="NameJa">
/// Japanese name. Present so the file is readable, and so it can be checked
/// against <see cref="JisPrefectures"/> — a mismatch means the file and the code
/// disagree about which prefecture a code refers to, which should fail loudly.
/// </param>
/// <param name="Mascots">
/// Empty list means no mascot could be established from a source. That is a real,
/// recorded state, not missing data: see CLAUDE.md, "Do not invent mascot data".
/// A list rather than a single value because Shiga and Ehime genuinely have two.
/// </param>
public sealed record SeedPrefecture(
    int JisCode,
    string NameJa,
    IReadOnlyList<SeedMascot> Mascots);

/// <param name="Id">
/// Stable <see cref="Guid"/>, generated once and committed. It is in the file
/// rather than generated at seed time so that <c>/api/mascots/{id}</c> URLs
/// survive re-seeding, and so that re-running the seeder against an existing
/// database updates rows instead of duplicating them.
/// </param>
/// <param name="WikidataId">
/// Q-number, or null for a record that did not come from Wikidata. Not part of
/// the domain model — it belongs to the seed pipeline, which is why it is on this
/// record and not on <see cref="Mascot"/>. It is what makes re-running the
/// automated pass able to tell "this is the same mascot" from "this is a new one".
/// </param>
/// <param name="ReviewNotes">
/// What a human checked and what they changed. Not loaded into the database — it
/// is a note to the next reader of the file, and the machine-readable version of
/// the same information is in <paramref name="SourceCitations"/>.
/// </param>
public sealed record SeedMascot(
    Guid Id,
    string NameJa,
    string? NameRomaji,
    string? Motif,
    int? DebutYear,
    string? OwningBody,
    string? OfficialUrl,
    bool IsOfficial,
    ImageLicenseStatus ImageLicenseStatus,
    string? LicenseNotes,
    VerificationLevel VerificationLevel,
    string? WikidataId,
    string? ReviewNotes,
    IReadOnlyList<SeedCitation> SourceCitations);

/// <summary>
/// Flat mirror of <see cref="SourceCitation"/>. It exists as its own type so the
/// file format is not pinned to the domain record: <c>Url</c> is a string here so
/// that a malformed URL produces a clear "bad citation in the seed file" error
/// rather than a <see cref="JsonException"/> from deep inside the deserialiser.
/// </summary>
public sealed record SeedCitation(
    string Field,
    string SourceName,
    string Url,
    DateOnly RetrievedOn,
    SourceReliability Reliability);

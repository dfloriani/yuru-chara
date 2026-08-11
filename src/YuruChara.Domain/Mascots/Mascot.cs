using YuruChara.Domain.Prefectures;

namespace YuruChara.Domain.Mascots;

/// <summary>
/// A gotōchi-chara belonging to one prefecture.
/// <para>
/// Almost every descriptive field is nullable on purpose. Where a fact could not
/// be verified from a real source it is left null rather than filled in from
/// memory — a visibly incomplete map is fine, a confidently wrong one is not.
/// </para>
/// </summary>
public class Mascot
{
    public Guid Id { get; init; }

    public int PrefectureId { get; set; }

    /// <summary>Navigation back to the owning prefecture. Null until loaded.</summary>
    public Prefecture? Prefecture { get; set; }

    /// <summary>Japanese name, usually kana. Required, because a record for a mascot whose name is unknown would have nothing to identify it by.</summary>
    public required string NameJa { get; set; }

    /// <summary>Romanised name. Nullable — not every source supplies one, and we do not invent transliterations.</summary>
    public string? NameRomaji { get; set; }

    /// <summary>Free text, e.g. "pear", "bear", "samurai helmet". Deliberately not an enum — the real values are too varied.</summary>
    public string? Motif { get; set; }

    public int? DebutYear { get; set; }

    /// <summary>Prefectural government, tourism board, and so on.</summary>
    public string? OwningBody { get; set; }

    public Uri? OfficialUrl { get; set; }

    /// <summary>Some prefectures have a popular unofficial mascot alongside the official one.</summary>
    public bool IsOfficial { get; set; }

    public ImageLicenseStatus ImageLicenseStatus { get; set; } = ImageLicenseStatus.Unknown;

    /// <summary>Free text, typically a link to the terms page the status was read from.</summary>
    public string? LicenseNotes { get; set; }

    public VerificationLevel VerificationLevel { get; set; } = VerificationLevel.Automated;

    /// <summary>Per-field provenance. See <see cref="SourceCitation"/>.</summary>
    public IList<SourceCitation> SourceCitations { get; set; } = [];
}

namespace YuruChara.Domain.Mascots;

/// <summary>
/// Whether an image of this mascot could lawfully be shown, modelled as data.
/// <para>
/// This project ships no mascot images at all (see CLAUDE.md, "Image licensing").
/// The enum exists so the UI can *say* why there is no picture instead of leaving
/// a hole, and so that anyone revisiting the decision starts from researched facts
/// rather than guesswork. Mascot designs are copyrighted and trademarked by the
/// bodies that own them, and photographs of costumed performers are derivative
/// works of those designs.
/// </para>
/// </summary>
public enum ImageLicenseStatus
{
    /// <summary>Not yet researched. The default, and honest about it.</summary>
    Unknown = 0,

    /// <summary>The owning body requires a usage application before any reuse.</summary>
    ApplicationRequired,

    /// <summary>The owning body publishes downloadable assets together with terms.</summary>
    OfficialMaterialsPublished,

    /// <summary>A genuinely free-licensed photograph exists on Wikimedia Commons.</summary>
    CommonsFreeLicense,

    /// <summary>The terms explicitly forbid third-party reuse.</summary>
    NoReuseGranted
}

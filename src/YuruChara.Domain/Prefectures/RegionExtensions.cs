namespace YuruChara.Domain.Prefectures;

public static class RegionExtensions
{
    /// <summary>
    /// The display form of a region name, with the macrons the ASCII
    /// <see cref="Region"/> identifiers cannot carry: Hokkaidō, Tōhoku, Chūbu,
    /// Chūgoku, Kyūshū.
    /// <para>
    /// Kept here rather than in the API so that the enum and its display form
    /// stay in one place — the macron is part of the name, not a presentation
    /// choice, and the alternative is every consumer keeping its own table of
    /// five special cases. Responses carry both this and the plain enum name,
    /// so a client can group and key on the ASCII value and print this one.
    /// </para>
    /// <para>
    /// A switch expression rather than a <c>[Display]</c> attribute or a
    /// resource file: this is not localisation. There is one correct spelling
    /// of each name and no second language to switch to. A switch is also
    /// exhaustive at compile time, so adding a member to <see cref="Region"/>
    /// without a display form is caught here.
    /// </para>
    /// </summary>
    public static string ToDisplayName(this Region region) => region switch
    {
        Region.Hokkaido => "Hokkaidō",
        Region.Tohoku => "Tōhoku",
        Region.Kanto => "Kantō",
        Region.Chubu => "Chūbu",
        Region.Kansai => "Kansai",
        Region.Chugoku => "Chūgoku",
        Region.Shikoku => "Shikoku",
        Region.Kyushu => "Kyūshū",
        _ => throw new ArgumentOutOfRangeException(
            nameof(region), region, "No display name is defined for this region.")
    };
}

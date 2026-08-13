namespace YuruChara.Api.Tests.TestSupport;

/// <summary>
/// Locates the committed data files, which the fixture seeds into the test
/// container.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>
    /// Walks up from the test assembly's output directory until it finds the file
    /// that marks the repository root.
    /// <para>
    /// Tests run from <c>tests/YuruChara.Api.Tests/bin/&lt;config&gt;/net10.0/</c>, so a
    /// relative path such as <c>../../../../../data</c> would work — and would break
    /// the moment the configuration or target framework changed the depth. Searching
    /// for a marker file does not depend on that depth.
    /// </para>
    /// </summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The 47 prefecture boundaries. See DATA-SOURCES.md section 1.</summary>
    public static string BoundaryGeoJson => Path.Combine(Root, "data", "prefectures.geojson");

    /// <summary>The hand-curated mascot seed. See DECISIONS.md 18.</summary>
    public static string MascotSeed => Path.Combine(Root, "data", "prefecture-mascots.json");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "YuruChara.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find the repository root by walking up from '{AppContext.BaseDirectory}' " +
            "looking for YuruChara.slnx. The integration tests seed from the committed files in data/, " +
            "so they need to know where the working tree is.");
    }
}

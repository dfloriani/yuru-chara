using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace YuruChara.Ingestion.Seeding;

/// <summary>
/// Reads <c>data/prefectures.geojson</c> into NetTopologySuite geometry, keyed by
/// JIS code.
/// </summary>
public static class PrefectureBoundaryReader
{
    /// <summary>
    /// The property the source file carries the prefecture's Japanese name in.
    /// <c>N03_001</c> is the National Land Numerical Information field name for it;
    /// the "N03" prefix is the dataset code for 行政区域 (administrative divisions).
    /// It is the file's <em>only</em> property — there is no JIS code in it — which
    /// is why the join goes through <see cref="JisPrefectures.ByNameJa"/>.
    /// </summary>
    private const string NameProperty = "N03_001";

    /// <summary>SRID 4326 throughout. See DECISIONS.md 3.</summary>
    private const int Srid = 4326;

    /// <summary>
    /// Geometry factory fixed to SRID 4326. Constructed explicitly rather than using
    /// <c>GeometryFactory.Default</c>, whose SRID is 0: geometry read with SRID 0
    /// is rejected by the <c>geometry(MultiPolygon, 4326)</c> column at insert time,
    /// with an error that does not mention the factory.
    /// </summary>
    private static readonly GeometryFactory Factory =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(Srid);

    /// <summary>
    /// Built once and reused. <see cref="GeoJsonConverterFactory"/> teaches
    /// System.Text.Json to read GeoJSON into NTS types — the same package the API
    /// uses to write GeoJSON out, so the read and write paths agree on how geometry
    /// maps to JSON. Constructing the options per call would rebuild the converter
    /// cache on every read (CA1869).
    /// </summary>
    private static readonly JsonSerializerOptions GeoJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new GeoJsonConverterFactory(Factory) }
    };

    public static async Task<IReadOnlyDictionary<int, MultiPolygon>> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var collection =
            await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, GeoJson, cancellationToken)
            ?? throw new InvalidOperationException($"'{path}' did not deserialise to a GeoJSON FeatureCollection.");

        var boundaries = new Dictionary<int, MultiPolygon>();
        var unmatched = new List<string>();

        foreach (var feature in collection)
        {
            var nameJa = feature.Attributes.Exists(NameProperty)
                ? feature.Attributes[NameProperty]?.ToString()
                : null;

            if (nameJa is null || !JisPrefectures.ByNameJa.TryGetValue(nameJa, out var prefecture))
            {
                unmatched.Add(nameJa ?? "(no name property)");
                continue;
            }

            boundaries[prefecture.JisCode] = AsMultiPolygon(feature.Geometry, nameJa);
        }

        // Fail loudly rather than seeding a partial map. The join is by name, and a
        // renamed or re-encoded name in a future version of the source file would
        // otherwise silently drop a prefecture. The count check catches the same
        // problem from the other direction.
        if (unmatched.Count > 0)
        {
            throw new InvalidOperationException(
                $"{unmatched.Count} feature(s) in '{path}' did not match a known prefecture name: " +
                string.Join(", ", unmatched) +
                ". The boundary file and JisPrefectures disagree — see DATA-SOURCES.md, 'The name join'.");
        }

        if (boundaries.Count != JisPrefectures.All.Count)
        {
            throw new InvalidOperationException(
                $"'{path}' produced {boundaries.Count} boundaries; expected {JisPrefectures.All.Count}.");
        }

        return boundaries;
    }

    /// <summary>
    /// Promotes a <see cref="Polygon"/> to a single-member <see cref="MultiPolygon"/>.
    /// <para>
    /// The source file is mixed: 12 of the 47 features are <c>Polygon</c> and 35 are
    /// <c>MultiPolygon</c>. The domain models <c>Boundary</c> as a
    /// <see cref="MultiPolygon"/> unconditionally and the column type is
    /// <c>geometry(MultiPolygon, 4326)</c>, so the 12 have to be wrapped.
    /// </para>
    /// <para>
    /// Wrapping rather than relaxing the column to plain <c>geometry</c> is
    /// deliberate. Every query in this project treats a boundary as a set of rings;
    /// a column that could hold a Point or a LineString would push a type check into
    /// every one of them, and the wrap costs nothing — a one-member MultiPolygon is
    /// the same set of coordinates.
    /// </para>
    /// </summary>
    private static MultiPolygon AsMultiPolygon(Geometry geometry, string nameJa) => geometry switch
    {
        MultiPolygon multiPolygon => multiPolygon,
        Polygon polygon => Factory.CreateMultiPolygon([polygon]),
        _ => throw new InvalidOperationException(
            $"'{nameJa}' has geometry type {geometry.GeometryType}; expected Polygon or MultiPolygon.")
    };
}

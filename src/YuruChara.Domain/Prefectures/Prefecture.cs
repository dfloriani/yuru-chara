using NetTopologySuite.Geometries;
using YuruChara.Domain.Mascots;

namespace YuruChara.Domain.Prefectures;

/// <summary>
/// One of Japan's 47 prefectures, keyed by its JIS X 0401 code.
/// </summary>
public class Prefecture
{
    /// <summary>
    /// JIS X 0401 prefecture code, 1 (Hokkaidō) to 47 (Okinawa). Assigned by the
    /// standard, not by us, so it is set explicitly rather than generated — every
    /// Japanese open dataset keys on this, which is what makes joining them possible.
    /// </summary>
    public int Id { get; init; }

    public required string NameEn { get; set; }

    public required string NameJa { get; set; }

    public required string NameRomaji { get; set; }

    public Region Region { get; set; }

    /// <summary>
    /// Full-resolution prefecture outline in SRID 4326. Always a MultiPolygon:
    /// even mainland prefectures like Hiroshima have offshore islands, so a single
    /// Polygon would not fit them all and a mixed column type would be worse.
    /// This is kept at source resolution; simplification happens at read time.
    /// </summary>
    public required MultiPolygon Boundary { get; set; }

    /// <summary>
    /// ST_PointOnSurface of <see cref="Boundary"/>, computed once at seed time.
    /// <para>
    /// This is the position map labels are drawn at. It is always inside the
    /// polygon, which <see cref="Centroid"/> is not: the centroid of a concave
    /// shape, or of one made up of many islands, can fall outside the shape
    /// entirely. Several of Japan's prefectures are affected. Nagasaki's centroid
    /// falls in the water between its islands, and Tokyo's is several hundred
    /// kilometres south of the city because of the Ogasawara island chain.
    /// </para>
    /// </summary>
    public Point? LabelPoint { get; set; }

    /// <summary>
    /// ST_Centroid of <see cref="Boundary"/>, the centre of mass. Stored because it
    /// is the right answer for distance and "which is nearest" style questions, but
    /// it is <em>not</em> used for labels — see <see cref="LabelPoint"/>.
    /// </summary>
    public Point? Centroid { get; set; }

    /// <summary>
    /// Mascots owned by this prefecture. Empty is a real and expected state: it
    /// means no mascot could be verified from a source, not that data is missing
    /// by accident.
    /// </summary>
    public ICollection<Mascot> Mascots { get; } = [];
}

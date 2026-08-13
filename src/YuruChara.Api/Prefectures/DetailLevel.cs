namespace YuruChara.Api.Prefectures;

/// <summary>
/// How much boundary detail <c>GET /api/prefectures</c> should return, and the
/// <c>ST_Simplify</c> tolerance each level maps to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tolerances are in degrees</b>, because the geometry is SRID 4326.
/// Sizing them is a question about pixels, not about metres: simplification is
/// invisible while the tolerance stays below the size of a pixel at the zoom
/// the shape is drawn at. Across Japan's roughly 20° of longitude that gives
/// about 0.017°/pixel on a 1200-pixel desktop viewport and about 0.05°/pixel on
/// a 390-pixel phone, which is where these two values come from.
/// </para>
/// <para>
/// Measured on the committed geometry, over all 47 prefectures:
/// </para>
/// <list type="table">
///   <listheader><term>Level</term><description>tolerance / vertices / polygon rings</description></listheader>
///   <item><term>raw</term><description>— / 61,033 / 736</description></item>
///   <item><term>high</term><description>0.005° (~450 m) / 15,197 / 555</description></item>
///   <item><term>low</term><description>0.02° (~1.8 km) / 3,712 / 191</description></item>
/// </list>
/// <para>
/// The falling ring count is the point of <c>low</c>: <c>ST_Simplify</c> drops
/// rings that collapse, so the small islands go and Nagasaki and Okinawa stop
/// costing what they cost. That is a deliberate loss, not a defect — at phone
/// zoom those islands are smaller than a pixel.
/// </para>
/// </remarks>
public enum DetailLevel
{
    /// <summary>Phone-sized payload. Small islands are simplified away.</summary>
    Low,

    /// <summary>Desktop payload. Still simplified — the database keeps the full geometry.</summary>
    High
}

public static class DetailLevelExtensions
{
    /// <summary>
    /// The <c>ST_Simplify</c> tolerance for a level, in degrees. See
    /// <see cref="DetailLevel"/> for how these were chosen.
    /// </summary>
    public static double Tolerance(this DetailLevel level) => level switch
    {
        DetailLevel.Low => 0.02,
        DetailLevel.High => 0.005,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "No tolerance is defined for this detail level.")
    };

    /// <summary>
    /// Parses the <c>?detail=</c> query value.
    /// <para>
    /// A missing value means <see cref="DetailLevel.High"/>. Mobile-first is a
    /// design constraint of the frontend, but it is not a safe default for an
    /// API: the client is the only party that knows its viewport, and silently
    /// returning coarser geometry than a caller expected is a worse failure than
    /// returning finer. The frontend opts into <c>low</c> explicitly.
    /// </para>
    /// <para>
    /// An unrecognised value is rejected rather than falling back to the
    /// default, so <c>?detail=medium</c> is a 400 that names the mistake instead
    /// of a 200 that quietly ignores it.
    /// </para>
    /// </summary>
    public static bool TryParse(string? value, out DetailLevel level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = DetailLevel.High;
            return true;
        }

        // Case-insensitive, but only these two spellings. Enum.TryParse would also
        // accept "0", "1" and the member names in any casing, which is a wider
        // contract than the one documented in CLAUDE.md.
        switch (value.Trim().ToLowerInvariant())
        {
            case "low":
                level = DetailLevel.Low;
                return true;
            case "high":
                level = DetailLevel.High;
                return true;
            default:
                level = DetailLevel.High;
                return false;
        }
    }
}

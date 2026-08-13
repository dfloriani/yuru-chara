using NetTopologySuite.Geometries;

namespace YuruChara.Infrastructure;

/// <summary>
/// PostGIS functions that the Npgsql provider does not translate on its own,
/// exposed to LINQ as EF Core user-defined functions.
/// <para>
/// The Npgsql NetTopologySuite plugin translates a large part of PostGIS by
/// mapping NetTopologySuite's own members onto it — <c>Geometry.Contains</c>
/// becomes <c>ST_Contains</c>, <c>Geometry.Centroid</c> becomes
/// <c>ST_Centroid</c>, <c>Geometry.InteriorPoint</c> becomes
/// <c>ST_PointOnSurface</c>. That only works where NetTopologySuite has an
/// equivalent member to hang the translation on. <c>ST_Simplify</c> has none:
/// NetTopologySuite performs Douglas-Peucker simplification through a separate
/// <c>DouglasPeuckerSimplifier</c> class rather than a method on
/// <c>Geometry</c>, so there is no member for the plugin to map and no
/// <c>EF.Functions.Simplify</c> either.
/// </para>
/// <para>
/// The alternatives were raw SQL for the whole boundaries query, or pulling all
/// 47 full-resolution boundaries into memory and simplifying them client-side
/// with NetTopologySuite. Raw SQL would mean hand-writing the column list and
/// losing the mascot-count subquery; client-side simplification would transfer
/// the full 2.4 MB from the database on every cache miss in order to send a
/// fraction of it to the browser, and would put the work on the web server
/// instead of the database that is built for it. Declaring the function to EF
/// Core keeps the query as ordinary LINQ and lets PostGIS do the simplifying.
/// </para>
/// </summary>
public static class PostGis
{
    /// <summary>
    /// <c>ST_Simplify(geometry, tolerance)</c> — Douglas-Peucker simplification.
    /// Drops vertices that sit within <paramref name="tolerance"/> of the line
    /// between their neighbours.
    /// <para>
    /// <paramref name="tolerance"/> is in the units of the geometry's SRID, and
    /// this project stores SRID 4326, so the unit is <b>degrees</b>, not metres.
    /// See <c>YuruChara.Api.Prefectures.DetailLevel</c> for the two values used
    /// and what they work out to on the ground.
    /// </para>
    /// <para>
    /// Two behaviours of <c>ST_Simplify</c> matter here and are deliberate:
    /// it <b>removes</b> rings that become degenerate, which is what discards
    /// the hundreds of tiny islands off Nagasaki and Okinawa at low detail; and
    /// it can produce <b>self-intersecting</b> polygons, because it moves each
    /// ring independently without checking the result stays valid.
    /// <c>ST_SimplifyPreserveTopology</c> avoids both, which is why it is not
    /// used: keeping every island is the opposite of what a phone-sized payload
    /// needs. The output is for drawing, never for further spatial queries —
    /// those run against the full-resolution column.
    /// </para>
    /// <para>
    /// Returns <c>NULL</c> if the geometry collapses entirely. No prefecture
    /// does at the tolerances this project uses (checked up to 0.05°), and a
    /// null geometry would serialise as GeoJSON <c>"geometry": null</c>, which
    /// is valid, so there is no guard for it.
    /// </para>
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Always, if called outside a LINQ query. There is no client-side
    /// implementation on purpose: this method exists to be translated to SQL,
    /// and silently simplifying in .NET instead would hide the fact that the
    /// query fell back to client evaluation.
    /// </exception>
    public static Geometry Simplify(Geometry geometry, double tolerance) =>
        throw new NotSupportedException(
            $"{nameof(PostGis)}.{nameof(Simplify)} maps to the PostGIS ST_Simplify function and can only " +
            "be used inside an EF Core LINQ query, where it is translated to SQL.");
}

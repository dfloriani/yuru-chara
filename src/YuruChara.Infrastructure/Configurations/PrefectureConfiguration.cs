using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YuruChara.Domain.Prefectures;

namespace YuruChara.Infrastructure.Configurations;

internal sealed class PrefectureConfiguration : IEntityTypeConfiguration<Prefecture>
{
    public void Configure(EntityTypeBuilder<Prefecture> builder)
    {
        builder.ToTable("prefectures");

        builder.HasKey(p => p.Id);

        // JIS codes are assigned by the standard, so EF must not try to generate them.
        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.NameEn).HasMaxLength(64).IsRequired();
        builder.Property(p => p.NameJa).HasMaxLength(64).IsRequired();
        builder.Property(p => p.NameRomaji).HasMaxLength(64).IsRequired();

        // Stored as text rather than as an integer. This uses a few more bytes per
        // row and makes `select * from prefectures` in psql readable without a
        // lookup. For an enum with eight fixed values, readability is worth more
        // than the storage. The cost is that renaming a member requires a data
        // migration.
        builder.Property(p => p.Region)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // `geometry`, not `geography`. At prefecture scale the difference in
        // accuracy is irrelevant, geometry has far better operator and index
        // support, and ST_Simplify/ST_PointOnSurface are cheaper on it.
        // SRID 4326 throughout so the values are plain WGS84 lat/lng and need no
        // reprojection before they reach Leaflet. See DECISIONS.md.
        builder.Property(p => p.Boundary)
            .HasColumnType("geometry(MultiPolygon, 4326)")
            .IsRequired();

        builder.Property(p => p.LabelPoint)
            .HasColumnType("geometry(Point, 4326)");

        builder.Property(p => p.Centroid)
            .HasColumnType("geometry(Point, 4326)");

        // GIST index on the boundary column. A B-tree index cannot usefully index a
        // geometry. GIST indexes the bounding boxes instead, which allows the
        // ST_Contains query in the point-in-polygon endpoint to exclude most
        // prefectures by bounding box before testing any polygon in detail.
        builder.HasIndex(p => p.Boundary)
            .HasMethod("gist")
            .HasDatabaseName("ix_prefectures_boundary_gist");

        builder.HasMany(p => p.Mascots)
            .WithOne(m => m.Prefecture)
            .HasForeignKey(m => m.PrefectureId)
            .OnDelete(DeleteBehavior.Cascade);

        // The collection has no public setter, so EF reads and writes it through
        // the backing field rather than the property.
        builder.Navigation(p => p.Mascots)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

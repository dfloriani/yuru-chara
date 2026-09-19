using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YuruChara.Domain.Mascots;

namespace YuruChara.Infrastructure.Configurations;

internal sealed class MascotConfiguration : IEntityTypeConfiguration<Mascot>
{
    /// <summary>
    /// Serialiser settings for the citations column. Held as a static so the
    /// options object is not rebuilt on every read.
    /// </summary>
    private static readonly JsonSerializerOptions CitationJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Mascot> builder)
    {
        builder.ToTable("mascots");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.NameJa).HasMaxLength(128).IsRequired();
        builder.Property(m => m.NameRomaji).HasMaxLength(128);
        builder.Property(m => m.Motif).HasMaxLength(256);
        builder.Property(m => m.OwningBody).HasMaxLength(256);
        builder.Property(m => m.LicenseNotes).HasMaxLength(2048);

        // EF has no built-in mapping for Uri, so it is stored as text and rebuilt
        // on read. Keeping Uri on the entity rather than a string means a malformed
        // URL fails at seed time instead of reaching the browser's href.
        builder.Property(m => m.OfficialUrl)
            .HasConversion(
                uri => uri!.ToString(),
                text => new Uri(text))
            .HasMaxLength(2048);

        builder.Property(m => m.LicenseTermsUrl)
            .HasConversion(
                uri => uri!.ToString(),
                text => new Uri(text))
            .HasMaxLength(2048);

        builder.Property(m => m.ImageLicenseStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.VerificationLevel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Citations are stored as a single jsonb column rather than in a child
        // table. They are always read together with their mascot and are never
        // queried on their own, so a join would add cost without adding anything.
        // jsonb still allows them to be queried from psql if that changes later.
        // The conversion is written out explicitly instead of using
        // OwnsMany(...).ToJson() because SourceCitation contains Uri and DateOnly
        // properties, which a System.Text.Json round-trip handles without any
        // further mapping configuration.
        builder.Property(m => m.SourceCitations)
            .HasColumnType("jsonb")
            .HasConversion(
                citations => JsonSerializer.Serialize(citations, CitationJson),
                json => JsonSerializer.Deserialize<List<SourceCitation>>(json, CitationJson) ?? new List<SourceCitation>(),
                // Without an explicit comparer, EF Core compares the converted string
                // by reference and does not detect changes made to the existing list.
                // Copying into a new list is correct here because SourceCitation is an
                // immutable record.
                new ValueComparer<IList<SourceCitation>>(
                    (left, right) => left!.SequenceEqual(right!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    list => list.ToList()))
            .IsRequired();

        // The two filters on GET /api/mascots. Cheap at 47 rows, but the query
        // shapes are fixed and the index documents them.
        builder.HasIndex(m => m.Motif);
        builder.HasIndex(m => m.DebutYear);
    }
}

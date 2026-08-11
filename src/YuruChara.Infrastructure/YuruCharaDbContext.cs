using Microsoft.EntityFrameworkCore;
using YuruChara.Domain.Mascots;
using YuruChara.Domain.Prefectures;

namespace YuruChara.Infrastructure;

/// <summary>
/// The application's single EF Core context.
/// <para>
/// There is no repository layer over this. <see cref="DbContext"/> already is a
/// unit of work with a change tracker and a set-per-entity API; wrapping it in
/// <c>IPrefectureRepository</c> would add a layer whose only job is to forward
/// calls, and would hide the LINQ-to-PostGIS translation that this project is
/// specifically about. See DECISIONS.md.
/// </para>
/// </summary>
public class YuruCharaDbContext(DbContextOptions<YuruCharaDbContext> options) : DbContext(options)
{
    public DbSet<Prefecture> Prefectures => Set<Prefecture>();

    public DbSet<Mascot> Mascots => Set<Mascot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Declaring the extension on the model is what makes it appear in a
        // migration as `CREATE EXTENSION IF NOT EXISTS postgis`. Without this line
        // the geometry columns would still be generated, but the column type they
        // use would not exist, and enabling it would become a manual `psql` step
        // that is not recorded anywhere in the repository. Declaring it here means
        // `dotnet ef database update` is sufficient on its own.
        modelBuilder.HasPostgresExtension("postgis");

        // Picks up every IEntityTypeConfiguration<T> in this assembly. Mapping lives
        // in those classes rather than in data annotations on the entities, which is
        // what keeps YuruChara.Domain free of any EF Core reference. See DECISIONS.md.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YuruCharaDbContext).Assembly);
    }
}

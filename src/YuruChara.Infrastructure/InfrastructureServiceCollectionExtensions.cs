using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YuruChara.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Configuration key for the application database connection string.</summary>
    public const string ConnectionStringName = "YuruChara";

    /// <summary>
    /// Registers <see cref="YuruCharaDbContext"/> against PostgreSQL with the
    /// PostGIS/NetTopologySuite plugin enabled. Shared by the API host, the
    /// ingestion CLI and the integration tests so all three configure the provider
    /// identically — a context built without <c>UseNetTopologySuite</c> silently
    /// fails to map the geometry columns.
    /// </summary>
    public static IServiceCollection AddYuruCharaInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<YuruCharaDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Enables the NetTopologySuite plugin. Without it, Npgsql has no
                // mapping from a PostGIS `geometry` column to a MultiPolygon, and
                // building the model fails.
                npgsql.UseNetTopologySuite();

                // Migrations live in this assembly, not in whichever project happens
                // to be the startup project.
                npgsql.MigrationsAssembly(typeof(YuruCharaDbContext).Assembly.FullName);
            }));

        return services;
    }
}

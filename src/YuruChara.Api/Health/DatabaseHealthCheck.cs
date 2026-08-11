using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using YuruChara.Infrastructure;

namespace YuruChara.Api.Health;

/// <summary>
/// Confirms the API can reach the database and that PostGIS is actually installed
/// in it.
/// <para>
/// A plain "can I open a connection?" check is not enough for this app: every
/// interesting endpoint depends on PostGIS functions, and a database that is up
/// but missing the extension would report healthy right until the first request
/// for boundaries failed. So the check reads <c>postgis_version()</c> and reports
/// it, which doubles as a useful thing to see in the response.
/// </para>
/// </summary>
internal sealed class DatabaseHealthCheck(YuruCharaDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Raw SQL because postgis_version() is a server function with no EF Core
            // LINQ equivalent — there is no entity to query and nothing to translate.
            // SqlQuery<T> runs it and materialises the scalar without needing a DbSet.
            //
            // The `AS "Value"` alias is required. SqlQuery<T> wraps the SQL it is
            // given in a subquery and selects a column from it that it expects to be
            // named exactly "Value". Without the alias the column is named
            // "postgis_version" instead, and PostgreSQL rejects the generated SQL
            // with error 42703, "column s.Value does not exist".
            var version = await dbContext.Database
                .SqlQuery<string>($"""SELECT postgis_version() AS "Value" """)
                .SingleAsync(cancellationToken);

            var pendingMigrations = await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["postgis"] = version,
                ["pendingMigrations"] = pendingMigrations.ToArray()
            };

            // Pending migrations mean the schema is behind the code. Degraded rather
            // than Unhealthy: the process is fine and the fix is `dotnet ef database
            // update`, but it should not be invisible.
            return pendingMigrations.Any()
                ? HealthCheckResult.Degraded("Database reachable, but migrations are pending.", data: data)
                : HealthCheckResult.Healthy("Database reachable and PostGIS is available.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Could not reach the database.", ex);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace YuruChara.Infrastructure;

/// <summary>
/// Lets the <c>dotnet ef</c> tooling construct a context without booting the API.
/// <para>
/// This is what makes the command in CLAUDE.md work as written —
/// <c>dotnet ef migrations add &lt;Name&gt; --project src/YuruChara.Infrastructure</c>
/// with no <c>--startup-project</c>. Worth knowing: when a design-time factory
/// exists, EF uses it and ignores the startup project's configuration entirely,
/// so the connection string has to be resolved here rather than inherited.
/// </para>
/// <para>
/// Design time only. It is never constructed at runtime — the API and the
/// ingestion CLI both get their context from
/// <see cref="InfrastructureServiceCollectionExtensions.AddYuruCharaInfrastructure"/>.
/// </para>
/// </summary>
public sealed class YuruCharaDbContextFactory : IDesignTimeDbContextFactory<YuruCharaDbContext>
{
    /// <summary>
    /// Matches the <c>&lt;UserSecretsId&gt;</c> in YuruChara.Api.csproj and
    /// YuruChara.Ingestion.csproj. Repeated here because this project is a class
    /// library with no host of its own to inherit it from — the id is not a secret,
    /// it is only the folder name the secrets are filed under.
    /// </summary>
    private const string UserSecretsId = "5beb71d8-3b7f-4a76-820f-36b66ce42e92";

    public YuruCharaDbContext CreateDbContext(string[] args)
    {
        // No connection string is committed anywhere, so read the same two sources
        // the running app does: the per-user secret store outside the repository,
        // then the environment (which wins, for CI and containers).
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(UserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"""
                 No '{InfrastructureServiceCollectionExtensions.ConnectionStringName}' connection string is configured,
                 so the EF Core tooling cannot build a DbContext.

                     dotnet user-secrets set "ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}" \
                       "Host=localhost;Port=5432;Database=yuruchara;Username=yuruchara;Password=<password>" \
                       --project src/YuruChara.Api

                 Use the credentials from docker-compose.yml, or set the
                 ConnectionStrings__{InfrastructureServiceCollectionExtensions.ConnectionStringName} environment variable.
                 """);

        var options = new DbContextOptionsBuilder<YuruCharaDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.MigrationsAssembly(typeof(YuruCharaDbContext).Assembly.FullName);
            })
            .Options;

        return new YuruCharaDbContext(options);
    }
}

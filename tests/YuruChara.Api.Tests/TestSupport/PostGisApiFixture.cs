using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using YuruChara.Infrastructure;
using YuruChara.Ingestion.Seeding;

namespace YuruChara.Api.Tests.TestSupport;

/// <summary>
/// A throwaway PostGIS container with the real schema and the real committed seed
/// in it, and an API host wired to it.
/// <para>
/// <b>Testcontainers rather than the EF Core in-memory provider.</b> The in-memory
/// provider is not a database — it is a LINQ provider over dictionaries, with no
/// SQL, no PostGIS, and therefore no <c>ST_Simplify</c>, no <c>ST_Contains</c> and
/// no GIST index. Every behaviour these tests exist to check is exactly what it
/// cannot execute, so a suite passing against it would prove only that the C#
/// compiles. See DECISIONS.md 13.
/// </para>
/// <para>
/// <b>And rather than a shared test database.</b> A long-lived database accumulates
/// state, which makes tests depend on the order they ran in and on what ran
/// yesterday. This container is created for the run and destroyed with it.
/// </para>
/// <para>
/// <b>One container for the whole assembly</b>, not one per test class. Starting
/// PostGIS and seeding 47 boundaries takes several seconds, and every test here is
/// a read — there is nothing for them to corrupt for one another. The isolation
/// that matters is between runs, and the throwaway container is what provides it.
/// </para>
/// </summary>
public sealed class PostGisApiFixture : IAsyncLifetime
{
    // The same image docker-compose.yml uses, for the same reason: the canonical
    // postgis/postgis images are published for linux/amd64 only and this one
    // publishes both architectures. See DECISIONS.md 10. Pinned to a version rather
    // than :latest so a run is reproducible.
    // The image goes to the constructor rather than to .WithImage(): the
    // parameterless PostgreSqlBuilder() is obsolete in Testcontainers 4.13, because
    // defaulting the image and then overriding it made it easy to run against a
    // different database than intended.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("imresamu/postgis:17-3.5")
        .WithDatabase("yuruchara_test")
        .WithUsername("yuruchara_test")
        .WithPassword("yuruchara_test")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The API host, running against the container.</summary>
    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>A client for the API host. Shared; tests must not dispose it.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// The running test's cancellation token. xUnit v3 cancels this when a run is
    /// interrupted or a test times out, and its analyzer requires it to be passed to
    /// anything that accepts one — otherwise a cancelled run waits for in-flight
    /// HTTP calls that nothing is going to read.
    /// </summary>
    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// <c>GET</c> against the API. These three wrappers exist so that every call site
    /// passes <see cref="Cancellation"/> and takes a relative path as a plain string,
    /// rather than repeating both at each of the several dozen requests these tests
    /// make. Caller disposes the response.
    /// </summary>
    public Task<HttpResponseMessage> GetAsync(string path) =>
        Client.GetAsync(new Uri(path, UriKind.Relative), Cancellation);

    public Task<string> GetStringAsync(string path) =>
        Client.GetStringAsync(new Uri(path, UriKind.Relative), Cancellation);

    public Task<byte[]> GetBytesAsync(string path) =>
        Client.GetByteArrayAsync(new Uri(path, UriKind.Relative), Cancellation);

    /// <summary>What the seeder reported, so tests can assert against the data actually loaded.</summary>
    public SeedOutcome SeedOutcome { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        SeedOutcome = await MigrateAndSeedAsync();

        _factory = new ApiFactory(_container.GetConnectionString());
        Client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// A context against the container, for the few assertions that are about the
    /// database rather than about the API — whether the GIST index exists, for
    /// instance. Caller disposes.
    /// </summary>
    public YuruCharaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<YuruCharaDbContext>()
            // The same provider configuration the application uses. A context built
            // without UseNetTopologySuite has no mapping for the geometry columns and
            // fails while building the model, so the tests configure it identically
            // rather than approximately.
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.MigrationsAssembly(typeof(YuruCharaDbContext).Assembly.FullName);
            })
            .Options;

        return new YuruCharaDbContext(options);
    }

    /// <summary>
    /// Brings the container up to the committed schema and loads the committed data.
    /// <para>
    /// The migrations are the real ones from <c>YuruChara.Infrastructure</c>, so this
    /// also proves they apply to an empty database — <c>CREATE EXTENSION postgis</c>
    /// and the GIST index included. The seeding reuses <c>DatabaseSeeder</c> from the
    /// ingestion CLI rather than inserting fixture rows, and that is what lets these
    /// tests assert against real Japanese geometry: a hand-made square polygon would
    /// answer "is Tokyo Station in Tokyo?" only by coincidence.
    /// </para>
    /// </summary>
    private async Task<SeedOutcome> MigrateAndSeedAsync()
    {
        await using var db = CreateDbContext();

        await db.Database.MigrateAsync();

        var boundaries = await PrefectureBoundaryReader.ReadAsync(
            RepositoryPaths.BoundaryGeoJson, CancellationToken.None);

        await using var seedStream = File.OpenRead(RepositoryPaths.MascotSeed);
        var seed = await JsonSerializer.DeserializeAsync<SeedDocument>(seedStream, SeedDocument.Json)
                   ?? throw new InvalidOperationException($"'{RepositoryPaths.MascotSeed}' did not deserialise.");

        return await new DatabaseSeeder(db).SeedAsync(seed, boundaries, CancellationToken.None);
    }

    /// <summary>
    /// Boots the real <c>Program</c> — the same startup path, middleware order and
    /// endpoint registrations the application runs — pointed at the container.
    /// </summary>
    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // "Testing", deliberately not "Development", for two reasons.
            //
            // First, WebApplicationBuilder registers the user-secrets provider in
            // Development only. Under Development this host would read the
            // developer's own secrets store, find their real connection string
            // there, and run the tests against the local development database
            // instead of the container — without saying so.
            //
            // Second, Program applies migrations at startup in Development (see
            // DECISIONS.md 9). The fixture has already migrated; having both do it
            // invites a race for no benefit.
            builder.UseEnvironment("Testing");

            builder.UseSetting(
                $"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}",
                connectionString);
        }
    }
}

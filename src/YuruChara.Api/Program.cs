using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO.Converters;
using YuruChara.Api.Health;
using YuruChara.Api.Mascots;
using YuruChara.Api.Prefectures;
using YuruChara.Infrastructure;

// Minimal APIs rather than MVC controllers. This app is a handful of read-only
// GET endpoints over one DbContext; controllers would add routing attributes,
// a base class and a discovery convention without changing what any of it does.
// See DECISIONS.md.
var builder = WebApplication.CreateBuilder(args);

// No connection string is committed anywhere in this repository. In Development it
// comes from the user-secrets store (WebApplicationBuilder wires that provider up
// automatically for the Development environment, and nowhere else); elsewhere it
// comes from the environment. A fresh clone therefore starts with nothing
// configured, so the failure has to say exactly how to fix it rather than just
// naming the missing key.
var connectionString =
    builder.Configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"""
         No '{InfrastructureServiceCollectionExtensions.ConnectionStringName}' connection string is configured.

         For local development, set it in the user-secrets store (it lives outside the
         repository, so it cannot be committed by accident):

             dotnet user-secrets set "ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}" \
               "Host=localhost;Port=5432;Database=yuruchara;Username=yuruchara;Password=<password>" \
               --project src/YuruChara.Api

         Use the credentials from docker-compose.yml. Anywhere else, set the
         ConnectionStrings__{InfrastructureServiceCollectionExtensions.ConnectionStringName} environment variable.

         See src/YuruChara.Api/appsettings.Development.example.json.
         """);

builder.Services.AddYuruCharaInfrastructure(connectionString);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// JSON settings for every minimal API endpoint in this host.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Teaches System.Text.Json to write NetTopologySuite types as GeoJSON:
    // Geometry, Feature, FeatureCollection and AttributesTable. Without it the
    // boundaries endpoint would serialise the .NET shape of a MultiPolygon —
    // Shell, Holes, Envelope, IsValid and so on — instead of GeoJSON, and this
    // project would have to write its own GeoJSON writer.
    options.SerializerOptions.Converters.Add(new GeoJsonConverterFactory());

    // Enums as their names, not their numeric values. ImageLicenseStatus and
    // VerificationLevel are the two fields a reader of this API most needs to
    // understand, and "ApplicationRequired" says what 1 does not. It also stops
    // the wire format depending on declaration order, so inserting an enum
    // member stays a non-breaking change.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Output caching for the boundaries endpoint.
//
// WHY HERE: /api/prefectures is by far the largest response this app serves
// (hundreds of kilobytes of GeoJSON), it is the same for every visitor, and the
// data behind it changes only when the ingestion CLI is re-run against the
// committed seed. That is the case output caching is for. It is server-side, so
// unlike response caching it does not depend on the client honouring anything.
//
// WHAT INVALIDATES IT: nothing automatically, and that is the honest description.
// The cache is in-process and in memory, so it is emptied by restarting the app,
// which is also what a deployment does. Re-seeding the database while the app is
// running therefore serves stale boundaries for up to the expiry below. The tag
// makes a deliberate eviction possible via IOutputCacheStore.EvictByTagAsync if a
// v2 admin or ingestion endpoint ever needs one. See DECISIONS.md 14.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(PrefectureEndpoints.BoundariesCachePolicy, policy => policy
        // One hour. Long, because the data is static between seed runs; not
        // infinite, so a re-seed against a running instance eventually shows up
        // without anyone having to know about the cache.
        .Expire(TimeSpan.FromHours(1))
        // The response differs per detail level, so the cache key must too.
        // Without this, the first caller's detail level would be served to
        // everyone — a phone asking for ?detail=low would get whatever a desktop
        // asked for a moment earlier.
        .SetVaryByQuery("detail")
        .Tag("prefectures"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development only. This makes `docker compose up -d` followed by `dotnet run`
    // sufficient, with no separate `dotnet ef database update` step against an
    // empty container. It is restricted to Development because two instances
    // starting at the same time would both attempt the same migration, and because
    // it requires the application's runtime login to hold schema-modification
    // rights that it does not otherwise need.
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<YuruCharaDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Must come before the endpoints it caches. The output cache middleware works by
// short-circuiting the pipeline on a hit, so anything registered after it never
// runs for a cached response — which is the point, but it means ordering is not a
// detail here.
app.UseOutputCache();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapPrefectureEndpoints();
app.MapMascotEndpoints();

app.Run();

// Exposed so the integration tests can drive the real host through
// WebApplicationFactory<Program>. Top-level statements generate this class as
// internal, and the InternalsVisibleTo in the .csproj is what lets the test
// project see it.
public partial class Program;

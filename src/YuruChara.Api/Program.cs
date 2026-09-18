using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
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
    // boundaries endpoint would serialise the .NET object model of a MultiPolygon
    // — its Shell, Holes, Envelope, IsValid, IsSimple, Area and Length members —
    // instead of GeoJSON, and this project would have to write its own GeoJSON
    // writer.
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
        .Tag(PrefectureEndpoints.BoundariesCacheTag));
});

// Compression for the GeoJSON. /api/prefectures?detail=high is 607 KB of coordinate
// text, which is the largest thing this app sends and compresses well. The default
// MIME-type list covers application/json but not application/geo+json, which is what
// the boundaries endpoint returns, so that type is added; without it the one response
// that most needs compressing is the one that does not get it.
builder.Services.AddResponseCompression(options =>
{
    // Compression over TLS is off by default because of BREACH, an attack that
    // recovers a secret from compressed response sizes. It needs a secret in the
    // response body and a way to inject text into it. These endpoints are anonymous
    // GETs of public, static data: there is no session, no cookie and no user input
    // in any response.
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, "application/geo+json"];
});

// A cap on requests per minute for the whole app, not per caller.
//
// Per-caller limiting belongs in front of this app, where the visitor's address is
// known. Behind a CDN every request arrives from the CDN's own addresses, so a
// per-address limit here would count all visitors as one caller and refuse them
// together.
//
// What this cap does is bound the requests that reach the endpoints at all, which is
// what protects the hosting quotas and the database from a request loop aimed
// straight at this app's address. A visitor costs two requests per page load, so the
// limit below allows about 60 page loads a second.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            // No queue. A refused request answers immediately with 429 instead of
            // holding a connection open, and the client retries once on a 5xx only.
            QueueLimit = 0
        }));
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

// Outside the output cache, deliberately. The output cache does not vary its entries
// by Accept-Encoding, so a stored compressed body would be served to a client that
// asked for none. Registered here, compression runs on the way out, on the cached
// body as well as a fresh one, and each caller gets the encoding it accepts.
app.UseResponseCompression();

// Before the output cache, so that a cache hit is counted too. Registered after it,
// the cache would short-circuit the pipeline on a hit and the limit would only ever
// see the requests that miss.
app.UseRateLimiter();

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

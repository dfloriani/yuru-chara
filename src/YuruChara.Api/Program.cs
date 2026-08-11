using Microsoft.EntityFrameworkCore;
using YuruChara.Api.Health;
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

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

// Exposed so the integration tests can drive the real host through
// WebApplicationFactory<Program>. Top-level statements generate this class as
// internal, and the InternalsVisibleTo in the .csproj is what lets the test
// project see it.
public partial class Program;

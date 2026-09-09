using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YuruChara.Infrastructure;
using YuruChara.Ingestion.Seeding;
using YuruChara.Ingestion.Wikidata;

// Seed-building CLI. Two commands:
//
//   wikidata  Runs the committed SPARQL query and writes a DRAFT seed file to
//             data/raw/ (gitignored), plus a coverage report. It never writes
//             data/prefecture-mascots.json, because that file holds hand-checked
//             corrections an automated re-run must not be able to overwrite.
//
//   seed      Loads data/prefectures.geojson and data/prefecture-mascots.json into
//             PostGIS and computes the stored ST_PointOnSurface label points.
//
// Top-level statements rather than a Main method, and no generic host. This process
// runs one command and exits; a host would add a lifetime, a logging pipeline and a
// shutdown sequence to something that needs none of them. The connection string is
// read from the same configuration sources the API uses, which is the one piece of
// host behaviour that is actually wanted, so that is set up explicitly below.

const string BoundaryPath = "data/prefectures.geojson";
const string SeedPath = "data/prefecture-mascots.json";
const string DraftPath = "data/raw/prefecture-mascots.draft.json";

var command = args.FirstOrDefault();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return command switch
    {
        "wikidata" => await RunWikidataPassAsync(cancellation.Token),
        "seed" => await RunSeedAsync(cancellation.Token),
        _ => Usage(command)
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception exception)
{
    // Message only, no stack trace. Every throw in this pipeline is a deliberate
    // "your data is wrong, here is which part" with the detail in the message; a
    // stack trace above it buries the useful line.
    Console.Error.WriteLine($"Failed: {exception.Message}");
    return 1;
}

static int Usage(string? command)
{
    if (command is not null)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
    }

    Console.Error.WriteLine(
        """
        YuruChara.Ingestion — builds and loads the committed seed data.

          dotnet run --project src/YuruChara.Ingestion -- wikidata
              Run the Wikidata SPARQL pass. Writes a draft to
              data/raw/prefecture-mascots.draft.json and prints a coverage report.
              Does not modify data/prefecture-mascots.json.

          dotnet run --project src/YuruChara.Ingestion -- seed
              Load data/prefectures.geojson and data/prefecture-mascots.json into
              PostGIS, and compute the stored ST_PointOnSurface label points.
        """);

    return 2;
}

async Task<int> RunWikidataPassAsync(CancellationToken cancellationToken)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

    // WDQS requires a descriptive User-Agent and throttles or blocks requests that
    // do not send one. https://foundation.wikimedia.org/wiki/Policy:User-Agent_policy
    http.DefaultRequestHeaders.UserAgent.ParseAdd(
        "YuruCharaMap/0.1 (https://github.com/dfloriani/yuru-chara; educational project)");

    var retrievedOn = DateOnly.FromDateTime(DateTime.UtcNow);
    Console.WriteLine($"Querying Wikidata Query Service ({retrievedOn:yyyy-MM-dd})...");

    var result = await new WikidataMascotPass(http).RunAsync(retrievedOn, cancellationToken);

    Directory.CreateDirectory(Path.GetDirectoryName(DraftPath)!);
    await using (var stream = File.Create(DraftPath))
    {
        await JsonSerializer.SerializeAsync(stream, result.Document, SeedDocument.Json, cancellationToken);
    }

    ReportCoverage(result, DraftPath);
    return 0;
}

async Task<int> RunSeedAsync(CancellationToken cancellationToken)
{
    var connectionString = ReadConnectionString();

    Console.WriteLine($"Reading {BoundaryPath}...");
    var boundaries = await PrefectureBoundaryReader.ReadAsync(BoundaryPath, cancellationToken);
    Console.WriteLine($"  {boundaries.Count} boundaries.");

    Console.WriteLine($"Reading {SeedPath}...");
    await using var seedStream = File.OpenRead(SeedPath);
    var seed = await JsonSerializer.DeserializeAsync<SeedDocument>(
                   seedStream, SeedDocument.Json, cancellationToken)
               ?? throw new InvalidOperationException($"'{SeedPath}' did not deserialise to a seed document.");

    var services = new ServiceCollection()
        .AddYuruCharaInfrastructure(connectionString)
        .BuildServiceProvider();

    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<YuruCharaDbContext>();

    Console.WriteLine("Seeding...");
    var outcome = await new DatabaseSeeder(db).SeedAsync(seed, boundaries, cancellationToken);

    Console.WriteLine(
        $"""

          Prefectures seeded         {outcome.Prefectures}
          Mascot records             {outcome.Mascots}
            of which hand-checked    {outcome.ManuallyVerified}
          Prefectures with no mascot {outcome.PrefecturesWithoutMascot}
          Label points computed      {outcome.LabelPointsComputed}  (ST_PointOnSurface)
        """);

    return 0;
}

static void ReportCoverage(WikidataPassResult result, string draftPath)
{
    var withMascot = result.Document.Prefectures.Where(p => p.Mascots.Count > 0).ToList();
    var withoutMascot = result.Document.Prefectures.Where(p => p.Mascots.Count == 0).ToList();
    var mascotCount = withMascot.Sum(p => p.Mascots.Count);

    Console.WriteLine(
        $"""

          Rows returned      {result.RowCount}
          Mascot records     {mascotCount}
          Prefectures covered {withMascot.Count} of {result.Document.Prefectures.Count}
          Draft written to   {draftPath}
        """);

    if (result.Skipped.Count > 0)
    {
        Console.WriteLine($"\n  Skipped {result.Skipped.Count} row(s):");
        foreach (var reason in result.Skipped)
        {
            Console.WriteLine($"    {reason}");
        }
    }

    // Printed in full rather than counted. A gap list is the thing a reviewer has to
    // act on, and "14 prefectures have no mascot" is not actionable on its own.
    Console.WriteLine($"\n  No mascot found for {withoutMascot.Count} prefecture(s):");
    foreach (var prefecture in withoutMascot)
    {
        var reference = JisPrefectures.ByCode[prefecture.JisCode];
        Console.WriteLine($"    {prefecture.JisCode,2}  {reference.NameJa,-5} {reference.NameRomaji}");
    }

    Console.WriteLine(
        """

          These are gaps in Wikidata, not confirmed absences. Do not fill them in from
          memory: leave the mascot list empty and record the gap. See CLAUDE.md.
        """);
}

static string ReadConnectionString()
{
    // Same sources, in the same order, as YuruChara.Api: user secrets in Development
    // so the password is never in the repository, and environment variables
    // everywhere else. The UserSecretsId in this .csproj matches the API's, so one
    // `dotnet user-secrets set` configures both.
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(Program).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    return configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"No '{InfrastructureServiceCollectionExtensions.ConnectionStringName}' connection string. " +
            "Run ./scripts/dev-setup.sh, or set ConnectionStrings__YuruChara.");
}

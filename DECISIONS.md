# Decisions

One entry per deliberate choice that had a viable alternative. Each entry states
what was chosen, what was rejected, and what the choice costs. The cost is
recorded because it is what allows the decision to be reconsidered later.

Entries are added as the thing they describe is built. Anything listed in
CLAUDE.md but not yet implemented is marked **pending**.

---

## 1. Minimal APIs rather than MVC controllers

**Chosen:** ASP.NET Core minimal APIs, grouped with `MapGroup`, one static class
per group.

**Rejected:** MVC controllers.

**Why:** The whole API is a small number of read-only `GET` endpoints over one
`DbContext`. Controllers would add a base class, attribute routing and a
discovery convention, and none of those change what any endpoint does. Minimal
APIs also put the route, its parameters and its handler in one place, which
matters for a codebase that is meant to be explained to other people.

**What it costs:** No action filters, no model-binding conventions, and no
built-in `[ApiController]` validation behaviour. If this project later adds
authentication, per-action authorisation policies and many more endpoints,
controllers become the better choice and this decision should be reconsidered.

**Where:** `src/YuruChara.Api/Program.cs`.

---

## 2. `IEntityTypeConfiguration<T>` rather than data annotations

**Chosen:** One configuration class per entity in
`src/YuruChara.Infrastructure/Configurations/`, discovered by
`ApplyConfigurationsFromAssembly`.

**Rejected:** `[Table]`, `[MaxLength]` and `[Column(TypeName = ...)]` attributes
on the entities.

**Why:** Annotations would require the Domain project to reference EF Core, which
contradicts decision 4. There is no way to write
`[Column(TypeName = "geometry(MultiPolygon, 4326)")]` without adding that
dependency. Separately, the mapping is where the PostGIS detail is recorded
(column types, the GIST index method, the jsonb conversion), and that is easier
to read and comment on in one file per entity than as attributes spread across a
class.

**What it costs:** The mapping is not visible when reading the entity. Someone
reading `Prefecture.cs` cannot see that `Boundary` is indexed, and has to know to
open `PrefectureConfiguration.cs`.

**Where:** `src/YuruChara.Infrastructure/Configurations/`.

---

## 3. `geometry` rather than `geography`

**Chosen:** `geometry(MultiPolygon, 4326)` and `geometry(Point, 4326)`.

**Rejected:** `geography`.

**Why:** `geography` calculates on the spheroid, which is the correct answer for
long distances, but it costs more per operation and supports a smaller set of
functions and operators. At the size of a single prefecture the planar
approximation is not visible on a map, and every operation this project performs
(`ST_Simplify`, `ST_Contains`, `ST_PointOnSurface`, and GIST bounding-box search)
has better support on `geometry`.

SRID 4326 throughout means stored coordinates are WGS84 longitude and latitude,
which Leaflet accepts without reprojection.

**What it costs:** Any distance or area calculation added later will be incorrect
unless it first casts to `geography` or reprojects to a metric coordinate system.
`ST_Area` on 4326 `geometry` returns square degrees, which is not a unit of area.
This will matter if "which prefecture is nearest?" or "how large is it?" becomes
a feature.

**Where:** `src/YuruChara.Infrastructure/Configurations/PrefectureConfiguration.cs`.

---

## 4. No EF Core reference in the Domain project

**Chosen:** `YuruChara.Domain` references exactly one package,
`NetTopologySuite`, and nothing else.

**Rejected:** Allowing entities to carry EF Core attributes or navigation
helpers.

**Why:** It keeps the entities readable as a description of the subject matter
rather than of a database, and it makes the boundary enforceable rather than
merely intended: the build fails if someone adds `[Key]`. NetTopologySuite is the
one exception, and it is deliberate. A prefecture's boundary is part of what a
prefecture is, not a storage detail, and representing it as `byte[]` or as a WKT
string in order to avoid the dependency would be worse.

**What it costs:** All mapping has to be written out in configuration classes
(decision 2), which is more code than annotations.

**Where:** `src/YuruChara.Domain/YuruChara.Domain.csproj`.

---

## 5. `ST_PointOnSurface` rather than `ST_Centroid` for label placement

**Chosen:** Store both. Draw map labels from `LabelPoint`, which is
`ST_PointOnSurface(boundary)` computed once at seed time.

**Rejected:** Placing labels at `ST_Centroid`.

**Why:** A centroid is a centre of mass, and it is not guaranteed to be inside
its own polygon. `ST_PointOnSurface` always returns a point that is on the
geometry, so labels are drawn on land.

**Measured, after seeding the committed geometry.** All 47 `ST_PointOnSurface`
points are inside their own boundary. **43 of the 47 centroids are.** The four
that are not:

| Prefecture | Centroid distance offshore | Why |
|---|---:|---|
| Okinawa | 58.9 km | Islands spread over 800 km of ocean; the centroid is sea between them |
| Tokyo | 34.7 km | Izu and Ogasawara chains pull it south, into Sagami Bay |
| Kagoshima | 17.1 km | Two peninsulas around Kinkō Bay; the centroid is in the bay |
| Kōchi | 0.2 km | Concave crescent around Tosa Bay; the centroid is just offshore |

Distances are computed by casting to `geography`, because `ST_Distance` on 4326
`geometry` returns degrees — see decision 3.

**An earlier version of this entry named the wrong examples, and the correction
is worth keeping.** It asserted that Nagasaki's centroid "falls in open water"
and that Tokyo's is "roughly a thousand kilometres south". Neither is true:

- **Nagasaki's centroid is inside its boundary.** It is the most island-heavy
  prefecture in the country, which is why it looked like the obvious example,
  but `ST_Centroid` is **area-weighted**. Nagasaki's mainland peninsulas hold
  most of its area, so they dominate the result and it lands on land.
- **Tokyo's centroid is 34.7 km out, not a thousand.** Same reason from the
  other direction: the Ogasawara islands are a thousand kilometres away but
  they are tiny, so they contribute almost no area and move the centroid only
  slightly.
- **Kōchi was not predicted at all,** and it is the most interesting case. It
  has no far-flung islands. It is simply concave — a crescent around Tosa Bay —
  and concavity alone is enough. This is the failure mode that would be missed
  by reasoning only about islands.

The lesson is that "many islands" is the wrong intuition for this problem and
"the shape is not convex" is the right one. Islands matter only when they are
far away *and* carry real area, which is Okinawa.

Both values are computed at seed time rather than per request. They never change,
and recomputing them on every read of all 47 boundaries would be wasted work.

**What it costs:** `ST_PointOnSurface` returns one valid interior point, not
necessarily the one that looks best. For a horseshoe-shaped prefecture it can be
close to an edge. A pole-of-inaccessibility algorithm would produce a better
position and is considerably more work. `Centroid` is stored as well because it
remains the correct value for questions about which prefecture is nearest.

A second cost, visible in the table above: both values are properties of the
**committed simplified geometry**, not of the true coastline. Re-source the
boundary file at a different tolerance and these numbers change. That is
acceptable because the label has to sit on the polygon actually being drawn,
which is the simplified one.

**Where:** `src/YuruChara.Domain/Prefectures/Prefecture.cs`,
`src/YuruChara.Ingestion/Seeding/DatabaseSeeder.cs`.

---

## 6. Coverage and confidence as two separate fields

**Chosen:** Whether a prefecture has a mascot record at all is one question.
`VerificationLevel` (`Automated` or `ManuallyVerified`) is a separate one.

**Rejected:** A single `IsVerified` flag, or treating "has data" as meaning "is
correct".

**Why:** The two are different, and combining them removes information this
project is specifically trying to show. The Wikidata pass produces broad coverage
at low confidence. A hand-checked subset produces high confidence for a small
number of prefectures. With two fields the UI can state "40 prefectures have
data, and 10 of those have been checked against the owning body's own website".
With one field it can only state "40 prefectures", which implies more than is
true.

Both fields default to the more cautious value. `VerificationLevel.Automated` and
`ImageLicenseStatus.Unknown` are both `0`, so a record created without an explicit
decision is described as unverified rather than as trustworthy. Tests in
`tests/YuruChara.Domain.Tests/MascotDefaultsTests.cs` assert this.

**What it costs:** Two fields to keep accurate instead of one, and the UI has to
render three states (verified, automated, absent) rather than two.

**Where:** `src/YuruChara.Domain/Mascots/VerificationLevel.cs`.

---

## 7. No repository pattern over `DbContext`

**Chosen:** Endpoints and the seeder use `YuruCharaDbContext` directly.

**Rejected:** Adding an interface per entity, such as `IPrefectureRepository` and
`IMascotRepository`, with classes implementing them over the `DbContext`.

**Why:** `DbContext` is already a unit of work with a change tracker and a
set-per-entity API. A repository over it would mostly forward calls to it.
It would also conceal the translation from LINQ to PostGIS, which is one of the
things in this project most worth reading: `EF.Functions.Contains(p.Boundary,
point)` becoming an `ST_Contains` query that uses the GIST index.

**What it costs:** Query code is written inside endpoint handlers, so sharing a
query between two endpoints means extracting a method rather than having one
obvious place for it. If that happens often, the better answer is a query object,
still not a repository.

**Where:** `src/YuruChara.Infrastructure/YuruCharaDbContext.cs`.

---

## 8. PostGIS extension enabled by migration, never by hand

**Chosen:** `modelBuilder.HasPostgresExtension("postgis")`, which writes
`CREATE EXTENSION IF NOT EXISTS postgis` into the `InitialSchema` migration.

**Rejected:** A `CREATE EXTENSION` statement in a container initialisation
script, or a manual `psql` step described in the documentation.

**Why:** A manual step must be repeated for every new checkout, every CI run and
every Testcontainers instance, and can be forgotten in any of them. Putting it in
the migration means `dotnet ef database update` is sufficient everywhere, and the
requirement is recorded next to the schema that depends on it.

So that this is actually true rather than only true by coincidence,
`docker/initdb/20-trim-extensions.sql` drops the `postgis` extension that the
base image creates. On a new volume the migration is therefore what installs
PostGIS, in the same way it would against a managed PostgreSQL service that
enables no extensions in advance. The same script drops `postgis_topology`,
`fuzzystrmatch` and `postgis_tiger_geocoder`, which the image also enables and
which add about 37 unused US census tables to the output of `\dt`.

**What it costs:** Creating an extension requires elevated database rights. That
is available locally, but a managed PostgreSQL service where the application's
login cannot run `CREATE EXTENSION` would need PostGIS enabled separately by an
administrator, and this migration marked as already applied.

**Where:** `src/YuruChara.Infrastructure/YuruCharaDbContext.cs`,
`docker/initdb/20-trim-extensions.sql`.

---

## 9. Migrations applied at startup in Development only

**Chosen:** `Database.MigrateAsync()` runs during startup when the environment is
Development.

**Rejected:** Running it in all environments, or in none.

**Why:** It makes `docker compose up -d` followed by
`dotnet run --project src/YuruChara.Api` sufficient for a new checkout, which is
what the project brief requires. The health check reports pending migrations as
`Degraded` rather than `Healthy`, so if this is ever removed the missing step is
reported rather than silent.

**What it costs:** Outside Development this would be incorrect. Two instances
starting at the same time would both attempt the same migration, and it requires
the application's runtime login to hold schema-modification rights it does not
otherwise need. That is why it is restricted by environment.

**Where:** `src/YuruChara.Api/Program.cs`.

---

## 10. `imresamu/postgis` image rather than `postgis/postgis`

**Chosen:** `imresamu/postgis:17-3.5`.

**Rejected:** `postgis/postgis:17-3.5`, which is the image named in CLAUDE.md.

**Why:** The `postgis/postgis` images are published for `linux/amd64` only, and
this project is developed on arm64, where `docker compose up` fails with
`no matching manifest for linux/arm64/v8`. `imresamu/postgis` is built from the
same docker-postgis sources by a maintainer of that repository and publishes both
architectures, so one tag works on arm64 Linux, on Apple Silicon and on x86.

**What it costs:** An image name that is not the canonical one and therefore
needs explaining, and a dependency on a publisher outside the PostGIS project's
own Docker Hub namespace. The PostgreSQL and PostGIS versions are the same: 17.6
and 3.5.3. Change back to `postgis/postgis:17-3.5` if this project is only ever
built on amd64.

**Where:** `docker-compose.yml`.

---

## 11. One stored copy of the database password, in user secrets

**Chosen:** The password exists in exactly one place that a person writes to: the
.NET user-secrets store, which is outside the repository. Everything else derives
from it.

- The API and the ingestion CLI read the connection string from that store in
  Development, and from the `ConnectionStrings__YuruChara` environment variable
  everywhere else.
- `scripts/dev-setup.sh` reads the same store and writes `.env`, which is the
  only file Docker Compose reads credentials from. `.env` is excluded by
  `.gitignore` and is marked as generated in its own header.
- On first run, when no secret exists, the script generates a random password
  with `openssl rand -hex 24`, stores it, and writes `.env` from it. The password
  is therefore never typed by a person and never displayed.

**Rejected:**

- A working connection string in `appsettings.json`, or a password written into
  `docker-compose.yml`. Both files are committed.
- A password written by hand into both `.env` and the user-secrets store. This
  works, but the same value then exists in two files that are edited separately
  and can disagree.
- Having the application read `.env` directly, so that `.env` is the single
  source. .NET has no built-in configuration provider for `.env` files, so this
  requires either a third-party package such as DotNetEnv, or a wrapper script
  that exports the variables before running `dotnet`, or entries in
  `launchSettings.json`, which is committed and so reintroduces the original
  problem.

**Why:** The user-secrets store is at
`~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` on Linux and macOS, and
`%APPDATA%\Microsoft\UserSecrets\<id>\` on Windows. Because it is outside the
repository, no `git add`, `.gitignore` change, directory copy or backup of the
working tree can include it. `WebApplicationBuilder` registers that provider for
the Development environment only, so a developer's local secrets are not read in
a deployed environment.

Deriving `.env` from the store rather than maintaining it separately is what
removes the possibility of the two disagreeing. Docker Compose reads credentials
only from `.env` or from the shell environment, and PostgreSQL accepts its
initial password only from an environment variable, so some file for Compose is
unavoidable. Generating it means it is never authored, only produced.

The credentials this protects are local and disposable. The reason to do it now
is that the mechanism has to exist before there is anything worth protecting.

`YuruChara.Api` and `YuruChara.Ingestion` declare the same `UserSecretsId`, so one
`dotnet user-secrets set` configures both. `YuruCharaDbContextFactory` reads the
same store, so `dotnet ef database update` works with no connection string in the
repository. That factory has to read configuration itself: when a design-time
factory exists, EF Core uses it and does not read the startup project's
configuration, so it has no other source available.

The port is published as `127.0.0.1:5432:5432` rather than `5432:5432`, because
the shorter form listens on every network interface and makes the development
database reachable from other machines on the same network.

**What it costs:** A new checkout does not run until `./scripts/dev-setup.sh` has
been run once. The API, the EF Core design-time factory and Docker Compose each
fail with a message naming the command that fixes it, rather than with a null
reference or a default password.

The user-secrets store is per-user and per-machine, so CI cannot use it and must
set `ConnectionStrings__YuruChara` instead.

`.env` is still a real file containing the password. It cannot be committed by
accident, but it can be read by anything that can read the working directory. It
is written with mode `600`.

Changing the password requires deleting the database volume, because PostgreSQL
writes the password into the data directory only when it first creates it. An
existing volume keeps the password it was created with. `scripts/dev-setup.sh`
detects that a volume exists and prints this. See decision 11a.

**Where:** `scripts/dev-setup.sh`, `docker-compose.yml`,
`src/YuruChara.Api/Program.cs`,
`src/YuruChara.Infrastructure/YuruCharaDbContextFactory.cs`,
`src/YuruChara.Api/appsettings.Development.example.json`.

---

## 11a. The container healthcheck authenticates by service name, not loopback

**Chosen:** The healthcheck runs
`PGPASSWORD=$POSTGRES_PASSWORD psql -h db ...`, connecting by Docker service
name.

**Rejected:** `pg_isready` on its own, `psql` over the Unix socket, and
`psql -h 127.0.0.1`. None of those three verify a password.

**Why:** `initdb` writes these rules into `pg_hba.conf`:

```
local all all              trust
host  all all 127.0.0.1/32 trust
host  all all all          scram-sha-256
```

From inside the container, both the Unix socket and `127.0.0.1` match a `trust`
rule, so no password is checked. A healthcheck using either reports the container
as healthy even when no external client can authenticate. That happens whenever a
volume outlives a password change: the container reports healthy, and the
application then fails with `28P01: password authentication failed`.

`-h db` resolves through Docker's DNS to the container's own bridge address. That
address is not loopback, so it matches the `scram-sha-256` rule and the password
is verified. This has been checked in both directions: with a volume whose stored
password differs from `POSTGRES_PASSWORD` the container is reported `unhealthy`,
and with a newly initialised volume it is reported `healthy`.

One related behaviour worth recording: **`docker compose down -v` substitutes
variables from `.env` in the same way `docker compose up` does.** If `.env` is
missing, `down -v` stops with the same "required variable is missing" error and
removes nothing. An interpolation error from `down -v` means the volume still
exists.

**What it costs:** A healthcheck longer than a single command, which needs the
explanatory comment above it in `docker-compose.yml`. It also depends on the
service being named `db`.

**Where:** `docker-compose.yml`.

---

## 12. Seed data committed and simplified, not Git LFS

**Chosen:** `data/` holds the committed seed files, `prefecture-mascots.json` and
the prefecture GeoJSON, in ordinary version control. `data/raw/` is excluded by
`.gitignore`.

**Rejected:** Git LFS for the boundary file, and committing raw Wikidata
downloads next to the seed.

**Why:** The seed is the reproducible output of the ingestion step and is what
the application is built from, so it belongs in history where a diff shows what
changed about the data. Raw source downloads are inputs: they are large, they can
be fetched again, and they produce large uninformative diffs, so they stay
local.

If the source GeoJSON is tens of megabytes, the correct response is to simplify
the geometry before committing it rather than to use LFS. LFS adds a second
storage system, a smudge filter, and a category of clone failure, to a repository
whose purpose is to be small and self-contained. Simplification is also the
accurate response, because the full-resolution coastline of Nagasaki contains
detail this application will never draw.

**What it costs:** The committed geometry is lossy. The tolerance used at seed
time sets the maximum detail any `?detail=high` request can return: a further
simplification at read time can reduce detail, but nothing can restore it. That
tolerance therefore has to be recorded in `DATA-SOURCES.md` next to the licence.

**As built.** `data/prefectures.geojson` is 2.4 MB, 47 features, 61,033
vertices, simplified to 1%. `data/prefecture-mascots.json` is 35 mascot records
across 33 prefectures with 14 recorded gaps. `data/raw/` holds the draft the
Wikidata pass writes and is excluded. The tolerance and its consequence are
recorded in `DATA-SOURCES.md` section 1 under "Simplification tolerance".

**Where:** `.gitignore`, `data/`, `DATA-SOURCES.md`.

---

## 13. Testcontainers rather than an in-memory provider

**Chosen:** The API integration tests run against a PostGIS container started for
the test run and destroyed with it. One container for the whole test assembly,
migrated with the real migrations and seeded with the real committed data through
the real `DatabaseSeeder`.

**Rejected:** The EF Core in-memory provider, and a long-lived shared test
database.

**Why:** The in-memory provider is not a database. It is a LINQ provider over
dictionaries: no SQL, no PostGIS, no `ST_Simplify`, no `ST_Contains`, no GIST
index. Everything these tests exist to check is precisely what it cannot execute,
so a suite passing against it would establish only that the C# compiles. The two
defects found while building this checkpoint — an `OrderBy` that could not be
translated, and `Geometry.NumPoints` returning NULL for polygons — are both
invisible without a real server.

A shared test database can run these queries, but it accumulates state, so tests
come to depend on the order they ran in and on what ran yesterday. The container
gives isolation between runs, which is the isolation that matters here.

**Seeded from `data/`, not from fixtures.** The tests assert that Tokyo Station
resolves to Tokyo and that Cape Ashizuri resolves to Kōchi. Against a hand-made
square polygon those assertions would pass by construction and mean nothing.
Against the committed boundaries they mean the query resolved a real coordinate
against real geometry. It also gives the migrations a genuine test: every run
applies them to an empty database, `CREATE EXTENSION postgis` included.

**One container for the assembly, not one per class.** Starting PostGIS and
seeding 47 boundaries takes several seconds and every test is a read, so there is
nothing for them to corrupt for each other.

**What it costs:**

- `dotnet test` now requires a running Docker daemon, and CI must provide one.
  There is no fallback: the tests do not degrade to a fake, they fail.
- The first run pulls the image.
- Test classes run serially (`DisableTestParallelization`). Not for the
  database's sake — every test is a read — but because the output cache is
  process-wide state, and the cache tests have to observe it going from cold to
  warm without another class warming it underneath them.
- The fixture depends on `YuruChara.Ingestion`, so a test project references a
  console application. That is what avoids a second, drifting copy of the seeding
  logic.

**Where:** `tests/YuruChara.Api.Tests/TestSupport/PostGisApiFixture.cs`,
`tests/YuruChara.Api.Tests/AssemblyConfiguration.cs`.

---

## 14. Output caching on the boundaries endpoint

**Chosen:** `GET /api/prefectures` is output-cached for one hour, varying by the
`detail` query parameter and tagged `prefectures`.

**Rejected:** No cache; response caching (`Cache-Control`) instead of output
caching; caching the other endpoints too.

**Why:** It is by far the largest response the application serves — 593 KB at
`detail=high` — it is identical for every visitor, and it changes only when the
ingestion CLI is re-run. That is the shape of problem output caching is for.

Output caching rather than response caching because it is server-side: the app
stores the rendered response and serves it without touching PostGIS, whereas
response caching only sets headers and depends on the client or a proxy choosing
to honour them.

`SetVaryByQuery("detail")` is not optional. Without it the endpoint has one cache
entry, and whichever detail level was requested first is served to everyone —
a phone asking for `low` would receive a desktop's `high`, which is four times
the payload and precisely what the parameter exists to prevent.

Varying by `detail` **and nothing else** is also deliberate. A policy that varied
by the whole query string would let any caller fill the cache with unbounded
distinct keys by appending a counter.

**What invalidates it: nothing automatic.** This is the honest description and it
is stated rather than implied. The store is in-process and in memory, so it is
emptied by restarting the application, which is also what a deployment does.
Re-seeding the database underneath a running instance serves stale boundaries for
up to an hour. The one-hour expiry is what bounds that; it is long because the
data is static, and finite for exactly this reason. The `prefectures` tag makes
deliberate eviction possible through `IOutputCacheStore.EvictByTagAsync`, which is
what a v2 ingestion endpoint would call. Nothing in v1 calls it outside the tests.

**Not applied to `/api/mascots`.** Its response is a few tens of kilobytes and its
two filters open a much larger key space than the three variants of the boundaries
endpoint, so a cache there would hold many entries and save little.

**What it costs:** A window during which the API can serve boundaries that no
longer match the database, and a second copy of the largest response held in
memory per detail level. Both are bounded and neither is silent — the tests
observe cache hits through the `Age` and `Date` headers.

**Where:** `src/YuruChara.Api/Program.cs`,
`src/YuruChara.Api/Prefectures/PrefectureEndpoints.cs`,
`tests/YuruChara.Api.Tests/OutputCacheTests.cs`.

---

## 14a. `ST_Simplify` declared to EF Core as a database function

**Chosen:** `PostGis.Simplify` is a C# method that throws, mapped onto PostGIS's
`ST_Simplify` with `modelBuilder.HasDbFunction(...).HasName("ST_Simplify").IsBuiltIn()`.
The boundaries query then calls it from an ordinary LINQ projection.

**Rejected:** Raw SQL for the whole boundaries query; simplifying client-side with
NetTopologySuite; serving the full-resolution geometry.

**Why:** The Npgsql NetTopologySuite plugin translates a large part of PostGIS by
mapping NetTopologySuite's own members onto it — `Geometry.Contains` becomes
`ST_Contains`, `Geometry.InteriorPoint` becomes `ST_PointOnSurface`. That works
only where NetTopologySuite has an equivalent member. `ST_Simplify` has none:
NetTopologySuite does Douglas-Peucker simplification through a separate
`DouglasPeuckerSimplifier` class rather than a method on `Geometry`, so there is
nothing for the plugin to map and no `EF.Functions.Simplify` either.

Raw SQL for the whole query would mean hand-writing the column list and losing the
mascot-count sub-select. Client-side simplification would transfer all 2.3 MB of
full-resolution geometry from the database on every cache miss in order to send a
quarter of it to the browser, and would put the work on the web server rather than
the database built for it.

`IsBuiltIn()` is the part that is easy to get wrong and hard to diagnose. Without
it EF Core treats the function as user-defined, schema-qualifies it and quotes the
identifier, producing `public."ST_Simplify"(...)`. PostgreSQL does not case-fold a
quoted identifier, and the function PostGIS installs is named `st_simplify`, so
the quoted form fails with 42883 "function does not exist".

**`ST_Simplify` and not `ST_SimplifyPreserveTopology`,** which is the more
defensive choice and the wrong one here. `ST_Simplify` does two things
`ST_SimplifyPreserveTopology` does not: it drops rings that collapse, and it can
produce self-intersecting polygons. Dropping rings is the point — it takes the
polygon count from 736 to 191 at `detail=low`, which is most of where the phone
payload comes from, and those are islands far smaller than a pixel at that zoom.

**What it costs:** The served geometry is not guaranteed to be valid. At every
tolerance in use some prefectures come back self-intersecting; PostGIS reports
this through `ST_IsValid`. That is acceptable because the output is for drawing —
Leaflet renders a path either way — and the full-resolution column is what every
spatial query runs against. It would not be acceptable if a client ever performed
geometry operations on the response, and if that becomes a requirement the answer
is `ST_SimplifyPreserveTopology` plus a larger payload, not a different tolerance.

The declaration is also a bet on a package's limitations. If a future Npgsql
release translates `ST_Simplify` natively, this becomes redundant rather than
wrong — `PostGisTranslationTests` is what would report it.

**Where:** `src/YuruChara.Infrastructure/PostGis.cs`,
`src/YuruChara.Infrastructure/YuruCharaDbContext.cs`,
`tests/YuruChara.Api.Tests/PostGisTranslationTests.cs`.

---

## 14b. Simplification tolerances chosen in pixels, not in metres

**Chosen:** `detail=high` is `ST_Simplify` at 0.005°, `detail=low` at 0.02°. A
missing `detail` parameter means `high`; an unrecognised one is a 400.

**Rejected:** Tolerances derived from a distance on the ground; a single detail
level; defaulting to `low`.

**Why:** The tolerances are in degrees because the geometry is SRID 4326, and
sizing them is a question about pixels rather than about metres — simplification
is invisible as long as the tolerance stays below the size of a pixel at the zoom
the shape is drawn at. Japan spans roughly 20° of longitude, which is about
0.017°/pixel on a 1200-pixel desktop viewport and about 0.05°/pixel on a
390-pixel phone.

Measured over all 47 prefectures, through the API's own serialiser:

| Level | Tolerance | Bytes | Vertices | Polygons | % of raw |
|---|---|---:|---:|---:|---:|
| raw | — | 2,392,871 | 61,033 | 736 | 100.0% |
| high | 0.005° (~450 m) | 607,184 | 15,197 | 555 | 25.4% |
| low | 0.02° (~1.8 km) | 158,380 | 3,712 | 191 | 6.6% |

**A missing parameter means `high`, not `low`,** even though the frontend is
mobile-first. Mobile-first is a constraint on the frontend, not a safe default for
an API: the client is the only party that knows its viewport, and silently
returning coarser data than a caller expected is a worse failure than returning
finer. The frontend opts into `low` explicitly.

An unrecognised value is rejected rather than falling back, so `?detail=medium` is
a 400 naming the mistake instead of a 200 quietly ignoring it.

**What it costs:** Two fixed tolerances cannot be right at every zoom. `high` is
sized for viewing all of Japan; zoomed into a single prefecture, 0.005° is a few
pixels and the faceting is visible. The alternatives are a continuous tolerance
parameter, which would defeat the output cache by making the key space unbounded,
or pre-built tiles, which is a much larger piece of machinery than a choropleth of
47 polygons needs.

**Where:** `src/YuruChara.Api/Prefectures/DetailLevel.cs`,
`tests/YuruChara.Api.Tests/PayloadSizeTests.cs`.

---

## 15. Leaflet and PostGIS rather than Google data-driven styling

**Chosen:** The map draws GeoJSON served by this project's own API from its own
PostGIS database, rendered by Leaflet, behind the interface in
`web/src/map/PrefectureMap.ts`.

**Rejected:** Google Maps Platform's data-driven styling for boundaries, which
would style `ADMINISTRATIVE_AREA_LEVEL_1` features Google already holds.

**Why:** Four reasons, set out in CLAUDE.md and unchanged by building it. It
moves all spatial work off the backend, and PostGIS is the point of this project.
Google's terms do not permit storing or caching their geometry, so the two cannot
be run side by side. It requires a billing account, an API key and a vector Map
ID. It keys the data on Google Place IDs rather than on JIS prefecture codes,
which are open and are what every Japanese dataset uses.

**What crosses the interface**, which is the part that decides what a swap would
cost:

- GeoJSON in. Every mapping library reads it, and it is what the API serves.
- JIS prefecture codes out, on selection. Not a library's internal layer handle,
  and not a Place ID.
- A `Coverage` value per prefecture, not a colour. The implementation resolves it
  to a fill through the one table in `web/src/data/coverage.ts`, so a replacement
  map cannot draw a palette the legend disagrees with.
- A zoom threshold for labels, and the pixel height of whatever is covering the
  bottom of the map.

**What it costs:** Zoom is a map-shaped idea and it is in the interface. Any
replacement has to have a compatible notion of zoom levels for the label
threshold to mean anything, and Google's zoom levels are on the same scale, so
this is a real constraint rather than a leak. The larger cost is unchanged: this
project renders and simplifies its own geometry, and the labels, the zoom
behaviour and the interaction model are ours to get right rather than Google's.

Three things a map library normally leaks do not appear at the seam. There is no
tile layer to configure (entry 20), the projection is never named outside the
implementation, and the palette is resolved from a coverage state rather than
passed in. What remains visible is the interaction model, and it is the four
items listed above.

**Where:** `web/src/map/PrefectureMap.ts` is the interface;
`web/src/map/LeafletPrefectureMap.tsx` is the only file in the frontend that
imports `leaflet` or `react-leaflet`.

---

## 16. Boundaries sourced pre-simplified from an N03 derivative, not from MLIT

**Chosen:** `data/prefectures.geojson` is
`smartnews-smri/japan-topography`'s `prefectures.json` at 1% simplification,
committed byte-for-byte and verified by SHA-256.

**Rejected:** Downloading 国土数値情報 N03 from MLIT directly and simplifying it
in this repository.

**Why:** The direct route means reading a shapefile or GML, a raw input of tens
of megabytes carrying coastline detail this map will never draw, and a
simplification step of our own to build and tune. The 1% build already exists,
is derived from the same MLIT dataset, and is small enough to commit. Both hops
of the licence chain were checked before committing: the intermediary imposes no
condition of its own, and MLIT publishes N03 as CC BY 4.0 / PDL 1.0 for FY2018
onwards. See `DATA-SOURCES.md` section 1.

**What it costs:**

- The vintage is fixed at the intermediary's 2021 retrieval, not ours. This
  matters much less for prefectures than it would for municipalities, which
  merge and rename; prefecture boundaries have been stable since 1972.
- The 1% tolerance is theirs, so the detail ceiling was chosen by someone else.
  Raising it means changing source, not changing a parameter.
- One more party in the chain who could remove the file. The SHA-256 in
  `DATA-SOURCES.md` means a replacement can at least be recognised as different.
- The file has **no JIS code** in it — its only property is the Japanese name —
  so the join is by name. See decision 17.

**Where:** `data/prefectures.geojson`, `DATA-SOURCES.md` section 1.

---

## 17. The boundary file is joined to prefectures by Japanese name

**Chosen:** `PrefectureBoundaryReader` looks each feature's `N03_001` value up in
`JisPrefectures.ByNameJa`, and throws if any feature is unmatched or the count is
not 47.

**Rejected:** Trusting array position. The file's features are in fact in JIS
code order — feature 1 is 北海道, feature 47 is 沖縄県 — so `features[i]` would
work today.

**Why:** Order is not a documented guarantee of the source, and the failure mode
of relying on it is the worst kind available here: if a future release reorders
or inserts a feature, every prefecture silently gets the wrong shape, and the
map still renders. Nothing would look broken. Joining by name fails loudly
instead, and it was checked before being relied on — all 47 `N03_001` values
match the Japanese labels Wikidata returns, character for character.

**What it costs:** The join now depends on exact string equality of Japanese
text, which makes it sensitive to things position is not: a normalisation change
(NFC versus NFD), a full-width/half-width difference, or a genuine rename. All
three surface as a hard failure naming the unmatched value, which is the
intended trade.

**Where:** `src/YuruChara.Ingestion/Seeding/PrefectureBoundaryReader.cs`,
`src/YuruChara.Ingestion/Seeding/JisPrefectures.cs`.

---

## 18. The automated pass writes a draft; the committed seed is hand-curated

**Chosen:** `ingestion wikidata` writes `data/raw/prefecture-mascots.draft.json`,
which is gitignored. It never writes `data/prefecture-mascots.json`. Applying a
new automated pass means diffing the draft against the committed file and
merging by hand.

**Rejected:** Having the Wikidata pass write the seed file directly, with the
manual corrections applied afterwards as a patch file or an override table.

**Why:** The committed seed contains work that no automated pass can reproduce —
eleven records checked field by field against an owning body's own website, two
corrected debut years, three corrected URLs. If the automated pass wrote that
file, then re-running it would destroy all of it, and the destruction would look
like a successful run. Making the two files different paths means the automated
pass has no way to do that.

An override table was the closer alternative and was rejected because it splits
one mascot's facts across two files. The seed file is meant to be readable as
the answer to "what do we believe about this mascot and why", and that stops
being true when half the answer is somewhere else.

**What it costs:** Refreshing from Wikidata is a manual merge rather than a
command. With 47 prefectures that is the right trade; if this became thousands of
municipal mascots (a v2 goal) it would not be, and the correct answer then is
probably to keep the same split but generate the merge — comparing per field
against the recorded citation reliability, so an `Official` fact is never
overwritten by an `Aggregated` one.

**Where:** `src/YuruChara.Ingestion/Wikidata/WikidataMascotPass.cs`,
`src/YuruChara.Ingestion/Program.cs`, `.gitignore`.

---

## 19. Source citations are per field, not per record

**Chosen:** `SourceCitations` holds one entry per fact, each naming the field it
backs and its own `SourceReliability`. A single mascot routinely carries a mix.

**Rejected:** One source and one reliability per mascot record.

**Why:** Because the mixture is the normal case, not an edge case. Of the eleven
hand-checked mascots, most have an `Official` citation on `DebutYear`,
`OwningBody` and `OfficialUrl`, and an `Aggregated` one on `Motif`, because the
owning body's own page does not name the animal. Kumamoto's profile page
explicitly declines to say Kumamon is a bear. Under one reliability per record
that mascot has to be described as either wholly official — which overstates the
motif — or wholly aggregated, which discards a verified debut year. Neither is
true, and the per-field version can simply say what is.

It also makes `VerificationLevel` meaningful rather than decorative:
`ManuallyVerified` means someone checked this record against the owning body,
and the citations say precisely which fields that produced.

**What it costs:** Every seed record carries eight or so citation objects, which
is most of the bulk of a 2,200-line file. The `jsonb` column is written and read
whole and never queried on its own (see the comment in `MascotConfiguration`), so
the cost is file size and review effort rather than query time.

**Where:** `src/YuruChara.Domain/Mascots/SourceCitation.cs`,
`data/prefecture-mascots.json`.

---

## 20. No basemap tile layer under the choropleth

**Chosen:** The prefecture polygons are drawn on a flat background colour.

**Rejected:** An OpenStreetMap, Carto or MapTiler raster tile layer underneath
them, which is what a Leaflet map normally starts with.

**Why:** Three reasons, in order of weight. A tile layer is a data source, and
`DATA-SOURCES.md` governs every source this project displays — adding one that is
not recorded there would break the rule that file exists to enforce. It brings a
second attribution requirement, alongside the MLIT statement the footer already
has to carry in full. And it is a runtime dependency on a third-party server for
every pan and zoom, on a map whose entire content is already served by this
project.

There is also a design reason, which would not have been sufficient on its own: a
choropleth encodes its data as fill colour, and a photographic basemap underneath
competes with exactly the channel that carries the meaning.

**What it costs:** No coastline detail beyond prefecture borders, no cities, no
roads, and no sense of scale from a familiar backdrop. A visitor who does not
recognise the shape of Japan gets no help from the map — which is part of why the
searchable list is a first-class view rather than a fallback.

**Where:** `web/src/map/LeafletPrefectureMap.tsx`.

---

## 21. A transparent wide stroke for tap targets, on a second layer

**Chosen:** Every prefecture is drawn twice. The visible layer carries the fill
and a hairline border and takes no events. An identical layer above it takes all
the events and is drawn with a 20px stroke and a fill that are both fully
transparent.

**Rejected:** Widening the visible border, which changes the map to fix the
input; enlarging small prefectures' geometry, which would be a lie about the
data; and a single interactive layer, which offers no target beyond the shape.

**Why:** CLAUDE.md requires small prefectures to be tappable, and at the zoom that
fits Japan on a 375px screen Kagawa is about 12 pixels across against a 44px
minimum touch target. Transparency does not remove a shape from hit testing —
SVG decides that from whether `fill` and `stroke` are set at all, not from their
opacity — so the second layer is a target roughly 20px wider than the prefecture
and invisible. Measured in a browser at 1280px, Kagawa answers a click up to 16
pixels away from its label point, where its drawn width is about 8.

The hit layers are added largest first, so the smallest prefectures end up last
in the SVG and their strokes sit above their neighbours'. Ordering is by the
shoelace area of the real polygons rather than by bounding box, because Tokyo's
box spans a thousand kilometres of ocean while Tokyo itself is one of the
smallest prefectures — the exact case the ordering exists to get right.

**What it costs:** 47 more paths in the DOM, and a 20px stroke necessarily
overlaps its neighbours', so a tap in the gap between two prefectures resolves to
whichever is later in the SVG rather than to the nearer one.

**Where:** `web/src/map/LeafletPrefectureMap.tsx`.

---

## 22. Prefecture names labelled from a zoom threshold, not always

**Chosen:** All 47 labels are drawn at zoom 5 and above. Below it, only the
selected prefecture is labelled.

**Rejected:** Drawing all 47 at every zoom, and drawing none at all.

**Why:** CLAUDE.md asks for names rendered at the stored `ST_PointOnSurface`
points, and that is what the labels do — the point of storing them. But at the
zoom that fits Japan into a 375px viewport the whole country is about 200 pixels
across, and 47 names in that space overlap into something no one can read.

The threshold is 5 because that is where the split falls in practice, counted in
a browser at 375, 768, 1280 and 1920 pixels wide. Leaflet's default `zoomSnap` of
1 makes the initial fit land on a whole zoom level: 4 while the map pane is
narrower than roughly 500px, and 5 above it. A window from about 900px across
therefore opens with every label drawn, and anything narrower opens with none.
Set to 6 — the value that looks right by eye — no width a browser opens at shows
a label at all.

**What it costs:** A phone visitor sees an unlabelled map until they zoom in.
That is survivable only because the searchable list is a first-class view
carrying every name in English, Japanese and romaji, and because a tap always
labels what it selected.

**Where:** `web/src/layout.ts`, `web/src/map/LeafletPrefectureMap.tsx`.

---

## 23. Two requests for the whole dataset, and no use of `/api/prefectures/{id}`

**Chosen:** The frontend fetches `GET /api/prefectures` and `GET /api/mascots`
once each, joins them on the JIS code, and answers every later question from
memory. Search runs in the browser. The per-prefecture detail endpoint is not
called.

**Rejected:** Fetching a prefecture's detail on each selection, and passing the
search text to `/api/mascots?motif=`.

**Why:** The mascot list has to be fetched whole regardless, for a reason that is
not about the list view: the map colours a prefecture by whether any of its
mascots is `ManuallyVerified`, and the boundaries response carries only a count.
Once all 35 mascot records are in memory — a few tens of kilobytes — a request per
selection would fetch data the client already holds, and a request per keystroke
would make the search slower than the array scan it replaces.

**What it costs:** `/api/prefectures/{id}` and the two `/api/mascots` filters are
built, tested and unused by this frontend. They are the right shape for v2, where
thousands of municipal mascots make fetching everything the wrong default — and
that is the point at which this decision has to be revisited rather than
extended.

**Where:** `web/src/data/useAtlas.ts`, `web/src/api/client.ts`.

---

## 24. A single client-side retry on a 5xx, and the server bug behind it

**Chosen:** `getJson` retries once, after 300ms, when a response is 5xx. It does
not retry a 4xx.

**Why:** Output caching on `/api/prefectures` (entry 14) collapses concurrent
requests for the same cache key: the first request executes the endpoint and the
rest wait for its response. If that first client disconnects, its cancellation
token fires, the endpoint throws `TaskCanceledException`, and the waiting requests
are handed a 500 they did nothing to cause.

Reproducible with two shells and no browser, against a cold cache:

```bash
curl -s -m 0.06 'http://localhost:5180/api/prefectures?detail=high' -o /dev/null &
curl -s -o /dev/null -w '%{http_code}
' 'http://localhost:5180/api/prefectures?detail=high'
# 500
```

React's StrictMode starts and immediately aborts one request per effect in
development, which is that pattern exactly, on every cold start.

**What it costs:** The retry is a client-side accommodation for a server-side
defect, and it hides the symptom at the point where it would otherwise be
noticed. The defect is on the API and is not fixed by it: a follower request
should not inherit the leader's cancellation. Retrying is defensible here on its
own terms — these are idempotent GETs of static data — but it is not the fix.

**Where:** `web/src/api/client.ts`.

---

## 25. One breakpoint, read in one place

**Chosen:** `WIDE_VIEWPORT_QUERY` in `web/src/layout.ts` is `(min-width: 768px)`,
read by `App.tsx` and by nothing else. Three things hang off it: the `?detail=`
level requested from the boundaries endpoint, whether the detail view is a bottom
sheet or a side panel, and whether the map and the list are shown together or one
at a time.

**Rejected:** Deciding each of the three where it is needed, and a container
query per component.

**Why:** CLAUDE.md asks for the sheet-or-panel choice to be made up front rather
than retrofitted, and the same argument applies to the other two. All three are
answers to one question — is this a phone? — and answering it three times is three
places for the answer to drift. Reading it in `App.tsx` also means the detail
level is known before the first request goes out, which is why the media query is
read through `useSyncExternalStore` rather than an effect: an effect would fire
after the first render, and the first render is what starts the fetch.

**What it costs:** A viewport between 768px and about 900px gets the wide layout,
where the sidebar takes 360px and leaves the map about 400 — narrower than the map
gets on a phone. The layout is correct there but the map is cramped, and a second
breakpoint would fix it.

**Where:** `web/src/layout.ts`, `web/src/App.tsx`.

---

## 26. Stylelint lints the stylesheets, not ESLint's CSS plugin

**Chosen:** Stylelint, with `stylelint-config-recommended`, lints the CSS in `web/`.
ESLint lints the TypeScript. Prettier formats both.

**Rejected:** `@eslint/css`, the ESLint team's plugin that makes ESLint read CSS
files. It would make ESLint the one linter for both languages.

**Why:** The stylesheets must use `rem` for font sizes, spacing, container widths
and focus indicators, and `px` only for hairline borders. That rule is an
accessibility requirement, so a linter checks it. Stylelint's
`declaration-property-unit-allowed-list` takes a list of properties and the units
each one allows, so it can check every part of the rule. The only unit rule in
`@eslint/css`, `relative-font-units`, checks `font-size` and the `font` shorthand.
It does not check `padding`, `margin`, `gap`, `width`, `max-height`, `outline` or
`outline-offset`.

`stylelint-config-recommended` enables only rules that find errors, for example an
unknown property or a duplicate selector. Prettier decides the layout of the CSS,
so no layout rules are needed.

**What it costs:** A third tool in `web/`: one more development dependency, one
more config file, and two lint commands where `@eslint/css` would need one.
`npm run lint` runs both.

**Where:** `web/stylelint.config.js`, `web/eslint.config.js`, `web/.prettierrc.json`,
`web/package.json`.

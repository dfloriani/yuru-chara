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
its own polygon. Japan has several prefectures where it is not:

- **Nagasaki** consists of hundreds of islands around a deeply concave
  peninsula. Its centroid falls in open water.
- **Tokyo** includes the Izu and Ogasawara island chains, which extend roughly a
  thousand kilometres south. They move the centroid far out into the Pacific,
  a long way from the city.
- **Kagoshima** and **Okinawa** have the same problem for the same reason.

`ST_PointOnSurface` always returns a point that is on the geometry, so labels are
drawn on land.

Both values are computed at seed time rather than per request. They never change,
and recomputing them on every read of all 47 boundaries would be wasted work.

**What it costs:** `ST_PointOnSurface` returns one valid interior point, not
necessarily the one that looks best. For a horseshoe-shaped prefecture it can be
close to an edge. A pole-of-inaccessibility algorithm would produce a better
position and is considerably more work. `Centroid` is stored as well because it
remains the correct value for questions about which prefecture is nearest.

**Where:** `src/YuruChara.Domain/Prefectures/Prefecture.cs`.

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

**Where:** `.gitignore`, `data/` — **to be filled in at Checkpoint 2.**

---

## 13. Testcontainers rather than an in-memory provider — **pending (Checkpoint 3)**

Intended: integration tests run against a real, disposable PostGIS container.

To be written up when it is implemented. The reasoning: the EF Core in-memory
provider does not implement PostGIS, so every query this project depends on
(`ST_Simplify`, `ST_Contains`, and use of the GIST index) is exactly what it
cannot test. A single shared test database would work, but it makes tests
dependent on execution order and prevents running them in parallel. The cost is
that `dotnet test` then requires a running Docker daemon.

---

## 14. Output caching on the boundaries endpoint — **pending (Checkpoint 3)**

Intended: `/api/prefectures` is output-cached, varying by the `detail` query
parameter.

To be written up with a statement of what invalidates the cache. The reasoning:
the boundary data does not change between seed runs and is the largest response
the application returns, so the cache has a high hit rate and a measurable
effect.

---

## 15. Leaflet and PostGIS rather than Google data-driven styling — **pending (Checkpoint 4)**

The reasoning is set out in CLAUDE.md. To be restated here alongside the
frontend's map interface once that interface exists, including what the interface
costs and what it does not provide: the tile layer, the projection and the
interaction model are still visible to the rest of the frontend, so replacing
Leaflet would touch more than one component.

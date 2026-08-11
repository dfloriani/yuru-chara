# Yuru-Chara Map — Project Brief

## What this is

An interactive map of Japan's 47 prefectures, each showing its official
mascot character (yuru-chara / gotōchi-chara). Click a prefecture, see its
mascot's details.

## Code style requirements

Idiomatic and readable over clever. Non-obvious .NET or PostGIS choices should carry a short comment explaining _why_ that construct was used. This applies especially to hosted services, EF Core configuration, spatial functions, and caching.

## v1 scope

- 47 prefecture-level mascots only.
- Choropleth map of Japan; prefecture polygons, not pins.
- Click a prefecture → detail panel: mascot name (kana + romaji), motif,
  debut year, owning body, link to official site.
- **No mascot images.** See "Image licensing" below — this is a hard rule.
- Seed data committed to the repo. Live ingestion is v2.

## Explicit non-goals for v1

- Municipal / corporate mascots (thousands of them — v2).
- User accounts, favourites, voting.
- Yuru-Chara Grand Prix historical rankings (v2).
- Native mobile apps (MAUI, React Native). The web app itself is
  **mobile-first** — see below.
- Mascot images of any kind.

## Mobile-first — design constraint

Assume most visitors arrive on a phone. Design for a narrow viewport first
and treat desktop as the enhancement, not the reverse.

Specific problems this map has on a small screen, and how to handle them:

- **Tiny prefectures.** Kagawa and Osaka are small; Tokyo is mostly islands
  strung a thousand kilometres south. Precise polygon tapping at
  full-Japan zoom is miserable.
  - Give polygons a wide transparent stroke so the hit area exceeds the
    visible shape.
  - Provide a **searchable list view** of all 47 prefectures alongside the
    map. On a phone this may well be the primary interface, with the map as
    the visual. Do not treat the list as a fallback — build it properly.
- **Detail panel.** Bottom sheet on narrow viewports, side panel on wide.
  Decide this up front rather than retrofitting.
- **Simplification tolerance should vary by viewport.** Serve more
  aggressively simplified geometry to narrow screens. This is a query
  parameter on the boundaries endpoint, not a separate pipeline.
- **Labels.** Render prefecture names on the map at the stored
  `ST_PointOnSurface` points. This is what those points are for.

## Image licensing — hard rule

Do not add mascot images, illustrations, sprites, or AI-generated
lookalikes to this project. Mascot designs are copyrighted and trademarked
by the prefectures that own them, and photographs of costumed performers are
derivative works of those designs. Kumamon's royalty-free licence is a
conditional outlier, not the norm.

Instead, model licensing as data. Every mascot carries an
`ImageLicenseStatus`, and the UI renders accordingly:

```csharp
public enum ImageLicenseStatus
{
    Unknown = 0,              // not yet researched
    ApplicationRequired,      // prefecture requires a usage application
    OfficialMaterialsPublished, // prefecture publishes downloadable assets + terms
    CommonsFreeLicense,       // a genuinely free-licensed photo exists on Wikimedia Commons
    NoReuseGranted            // terms explicitly forbid third-party reuse
}
```

Where a mascot has no usable image, render the prefecture's motif as a
neutral icon plus the mascot name. This is a deliberate design decision, not
a gap to be filled later without checking terms.

## Stack

- **.NET 10** (LTS, Nov 2025 → Nov 2028). C# 14.
- **ASP.NET Core minimal APIs** — no MVC controllers.
- **EF Core 10** + `Npgsql.EntityFrameworkCore.PostgreSQL` +
  `NetTopologySuite` for spatial types.
- **PostgreSQL 17 + PostGIS**, via Docker Compose.
- **React 19 + TypeScript + Vite** frontend.
- **react-leaflet** for the map — see "Map library" below. Don't reach for
  MapLibre or deck.gl.
- **xUnit + Testcontainers** for integration tests against a real
  throwaway Postgres. The author has a test-automation background — tests are
  not an afterthought.
- **Docker Compose** for local dev.

## Map library — decision and rationale

Leaflet, rendering GeoJSON served from our own PostGIS database.

Google Maps Platform's **data-driven styling for boundaries** was considered
and rejected. It would work: `ADMINISTRATIVE_AREA_LEVEL_1` covers Japanese
prefectures, click events return the boundary's place ID and display name,
and you get Google's labels and zoom behaviour for free. It was rejected
because:

- It moves all spatial work off our backend. PostGIS is the point of this
  project, not incidental to it.
- Google's terms don't permit storing or caching their geometry, so there's
  no hedging — it's one or the other.
- It requires a billing account, an API key, and a vector Map ID.
- It keys our data on Google Place IDs rather than JIS prefecture codes,
  which are open and are what every Japanese dataset uses.

Consequence for the frontend: **keep the map component swappable.** The map
should consume our GeoJSON through a narrow interface, so that replacing
Leaflet with Google DDS later touches one component and no API code.

## Solution layout

```
/src
  /YuruChara.Api          — ASP.NET Core minimal API host
  /YuruChara.Domain       — entities, enums, value objects. No EF references.
  /YuruChara.Infrastructure — DbContext, EF configs, migrations, ingestion
  /YuruChara.Ingestion    — console/CLI for building seed data (v2 → worker)
/tests
  /YuruChara.Api.Tests
  /YuruChara.Domain.Tests
/web                      — React + TS + Vite
/data                     — committed seed JSON + GeoJSON
docker-compose.yml
DATA-SOURCES.md
```

Keep `Domain` free of EF Core and Npgsql references. Configure entities via
`IEntityTypeConfiguration<T>` in `Infrastructure`.

## Domain model

```
Prefecture
  Id (int)              — JIS prefecture code 1–47
  NameEn, NameJa, NameRomaji
  Region                — Hokkaidō, Tōhoku, Kantō, Chūbu, Kansai, Chūgoku, Shikoku, Kyūshū
  Boundary (MultiPolygon, SRID 4326)
  Centroid (Point, SRID 4326)  — for label placement
  Mascots                      — collection

Mascot
  Id (Guid)
  PrefectureId
  NameJa, NameRomaji
  Motif                 — free text: "pear", "bear", "samurai helmet"
  DebutYear (int?)
  OwningBody            — prefectural government, tourism board, etc.
  OfficialUrl (Uri?)
  IsOfficial (bool)     — some prefectures have popular unofficial mascots
  ImageLicenseStatus
  LicenseNotes (string?) — free text, e.g. link to the terms page
  VerificationLevel     — Automated | ManuallyVerified
  SourceCitations       — where each fact came from
```

`VerificationLevel` separates **coverage** from **confidence**, and the two
are not the same thing. The Wikidata pass populates as many prefectures as
it can at `Automated`. A hand-checked subset — roughly ten well-known
mascots, official URL and debut year confirmed against the owning body's
own site — gets promoted to `ManuallyVerified`. Surface the distinction in
the UI rather than hiding it.

`SourceCitations` matters: the data is stitched together from several
sources of differing reliability, and being able to say where a fact came
from is part of the point.

## Spatial specifics

Use these deliberately — they're the parts worth understanding:

- SRID **4326** throughout. Use `geometry`, not `geography`; at prefecture
  scale the difference doesn't matter and `geometry` has better operator
  support.
- **GIST index** on `Prefecture.Boundary`.
- **`ST_Simplify`** when serving boundaries to the browser. Raw Japanese
  prefecture geometry is heavy (Nagasaki alone has hundreds of islands).
  Serve simplified polygons; keep the full geometry in the database.
- **`ST_Contains`** for a "which prefecture is this point in?" endpoint —
  even though v1 has no UI for it, it's a two-line endpoint and it's the
  thing people ask about.
- **`ST_PointOnSurface`** computed once at seed time and stored, for map
  label placement. Use this rather than `ST_Centroid`: a centroid can fall
  outside a concave or island-heavy prefecture, and several of Japan's are
  both. Store `ST_Centroid` too if useful, but label from
  `ST_PointOnSurface`. Comment the difference — it's a good small thing to
  be able to explain.

Add a short comment anywhere raw SQL or a PostGIS function is used, saying
what it does and why EF Core LINQ wasn't enough.

## API

```
GET  /api/prefectures?detail=low|high   → GeoJSON FeatureCollection, simplification tolerance per detail level
GET  /api/prefectures/{id}             → detail incl. mascots
GET  /api/mascots                      → flat list, filterable by ?motif= &debutBefore=
GET  /api/mascots/{id}
GET  /api/prefectures/at?lat=&lng=     → ST_Contains lookup
GET  /health
```

Return GeoJSON directly for the map endpoint — `NetTopologySuite.IO.GeoJSON4STJ`
serialises NTS geometry through System.Text.Json.

Use **output caching** on `/api/prefectures` — the data is static and the
payload is the largest thing the app serves. Good, small, real use case.

## Data sources

Record everything in `DATA-SOURCES.md`: URL, licence, date retrieved, and
what was taken from it. Do not skip this file.

- **Prefecture boundaries**: source a Japan prefecture GeoJSON and **verify
  its licence before committing it**. Most derive from Japan's National Land
  Numerical Information (国土数値情報), which permits reuse with attribution.
  Record the attribution requirement in `DATA-SOURCES.md` and surface it in
  the UI footer.
- **Mascot data**: Wikidata Query Service (https://query.wikidata.org/) via
  SPARQL is the best structured source. Cross-check against English
  Wikipedia. The Fandom Yuru-chara wiki is community-maintained — usable to
  fill gaps, but mark those facts as lower-confidence in `SourceCitations`.
- **Do not invent mascot data.** If a prefecture's mascot can't be verified
  from a real source, set the fields to null and `ImageLicenseStatus.Unknown`
  rather than guessing. A visibly incomplete map is fine; a confidently wrong
  one is not.
- **Verification is tiered.** Everything Wikidata yields lands at
  `VerificationLevel.Automated`. Around ten well-known mascots get
  hand-checked against the owning body's own site and promoted to
  `ManuallyVerified`. Report which ones were promoted and what was corrected.

## Conventions

- Nullable reference types on. Warnings as errors.
- `record` for DTOs, `class` for entities.
- Minimal API endpoints grouped with `MapGroup`, one static class per group.
- EF Core migrations checked in.
- No `AutoMapper` — hand-write the mapping, it's more readable for someone
  learning the codebase.
- No repository pattern over `DbContext`. `DbContext` is already a unit of
  work; adding a layer here is the kind of thing that starts arguments.

## Commands

```bash
docker compose up -d                       # postgres + postgis
dotnet run --project src/YuruChara.Api
dotnet ef migrations add <Name> --project src/YuruChara.Infrastructure
dotnet test
cd web && npm run dev
```

## Decisions that must be documented in code

Each of the following is a deliberate choice with a viable alternative.
Wherever one appears, leave a comment or an entry in `DECISIONS.md` stating
the reasoning:

- Minimal APIs rather than controllers.
- `IEntityTypeConfiguration<T>` rather than data annotations.
- Testcontainers rather than an in-memory provider or a shared test database.
- `geometry` rather than `geography`.
- Output caching on the boundaries endpoint, and what invalidates it.
- No EF Core reference in the domain project.
- Leaflet + PostGIS over Google data-driven styling, and what that choice costs.
- `ST_PointOnSurface` rather than `ST_Centroid` for label placement.
- Coverage and confidence as two separate fields.

-- The postgis base image enables postgis_topology, fuzzystrmatch and
-- postgis_tiger_geocoder as well as postgis itself. Those add about 37 US census
-- tables to the public schema. None of them are used by this project, and they
-- make the output of `\dt` long enough to hide the two tables that are.
--
-- Scripts in /docker-entrypoint-initdb.d run in filename order, after the image's
-- own 10_postgis.sh, and only when an empty data directory is initialised. This
-- script only drops things: `postgis` itself is created by the EF Core migration
-- rather than here, so that the schema is fully described by the migrations.
DROP EXTENSION IF EXISTS postgis_tiger_geocoder CASCADE;
DROP EXTENSION IF EXISTS postgis_topology CASCADE;
DROP EXTENSION IF EXISTS fuzzystrmatch CASCADE;
DROP SCHEMA IF EXISTS tiger CASCADE;
DROP SCHEMA IF EXISTS tiger_data CASCADE;
DROP SCHEMA IF EXISTS topology CASCADE;

-- The postgis extension is dropped as well. The image creates it in advance,
-- which would hide the fact that the EF Core migration is what creates it.
-- Dropping it here means the first `dotnet ef database update` is what installs
-- PostGIS, in the same way it would against a managed PostgreSQL service that
-- enables no extensions in advance.
DROP EXTENSION IF EXISTS postgis CASCADE;

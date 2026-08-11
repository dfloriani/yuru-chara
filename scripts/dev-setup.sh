#!/usr/bin/env bash
#
# Prepares local development configuration.
#
# The database password is stored in exactly one place: the .NET user-secrets
# store, which lives outside this repository. This script reads it from there and
# writes the .env file that Docker Compose needs, so that the password is never
# typed into two places and cannot drift between them.
#
# On first run there is no secret yet, so the script generates a random password,
# stores it, and then writes .env from it.
#
# Run this once after cloning:
#
#     ./scripts/dev-setup.sh
#     docker compose up -d
#     dotnet run --project src/YuruChara.Api
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

API_PROJECT="src/YuruChara.Api"
SECRET_KEY="ConnectionStrings:YuruChara"
ENV_FILE=".env"

for required in dotnet docker openssl; do
    if ! command -v "$required" > /dev/null 2>&1; then
        echo "error: '$required' is required but was not found on PATH." >&2
        exit 1
    fi
done

# `dotnet user-secrets list` prints one "key = value" line per secret. Selecting
# the line for our key and removing the prefix leaves the connection string.
read_connection_string() {
    dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null \
        | sed -n "s/^${SECRET_KEY} = //p"
}

connection_string="$(read_connection_string)"

if [[ -z "$connection_string" ]]; then
    # 24 random bytes as hex. Generated locally and never displayed, so there is
    # no point at which it could be copied into a file by hand.
    generated_password="$(openssl rand -hex 24)"
    connection_string="Host=localhost;Port=5432;Database=yuruchara;Username=yuruchara;Password=${generated_password}"

    dotnet user-secrets set "$SECRET_KEY" "$connection_string" --project "$API_PROJECT" > /dev/null
    echo "Generated a database password and stored it in user secrets."
else
    echo "Reusing the connection string already in user secrets."
fi

# Split the connection string on ';' and return the value of one key. The
# connection string is written by this script, so the key names and their
# capitalisation are known and do not need case-insensitive matching.
connection_string_field() {
    printf '%s\n' "$connection_string" | tr ';' '\n' | sed -n "s/^$1=//p" | head -n 1
}

postgres_db="$(connection_string_field Database)"
postgres_user="$(connection_string_field Username)"
postgres_password="$(connection_string_field Password)"
postgres_port="$(connection_string_field Port)"

for name in postgres_db postgres_user postgres_password postgres_port; do
    if [[ -z "${!name}" ]]; then
        echo "error: could not read '$name' from the stored connection string." >&2
        echo "       Inspect it with: dotnet user-secrets list --project $API_PROJECT" >&2
        exit 1
    fi
done

cat > "$ENV_FILE" <<EOF
# GENERATED FILE. Do not edit, and do not commit it (.gitignore excludes it).
#
# Written by scripts/dev-setup.sh from the connection string in the .NET
# user-secrets store, which is the single place this password is kept. To change
# the password, update the secret and run that script again:
#
#     dotnet user-secrets set "$SECRET_KEY" "<connection string>" --project $API_PROJECT
#     ./scripts/dev-setup.sh
#
POSTGRES_DB=$postgres_db
POSTGRES_USER=$postgres_user
POSTGRES_PASSWORD=$postgres_password
POSTGRES_PORT=$postgres_port
EOF

# Readable only by the current user, matching how the user-secrets store is kept.
chmod 600 "$ENV_FILE"

echo "Wrote $ENV_FILE for Docker Compose (database '$postgres_db', user '$postgres_user', port $postgres_port)."

# POSTGRES_PASSWORD is read only when the data directory is created. If a volume
# already exists it keeps whatever password it was built with, so a changed
# password will not take effect until the volume is deleted.
if [[ -n "$(docker volume ls --filter name=yuruchara-pgdata --quiet 2>/dev/null)" ]]; then
    echo
    echo "A database volume already exists. PostgreSQL only applies POSTGRES_PASSWORD"
    echo "when it first creates the data directory, so if the password changed you must"
    echo "delete the volume before the new one takes effect:"
    echo
    echo "    docker compose down -v && docker compose up -d"
    echo
    echo "That deletes the database contents. Re-run the seeder afterwards."
fi

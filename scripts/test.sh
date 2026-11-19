#!/usr/bin/env sh
# Runs the tests against a throwaway PostgreSQL 16 in Docker (container folio-parking-pg on 127.0.0.1:5435).
# Each test class creates and drops its own database on that server. ConnectionStrings__App overrides it (CI sets it).
set -e
if [ -z "$ConnectionStrings__App" ]; then
  docker start folio-parking-pg >/dev/null 2>&1 ||
    docker run -d --name folio-parking-pg -p 127.0.0.1:5435:5432 -e POSTGRES_PASSWORD=app postgres:16 >/dev/null
  until docker exec folio-parking-pg pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
  export ConnectionStrings__App='Host=127.0.0.1;Port=5435;Username=postgres;Password=app'
fi
exec dotnet test "$@"

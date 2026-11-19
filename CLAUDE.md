# Folio Parking API

ASP.NET Core 9 minimal API for the Folio3 office parking app: employees release and claim parking spots, gate guards verify plates, administrators manage employees, settings and reports. PostgreSQL through EF Core (Npgsql).

## Commands

- Setup: `dotnet restore`
- Build: `dotnet build --no-restore`
- Test: `./scripts/test.sh` (starts a PostgreSQL 16 container `folio-parking-pg` on 127.0.0.1:5435 when `ConnectionStrings__App` is not set, then runs `dotnet test`; needs Docker)
- Fast test: `./scripts/test.sh --filter FullyQualifiedName~App.Tests.HealthTests`
- One test class: `./scripts/test.sh --filter FullyQualifiedName~App.Tests.<ClassName>`

There is no lint step: `dotnet format` is not enforced (existing files do not pass it). Do not reformat code you are not changing.

## Layout

- `App.Api/` the API. One folder per area (`Admin/`, `Auth/`, `Gate/`, `Parking/`, `Profile/`, `Reports/`), each with an `Endpoints.cs` module (`AdminEndpoints.cs` in `Admin/`) and its records.
- `App.Tests/` xUnit integration tests through `WebApplicationFactory`. `AppFactory` gives each test class its own empty database.
- `contracts/openapi.yaml` and `contracts/data-model.yaml` are the API contract and data model. Keep them in step with the code.

More: [ARCHITECTURE.md](ARCHITECTURE.md), [docs/conventions.md](docs/conventions.md).

## Rules

- Endpoints live in a class implementing `IApiEndpointModule`; it is found by reflection, so no registration is needed. See the `add-endpoint` skill.
- Every endpoint declares `.WithName(...)`, its authorization and every `Produces<...>` status: the OpenAPI document is generated from them.
- Errors are returned as `Error(message, CODE)` JSON with the matching status code.
- The schema is created by `EnsureCreated()` at startup; there are no migrations. A new table means an entity implementing `IAppEntity` (picked up automatically) or an explicit mapping in `AppDb.OnModelCreating`.
- PostgreSQL enums are mapped in `AppDb` and need a `CREATE CAST` in `Program.cs` and `SeedData.EnumCastsSql`.
- Never put a password or connection string in code; configuration comes from `ConnectionStrings__App` and other environment variables (see `.env.example`).
- Every behaviour change gets an integration test in `App.Tests/` using `IClassFixture<AppFactory>`.

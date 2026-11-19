# Coding conventions

- C# with nullable reference types and implicit usings on. File-scoped namespaces (`namespace App.Api;`).
- One public type per file, named after the file. Request and response shapes are `record`s next to the endpoints that use them.
- Endpoint modules are `internal sealed class <Area>Endpoints : IApiEndpointModule`. Handlers are private static methods that take their dependencies as parameters (`AppDb db`, `TimeProvider time`, `ClaimsPrincipal user`).
- JSON uses camelCase (the web defaults). Route names (`WithName`) are camelCase verbs, for example `getMyProfile`.
- Errors: `Results.Json(new Error("Human message.", "UPPER_SNAKE_CODE"), statusCode: ...)`.
- Time comes from the injected `TimeProvider`, never `DateTime.Now`, so tests can control it.
- Queries use EF Core LINQ; raw SQL only for PostgreSQL-specific DDL (casts, enums).
- Comments explain why, not what.
- Tests: one class per feature, `IClassFixture<AppFactory>`, test names describe the behaviour (`ReleasingTwiceForTheSameDateIsRejected`). Arrange data through the API where possible, otherwise through `AppDb` on `factory.ConnectionString`.

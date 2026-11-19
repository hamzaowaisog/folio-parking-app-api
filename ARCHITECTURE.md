# Architecture

```
Web app (separate repo) ──HTTP/JSON, Bearer token──▶ App.Api (ASP.NET Core 9 minimal API) ──EF Core/Npgsql──▶ PostgreSQL
```

## Request path

1. `Program.cs` configures OpenAPI, the `AppDb` context, Bearer authentication (`Auth/BearerAuthenticationHandler`), two policies (`Administrator`, `GateOrAdmin`) and CORS for the web app's origin.
2. At startup it creates the schema with `EnsureCreated()`, adds the text-to-enum casts, and seeds demo rows only when `Seed:Demo` is true.
3. Every `IApiEndpointModule` in the assembly is registered and mapped. Modules:
   - `Auth/`: sign-in, sessions (hashed session tokens), accounts and roles.
   - `Profile/`: the signed-in employee's profile and vehicles (`/me/...`).
   - `Parking/`: releasing a spot for a date, claiming a released spot, the dashboard and availability.
   - `Gate/`: plate verification for gate guards, with the indicator state.
   - `Admin/`: employees, administrators' access, parking settings (areas, daily release time, report recipients).
   - `Reports/`: parking reports by period, unused parking, verification lists.

## Data

`AppDb` holds employees, accounts and session tokens explicitly; other entities implement `IAppEntity` and are added by reflection. Four PostgreSQL enums (`employee_parking_status`, `account_role`, `parking_release_status`, `parking_claim_status`) are mapped in `AppDb`. The data model is documented in `contracts/data-model.yaml`.

## Contract

`contracts/openapi.yaml` is the agreed API contract with the web app. The build writes the generated document to `App.Api/openapi/built.json` (ignored by git), which can be compared with the contract.

## Tests

`App.Tests` starts the real app in-process with `WebApplicationFactory`. `AppFactory` creates a fresh database per test class on the server named by `ConnectionStrings__App` and drops it afterwards.

---
name: add-endpoint
description: Add or change an HTTP endpoint in the Folio Parking API, with its contract entry and integration test. Use when a ticket asks for a new route, a new field in a response, or a changed status code.
---

# Add or change an endpoint

1. **Find the area.** Endpoints live in `App.Api/<Area>/Endpoints.cs` (Admin, Auth, Gate, Parking, Profile, Reports). Add to the existing module; create a new `<Area>Endpoints : IApiEndpointModule` only for a genuinely new area. Modules are discovered by reflection.
2. **Map the route** in `Map(...)`:
   ```csharp
   endpoints.MapGet("/me/vehicles", ListVehicles)
       .WithName("listMyVehicles")
       .RequireAuthorization()               // or .RequireAuthorization("Administrator") / "GateOrAdmin"
       .Produces<VehicleList>(StatusCodes.Status200OK)
       .Produces<Error>(StatusCodes.Status401Unauthorized);
   ```
   Declare every status the handler can return.
3. **Shapes.** Add request/response `record`s in the same folder, one per file. New tables: an entity implementing `IAppEntity` (picked up by `AppDb`); a new PostgreSQL enum also needs `MapEnum`/`HasPostgresEnum` in `AppDb` and a `CREATE CAST` in `Program.cs` and `SeedData.EnumCastsSql`.
4. **Contract.** Update `contracts/openapi.yaml` (and `contracts/data-model.yaml` for data changes) so it matches the code.
5. **Test.** Add or extend a test class in `App.Tests/` with `IClassFixture<AppFactory>`: call the route through `factory.CreateClient()` and assert status and body, including the error cases.
6. **Run** `dotnet build --no-restore` and `./scripts/test.sh`.

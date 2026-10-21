using App.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddSchemaTransformer<AuthSchemaTransformer>();
    options.AddSchemaTransformer<App.Api.Admin.ReportQuerySchemaTransformer>();
    options.AddDocumentTransformer<App.Api.Admin.ReportQuerySchemaTransformer>();
});
// PostgreSQL: the connection comes from ConnectionStrings__App and nowhere else. There is no built-in login: an app that is
// not given one stops when it first needs the database, so it can never run on a password that is written in the code.
builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("App")
        ?? throw new InvalidOperationException("No database connection is configured. Set ConnectionStrings__App (for example Host=...;Port=5432;Database=...;Username=...;Password=...).")));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, BearerAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy => policy.RequireRole("parkingAdministrator", "Administrator", "administrator"));
    options.AddPolicy("GateOrAdmin", policy => policy.RequireRole("gateGuard", "GateGuard", "parkingAdministrator", "Administrator", "administrator"));
});
foreach (var moduleType in typeof(IApiEndpointModule).Assembly.GetTypes()
             .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IApiEndpointModule).IsAssignableFrom(type)))
{
    builder.Services.AddTransient(typeof(IApiEndpointModule), moduleType);
}
// the web app's address: 3000 unless the setting Cors:Origin names another (the factory sets it when it starts the web app on another port)
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:3000").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/admin/reports"
        && context.Request.Query.TryGetValue("period", out var values)
        && values.Count == 1
        && !string.IsNullOrEmpty(values[0]))
    {
        var period = values[0]!;
        var query = context.Request.Query.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        query["period"] = new StringValues(char.ToUpperInvariant(period[0]) + period[1..]);
        context.Request.QueryString = QueryString.Create(query);
    }

    await next();
});
// Create the database with every table when the app starts. The build also runs this file, to write the API's OpenAPI
// document, and must not touch the database then. Keep this: the factory starts the app and reads the database it creates.
if (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
{
    // one at a time: the tests start several copies of the app in one process, and two creating the tables at once collide
    lock (typeof(AppDb))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_cast
                    WHERE castsource = 'text'::regtype
                      AND casttarget = 'employee_parking_status'::regtype
                ) THEN
                    EXECUTE 'CREATE CAST (text AS employee_parking_status) WITH INOUT AS IMPLICIT';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_cast
                    WHERE castsource = 'text'::regtype
                      AND casttarget = 'account_role'::regtype
                ) THEN
                    EXECUTE 'CREATE CAST (text AS account_role) WITH INOUT AS IMPLICIT';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_cast
                    WHERE castsource = 'text'::regtype
                      AND casttarget = 'parking_release_status'::regtype
                ) THEN
                    EXECUTE 'CREATE CAST (text AS parking_release_status) WITH INOUT AS IMPLICIT';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_cast
                    WHERE castsource = 'text'::regtype
                      AND casttarget = 'parking_claim_status'::regtype
                ) THEN
                    EXECUTE 'CREATE CAST (text AS parking_claim_status) WITH INOUT AS IMPLICIT';
                END IF;
            END
            $$;
            """);
        // sample rows, only when asked for (Seed__Demo=true): tests and the factory's own checks start on an empty database
        if (app.Configuration.GetValue<bool>("Seed:Demo")) SeedData.Run(db, app.Configuration["Seed:Password"]);
    }
}
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    logger.LogError(exception, "An unhandled API error occurred for {Path}", context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new Error("An unexpected error occurred.", "INTERNAL_SERVER_ERROR"));
}));
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => "API is up").ExcludeFromDescription();
foreach (var module in app.Services.GetServices<IApiEndpointModule>())
{
    module.Map(app);
}
// The API's own document (/openapi/v1.json) and a page to read and try it (/swagger). Keep both: the factory's product page links to the page.
app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "API"));

app.Run();

public partial class Program { }

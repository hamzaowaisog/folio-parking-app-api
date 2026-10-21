using App.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
// PostgreSQL: the connection comes from ConnectionStrings__App and nowhere else. There is no built-in login: an app that is
// not given one stops when it first needs the database, so it can never run on a password that is written in the code.
builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException("No database connection is configured. Set ConnectionStrings__App (for example Host=...;Port=5432;Database=...;Username=...;Password=...).")));
// the web app's address: 3000 unless the setting Cors:Origin names another (the factory sets it when it starts the web app on another port)
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:3000").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
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
        // sample rows, only when asked for (Seed__Demo=true): tests and the factory's own checks start on an empty database
        if (app.Configuration.GetValue<bool>("Seed:Demo")) SeedData.Run(db);
    }
}
app.UseCors();
app.MapGet("/", () => "API is up").ExcludeFromDescription();
// The API's own document (/openapi/v1.json) and a page to read and try it (/swagger). Keep both: the factory's product page links to the page.
app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "API"));

app.Run();

public partial class Program { }

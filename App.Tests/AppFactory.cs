using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace App.Tests;

/// <summary>
/// The app for one test class, on a database of its own that starts empty: no other test class, and no earlier run, can leave
/// rows in it. Use it as the class fixture: <c>public class OrdersTests : IClassFixture&lt;AppFactory&gt;</c>. A test that
/// restarts the app makes a second one on the same database: <c>using var again = factory.Restarted();</c>
/// The server and login come from ConnectionStrings__App, which the factory sets; only the database is new.
/// </summary>
public class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _server;
    private readonly bool _owns;
    public string ConnectionString { get; }

    public AppFactory() : this(null) { }

    private AppFactory(string? database)
    {
        _server = Environment.GetEnvironmentVariable("ConnectionStrings__App")
            ?? throw new InvalidOperationException("ConnectionStrings__App is not set: the tests need a PostgreSQL server.");
        _owns = database is null;
        var name = database ?? $"t_{Guid.NewGuid():N}";
        if (_owns) Exec(_server, $"CREATE DATABASE \"{name}\"");
        ConnectionString = new NpgsqlConnectionStringBuilder(_server) { Database = name }.ConnectionString;
    }

    /// <summary>The app started again on this class's database, as after a restart.</summary>
    public AppFactory Restarted() => new(new NpgsqlConnectionStringBuilder(ConnectionString).Database);

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:App"] = ConnectionString }));

    // xUnit ends a class fixture with DisposeAsync; Dispose covers a test that ends one itself with using
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Drop();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Drop();
    }

    private bool _dropped;
    private void Drop()
    {
        if (!_owns || _dropped) return;
        _dropped = true;
        NpgsqlConnection.ClearAllPools();
        Exec(_server, $"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(ConnectionString).Database}\" WITH (FORCE)");
    }

    private static void Exec(string connection, string sql)
    {
        using var c = new NpgsqlConnection(connection);
        c.Open();
        using var cmd = new NpgsqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }
}

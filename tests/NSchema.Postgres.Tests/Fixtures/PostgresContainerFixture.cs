using Npgsql;
using Testcontainers.PostgreSql;

namespace NSchema.Postgres.Tests.Fixtures;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <summary>
    /// The full connection string (including the password, which <see cref="NpgsqlDataSource.ConnectionString"/>
    /// redacts) — for tests that hand a connection string to the plugin's <c>Configure</c>.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        DataSource = NpgsqlDataSource.Create(ConnectionString);

        await using var conn = await DataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext;";
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

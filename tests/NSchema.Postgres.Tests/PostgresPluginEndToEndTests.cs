using NSchema.Configuration;
using NSchema.Operations.Apply;
using NSchema.Postgres.Sql;
using NSchema.Postgres.Tests.Fixtures;

namespace NSchema.Postgres.Tests;

/// <summary>
/// End-to-end proof that the <see cref="PostgresPlugin"/> manifest wires a fully working provider: it runs a real
/// migration THROUGH the plugin's <c>Configure</c> (not the direct <c>UseCurrentSchemaPostgres</c> API) against a real
/// PostgreSQL container, then re-introspects to confirm the schema was applied. Requires Docker.
/// </summary>
[Collection("postgres")]
public sealed class PostgresPluginEndToEndTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture = fixture;
    private readonly string _schema = $"e2e_{Guid.NewGuid():N}";
    private string _projectDir = null!;

    public ValueTask InitializeAsync()
    {
        _projectDir = Directory.CreateTempSubdirectory("nschema-pg-e2e-").FullName;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Directory.Delete(_projectDir, recursive: true);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Apply_ThroughThePlugin_CreatesTheDesiredSchema()
    {
        // Arrange — a desired schema on disk, and a host configured ONLY through the plugin manifest.
        await File.WriteAllTextAsync(Path.Combine(_projectDir, "schema.sql"), $"""
            CREATE SCHEMA {_schema};

            CREATE TABLE {_schema}.widgets (
              id   bigint NOT NULL,
              name text,
              CONSTRAINT widgets_pkey PRIMARY KEY (id)
            );
            """, TestContext.Current.CancellationToken);

        var builder = NSchemaApplication.CreateBuilder(new NSchemaApplicationOptions { ExceptionBehavior = ExceptionBehavior.Throw });
        var configured = new PostgresPlugin().Configure(builder, new ConfigBlock("provider", "postgres", new Dictionary<string, ConfigValue>
        {
            ["connection_string"] = ConfigValue.OfString(_fixture.ConnectionString),
        }));
        configured.Succeeded.ShouldBeTrue();

        builder.AddDdlSchemas(_projectDir);
        using var app = builder.Build();

        // Act — a real apply through the plugin-wired provider.
        await app.Apply(new ApplyArguments { Schemas = [_schema] }, TestContext.Current.CancellationToken);

        // Assert — the table really exists, read back via a fresh introspection.
        var live = await new PostgresSchemaProvider(_fixture.DataSource).GetSchema([_schema], TestContext.Current.CancellationToken);
        var table = live.Schemas.ShouldHaveSingleItem().Tables.ShouldHaveSingleItem();
        table.Name.ShouldBe("widgets");
        table.Columns.Select(column => column.Name).ShouldBe(["id", "name"]);
    }
}

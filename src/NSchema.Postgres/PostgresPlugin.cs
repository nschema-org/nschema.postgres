using NSchema.Configuration;
using NSchema.Plugins;

namespace NSchema.Postgres;

/// <summary>
/// The NSchema plugin manifest for the PostgreSQL provider.
/// </summary>
public sealed class PostgresPlugin : INSchemaProviderPlugin
{
    private const string EnvConnectionString = "NSCHEMA_POSTGRES_CONNECTION_STRING";
    private const string EnvUsername = "NSCHEMA_POSTGRES_USERNAME";
    private const string EnvPassword = "NSCHEMA_POSTGRES_PASSWORD";

    /// <inheritdoc />
    public string Label => "postgres";

    /// <inheritdoc />
    public string GetScaffoldTemplate(ScaffoldContext context)
    {
        var lines = new List<string> { "PROVIDER postgres (" };
        if (context.Version is { } version)
        {
            lines.Add($"  version           = '{version}',");
        }

        lines.Add($"  -- Prefer the {EnvConnectionString} environment variable, which");
        lines.Add("  -- overrides the value below.");
        lines.Add("  connection_string = ''");
        lines.Add("  -- Credentials may be supplied separately from the connection string (e.g. from");
        lines.Add($"  -- a secret store) via {EnvUsername} / {EnvPassword}.");
        lines.Add("  -- They override any user/password embedded in connection_string.");
        lines.Add(");");
        return string.Join("\n", lines);
    }

    /// <inheritdoc />
    public string GetSampleSchema() =>
        """
        CREATE SCHEMA app;

        CREATE TABLE app.widgets (
          id   bigint NOT NULL,
          name text,
          CONSTRAINT widgets_pkey PRIMARY KEY (id)
        );
        """;

    /// <inheritdoc />
    public PluginConfigureResult Configure(NSchemaApplicationBuilder builder, ConfigBlock block)
    {
        var errors = new List<string>();
        var connectionString = "";
        string? username = null;
        string? password = null;
        int? commandTimeout = null;

        foreach (var (key, value) in block.Attributes)
        {
            switch (key.ToLowerInvariant())
            {
                case "connection_string":
                    connectionString = value.AsString();
                    break;
                case "username":
                    username = value.AsString();
                    break;
                case "password":
                    password = value.AsString();
                    break;
                case "command_timeout":
                    if (value.Kind is ConfigValueKind.Integer)
                    {
                        commandTimeout = (int)value.AsInteger();
                    }
                    else
                    {
                        errors.Add("PROVIDER postgres: command_timeout must be an integer.");
                    }

                    break;
                default:
                    errors.Add($"PROVIDER postgres: unknown attribute '{key}'.");
                    break;
            }
        }

        // Credentials may be supplied out of band (e.g. a secret store); the environment overrides the block.
        connectionString = Environment.GetEnvironmentVariable(EnvConnectionString) ?? connectionString;
        username = Environment.GetEnvironmentVariable(EnvUsername) ?? username;
        password = Environment.GetEnvironmentVariable(EnvPassword) ?? password;

        if (string.IsNullOrEmpty(connectionString))
        {
            errors.Add($"PROVIDER postgres: connection_string is required. Set it via the {EnvConnectionString} environment variable or the block attribute.");
        }

        if (commandTimeout is < 0)
        {
            errors.Add("PROVIDER postgres: command_timeout must not be negative.");
        }

        if (errors.Count > 0)
        {
            return PluginConfigureResult.Failure([.. errors]);
        }

        builder.UseCurrentSchemaPostgres(dataSource =>
        {
            // Order matters: assigning ConnectionString re-parses the whole string, so it must precede the discrete overrides.
            dataSource.ConnectionStringBuilder.ConnectionString = connectionString;
            if (username is not null)
            {
                dataSource.ConnectionStringBuilder.Username = username;
            }

            if (password is not null)
            {
                dataSource.ConnectionStringBuilder.Password = password;
            }

            if (commandTimeout is { } timeout)
            {
                dataSource.ConnectionStringBuilder.CommandTimeout = timeout;
            }
        });

        return PluginConfigureResult.Success;
    }
}

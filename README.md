# NSchema.Postgres

PostgreSQL provider for [NSchema](https://github.com/tom-wolfe/NSchema), the declarative database schema migration library for .NET.

This package plugs PostgreSQL-specific implementations of NSchema's `ISchemaProvider` (live-database introspection) and `ISqlPlanner` (DDL generation) into your application, and contributes Postgres-only `SqlType` helpers.

## Getting started

Install the core package and this provider:

```bash
dotnet add package NSchema
dotnet add package NSchema.Postgres
```

Register Postgres against an `NSchemaApplicationBuilder`:

```csharp
using NSchema;
using NSchema.Postgres;

var builder = NSchemaApplication.CreateBuilder(args);

builder
    .AddSchemasFromAssemblyContaining<Program>()
    .UsePostgres(connectionString);

var app = builder.Build();
await app.Apply();
```

On startup NSchema introspects the database through this provider, diffs it against your declared schema, and applies the resulting plan.

## Configuration

`UsePostgres` has four overloads. The three connection-aware overloads register an `NpgsqlDataSource` for you (via `AddNpgsqlDataSource`) and wire up the schema provider and SQL planner; the no-arg overload assumes you've already registered an `NpgsqlDataSource` yourself.

```csharp
// 1. Connection string.
builder.UsePostgres("Host=localhost;Database=app;Username=postgres;Password=postgres");

// 2. Configure the data source builder directly.
builder.UsePostgres(b => b
    .EnableDynamicJson()
    .UseLoggerFactory(loggerFactory)
);

// 3. As above, with access to the IServiceProvider.
builder.UsePostgres((sp, b) => b.UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>()));

// 4. Bring your own data source.
builder.Services.AddNpgsqlDataSource(connectionString);
builder.UsePostgres();
```

## Postgres-specific types

`SqlTypePostgresExtensions` adds Postgres-only members to `SqlType`:

| Member           | Postgres type | Notes                                                   |
|------------------|---------------|---------------------------------------------------------|
| `SqlType.Citext` | `citext`      | Case-insensitive text. Requires the `citext` extension. |
| `SqlType.Jsonb`  | `jsonb`       | Binary JSON.                                            |

```csharp
users.Column("email", SqlType.Citext).NotNull();
users.Column("metadata", SqlType.Jsonb);
```

`citext` requires `CREATE EXTENSION citext;` in the target database — NSchema.Postgres does not create extensions for you.

## Supported schema objects

The introspector reads, and the planner emits DDL for:

- Schemas (including comments and `GRANT`s on the schema itself)
- Tables (including comments and table-level `GRANT`s)
- Columns (types, nullability, defaults, comments)
- Primary keys
- Foreign keys
- Indexes, including unique indexes and index comments

## Requirements

- .NET 10
- PostgreSQL (tested against `postgres:17-alpine` via Testcontainers)

## License

See [LICENSE](LICENSE).

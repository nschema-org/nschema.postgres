# ![NSchema](https://raw.githubusercontent.com/nschema-org/NSchema.Docs/main/assets/nschema-logo-horizontal.png)

[![NSchema.Postgres](https://github.com/nschema-org/NSchema.Postgres/actions/workflows/cicd.yml/badge.svg)](https://github.com/nschema-org/NSchema.Postgres/actions/workflows/cicd.yml)

# NSchema.Postgres

PostgreSQL provider for [NSchema](https://github.com/nschema-org/NSchema), the declarative database schema migration tool for .NET. It plugs PostgreSQL introspection and DDL generation into NSchema, and adds Postgres-only `SqlType` helpers (`citext`, `jsonb`).

Most users should use the [NSchema CLI](https://github.com/nschema-org/NSchema), which already includes this provider. Add this package directly only when [embedding the engine](https://nschema.dev/library/embedding/) in your own application.

## Installation

```sh
dotnet add package NSchema.Core
dotnet add package NSchema.Postgres
```

## Documentation

Full documentation lives at **[nschema.dev](https://nschema.dev)**:

- [PostgreSQL provider](https://nschema.dev/providers/postgres/) — configuration, library usage, and Postgres-specific types
- [Embedding the engine](https://nschema.dev/library/embedding/)

## License

See [LICENSE](LICENSE).

# Changelog

All notable changes to NSchema will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project will adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) once a 1.0 release is published.

## [Unreleased] - 2026-05-27

First stable release of the PostgreSQL provider for NSchema, tracking the 1.0 release of NSchema itself.

### Added

- `UsePostgres(...)` extensions on `NSchemaApplicationBuilder` for registering the provider — overloads for a connection string, an `NpgsqlDataSourceBuilder` configuration delegate, the same with `IServiceProvider` access, and a no-arg form for bring-your-own `NpgsqlDataSource`.
- `PostgresSchemaProvider` — `ISchemaProvider` implementation that reads the live database via `information_schema` and `pg_catalog`, with optional schema-name scoping. Reads schemas, tables, columns, primary keys, foreign keys, indexes, comments (on schemas, tables, columns, and indexes), and `GRANT`s (on schemas and tables).
- `PostgresSqlPlanner` — `ISqlPlanner` implementation that translates an NSchema `MigrationPlan` into PostgreSQL DDL.
- `SqlType.Citext` and `SqlType.Jsonb` Postgres-specific type helpers on `SqlType`.
- SourceLink and symbol packages (`.snupkg`) published alongside the main package for source-level debugging.

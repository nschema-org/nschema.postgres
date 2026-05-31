# Changelog

All notable changes to NSchema will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project (mostly) adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Versioning policy

This package uses **lockstep major versioning** with the core NSchema package: `NSchema.Postgres X.*.*` requires `NSchema X.*.*`, so version compatibility is always clear.

As a consequence, breaking changes that are specific to this provider (rather than the core API) are signalled by a **minor version bump** rather than a major one, and called out explicitly in this changelog.

## [Unreleased]

### Changed

- **Breaking:** Updated to NSchema 2.0.0, which includes some breaking changes to the core NSchema API. This provider's public API is unchanged, but you may need to update your application code to accommodate the NSchema changes.
- **Breaking:** The `UsePostgres` methods have been renamed to `UseCurrentSchemaPostgres` to be more explicit about what you're configuring.

## [1.0.0] - 2026-05-27

First stable release of the PostgreSQL provider for NSchema, tracking the 1.0 release of NSchema itself.

### Added

- `UsePostgres(...)` extensions on `NSchemaApplicationBuilder` for registering the provider — overloads for a connection string, an `NpgsqlDataSourceBuilder` configuration delegate, the same with `IServiceProvider` access, and a no-arg form for bring-your-own `NpgsqlDataSource`.
- `PostgresSchemaProvider` — `ISchemaProvider` implementation that reads the live database via `information_schema` and `pg_catalog`, with optional schema-name scoping. Reads schemas, tables, columns, primary keys, foreign keys, indexes, comments (on schemas, tables, columns, and indexes), and `GRANT`s (on schemas and tables).
- `PostgresSqlPlanner` — `ISqlPlanner` implementation that translates an NSchema `MigrationPlan` into PostgreSQL DDL.
- `SqlType.Citext` and `SqlType.Jsonb` Postgres-specific type helpers on `SqlType`.
- SourceLink and symbol packages (`.snupkg`) published alongside the main package for source-level debugging.

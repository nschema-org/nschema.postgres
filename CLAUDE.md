# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

NSchema.Postgres is the PostgreSQL provider for [NSchema](https://github.com/nschema-org/NSchema), a schema-migration framework. It plugs PostgreSQL-specific implementations of NSchema's `ISchemaProvider` (introspection) and `ISqlGenerator` (DDL generation) into the host application via `NSchemaApplicationBuilder.UsePostgres(...)`.

Target framework: `net10.0`. C# `LangVersion=latest` with nullable reference types and `TreatWarningsAsErrors=true`.

## Commands

- Build: `dotnet build NSchema.Postgres.slnx`
- Test (all): `dotnet test NSchema.Postgres.slnx`
- Test (single): `dotnet test --filter "FullyQualifiedName~PostgresSqlPlannerTests.MethodName"`
- Pack (matches CI output): `dotnet pack src/NSchema.Postgres/NSchema.Postgres.csproj -c Release`

Integration tests use **Testcontainers** to spin up `postgres:17-alpine` — Docker must be running locally. The fixture also enables the `citext` extension on startup (see `tests/.../Fixtures/PostgresContainerFixture.cs`).

CI/CD runs through an external orchestrator at `nschema-org/NSchema` (`build/build/NSchema.Build`) rather than raw `dotnet` commands — see `.github/workflows/cicd.yml`. The build pipeline expects `Build__ProjectFile=src/NSchema.Postgres/NSchema.Postgres.csproj`.

## Architecture

Two service registrations make up the entire public surface; everything else is `internal`:

- **`PostgresSchemaProvider`** (`Sql/PostgresSchemaProvider.cs`) — reads the live database via `information_schema` / `pg_catalog` queries and assembles an NSchema `DatabaseSchema`. It runs a fixed sequence of independent queries (tables, columns, PKs, FKs, unique/check constraints, indexes, comments, grants, and views) against a single `NpgsqlConnection` opened from the injected `NpgsqlDataSource`. Each query is parameterized by an optional schema-name filter; `null` / empty means "all visible schemas". Row DTOs live in `Models/`. View bodies come from `pg_get_viewdef` (the DB's canonical form, with the trailing terminator stripped) so that `apply` → `plan` round-trips without phantom diffs; view dependencies are read authoritatively from `pg_rewrite` / `pg_depend` (not parsed from the body) and feed the linearizer's drop ordering.
- **`PostgresSqlGenerator`** (`Sql/PostgresSqlGenerator.cs`) — implements `ISqlGenerator`, translating an NSchema `MigrationPlan` into PostgreSQL DDL. A view `CreateView` (used for both an add and a body modify) emits `CREATE OR REPLACE VIEW`; an incompatible output-column change is rejected loudly by Postgres rather than silently dropping dependents.

`NSchemaApplicationBuilderExtensions` uses C# 14 **extension blocks** (`extension(NSchemaApplicationBuilder builder) { ... }`) — not classic `this`-parameter extension methods. Editing this file requires `LangVersion=latest` / .NET 10 SDK.

Central package management is on (`Directory.Packages.props` with `ManagePackageVersionsCentrally=true` and `CentralPackageTransitivePinningEnabled=true`) — add versions there, not in csproj files.

`SqlTypePostgresExtensions.cs` maps NSchema's abstract `SqlType` to Postgres-specific type names; the test fixture's `CREATE EXTENSION citext` exists because the type map includes `citext`.

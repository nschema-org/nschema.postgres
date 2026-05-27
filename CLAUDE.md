# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

NSchema.Postgres is the PostgreSQL provider for [NSchema](https://github.com/nschema-org/NSchema), a schema-migration framework. It plugs PostgreSQL-specific implementations of NSchema's `ISchemaProvider` (introspection) and `ISqlPlanner` (DDL generation) into the host application via `NSchemaApplicationBuilder.UsePostgres(...)`.

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

- **`PostgresSchemaProvider`** (`Migration/PostgresSchemaProvider.cs`) — reads the live database via `information_schema` / `pg_catalog` queries and assembles an NSchema `DatabaseSchema`. It runs a fixed sequence of independent queries (tables, columns, PKs, FKs, indexes, comments, grants) against a single `NpgsqlConnection` opened from the injected `NpgsqlDataSource`. Each query is parameterized by an optional schema-name filter; `null` / empty means "all visible schemas". Row DTOs live in `Models/`.
- **`PostgresSqlPlanner`** (`Migration/PostgresSqlPlanner.cs`) — translates an NSchema schema diff into PostgreSQL DDL.

`NSchemaApplicationBuilderExtensions` uses C# 14 **extension blocks** (`extension(NSchemaApplicationBuilder builder) { ... }`) — not classic `this`-parameter extension methods. Editing this file requires `LangVersion=latest` / .NET 10 SDK.

Central package management is on (`Directory.Packages.props` with `ManagePackageVersionsCentrally=true` and `CentralPackageTransitivePinningEnabled=true`) — add versions there, not in csproj files.

`SqlTypePostgresExtensions.cs` maps NSchema's abstract `SqlType` to Postgres-specific type names; the test fixture's `CREATE EXTENSION citext` exists because the type map includes `citext`.

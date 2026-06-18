namespace NSchema.Postgres.Models;

internal sealed record CompositeTypeRow(
    string Schema,
    string Name,
    string? Comment);

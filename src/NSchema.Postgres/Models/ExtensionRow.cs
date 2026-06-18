namespace NSchema.Postgres.Models;

internal sealed record ExtensionRow(
    string Name,
    string? Version,
    string? Comment);

namespace NSchema.Postgres.Models;

internal sealed record ViewRow(
    string Schema,
    string Name,
    string Definition,
    bool IsMaterialized);

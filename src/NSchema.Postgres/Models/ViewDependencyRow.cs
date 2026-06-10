namespace NSchema.Postgres.Models;

/// <summary>One relation a view reads, as reported by the catalog (one row per referenced relation).</summary>
internal sealed record ViewDependencyRow(
    string ViewSchema,
    string ViewName,
    string RefSchema,
    string RefName);

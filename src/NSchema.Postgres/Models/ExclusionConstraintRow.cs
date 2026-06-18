namespace NSchema.Postgres.Models;

/// <summary>
/// A row of exclusion-constraint metadata. Elements are carried positionally: each is a column name or an
/// expression (per <see cref="IsExpressions"/>) paired with the operator in <see cref="Operators"/>.
/// </summary>
internal sealed record ExclusionConstraintRow(
    string TableSchema,
    string TableName,
    string ConstraintName,
    string? Method,
    string? Predicate,
    string[] ElementTexts,
    bool[] IsExpressions,
    string[] Operators);

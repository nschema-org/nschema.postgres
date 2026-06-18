namespace NSchema.Postgres.Models;

/// <summary>
/// A row of index metadata. Columns are carried positionally: the first <see cref="NumKeyAtts"/> entries are the
/// index keys (a column name or an expression, per <see cref="IsExpressions"/>, with ordering in
/// <see cref="Options"/>); the remainder are covering <c>INCLUDE</c> columns.
/// </summary>
internal sealed record IndexRow(
    string SchemaName,
    string TableName,
    string IndexName,
    bool IsUnique,
    string? Method,
    int NumKeyAtts,
    string? Predicate,
    string[] ColumnTexts,
    bool[] IsExpressions,
    int[] Options);

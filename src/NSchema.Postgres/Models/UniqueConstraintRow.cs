namespace NSchema.Postgres.Models;

internal sealed record UniqueConstraintRow(
    string TableSchema,
    string TableName,
    string ConstraintName,
    string[] ColumnNames
);

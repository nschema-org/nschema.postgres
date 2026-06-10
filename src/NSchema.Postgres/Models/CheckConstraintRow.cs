namespace NSchema.Postgres.Models;

internal sealed record CheckConstraintRow(
    string TableSchema,
    string TableName,
    string ConstraintName,
    string Expression
);

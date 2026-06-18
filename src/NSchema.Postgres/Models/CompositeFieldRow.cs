namespace NSchema.Postgres.Models;

internal sealed record CompositeFieldRow(
    string Schema,
    string TypeName,
    string FieldName,
    int OrdinalPosition,
    string DataType,
    string UdtName,
    int? MaxLength,
    int? Precision,
    int? Scale);

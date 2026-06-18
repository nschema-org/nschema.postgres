namespace NSchema.Postgres.Models;

internal sealed record DomainRow(
    string Schema,
    string Name,
    string DataType,
    string UdtName,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool NotNull,
    string? Default,
    string? Comment);

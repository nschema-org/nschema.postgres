namespace NSchema.Postgres.Models;

internal sealed record EnumRow(string Schema, string Name, string[] Values);

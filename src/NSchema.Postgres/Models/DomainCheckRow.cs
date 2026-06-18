namespace NSchema.Postgres.Models;

internal sealed record DomainCheckRow(
    string Schema,
    string DomainName,
    string CheckName,
    string Expression);

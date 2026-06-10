namespace NSchema.Postgres.Models;

/// <summary>One standalone sequence with the raw catalog option values (engine defaults not yet normalized).</summary>
internal sealed record SequenceRow(
    string Schema,
    string Name,
    string DataType,
    long Start,
    long Increment,
    long MinValue,
    long MaxValue,
    long Cache,
    bool Cycle);

namespace NSchema.Postgres.Models;

/// <summary>
/// One function or procedure, already split into the model's verbatim parts: the argument list (the text
/// inside the parentheses) and the definition (everything after the closing parenthesis).
/// </summary>
internal sealed record RoutineRow(
    string Schema,
    string Name,
    string Arguments,
    string Definition);

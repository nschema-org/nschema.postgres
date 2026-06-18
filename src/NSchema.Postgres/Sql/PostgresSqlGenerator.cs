using NSchema.Plan.Model;
using NSchema.Schema.Model;
using NSchema.Sql;
using NSchema.Sql.Model;

namespace NSchema.Postgres.Sql;

internal sealed class PostgresSqlGenerator : ISqlGenerator
{
    public SqlPlan Generate(MigrationPlan plan)
    {
        var preDeploymentStatements = plan.PreDeploymentScripts.Select(s => new SqlStatement(s.Sql, s.RunOutsideTransaction));
        var postDeploymentStatements = plan.PostDeploymentScripts.Select(s => new SqlStatement(s.Sql, s.RunOutsideTransaction));
        var statements = plan.Actions.SelectMany(GenerateStatements).ToList();
        List<SqlStatement> allStatements = [.. preDeploymentStatements, .. statements, .. postDeploymentStatements];
        return new SqlPlan(allStatements);
    }

    // ── SQL generation ────────────────────────────────────────────────────────

    private static IEnumerable<SqlStatement> GenerateStatements(MigrationAction action) => action switch
    {
        // A value added by ALTER TYPE … ADD VALUE cannot be used until the transaction that added it commits, so
        // the statement is carved out of the surrounding transaction. The executor commits the pending segment,
        // runs it alone, and resumes — ordering relative to later statements that use the value is preserved.
        AddEnumValue x => [new SqlStatement(BuildAddEnumValue(x), RunOutsideTransaction: true)],
        RecreateFunction x => BuildRecreateRoutine("FUNCTION", x.SchemaName, x.Function.Name, x.Function.Arguments, x.Function.Definition, x.Function.Comment),
        RecreateProcedure x => BuildRecreateRoutine("PROCEDURE", x.SchemaName, x.Procedure.Name, x.Procedure.Arguments, x.Procedure.Definition, x.Procedure.Comment),
        _ => [new SqlStatement(GenerateSql(action))],
    };

    private static string GenerateSql(MigrationAction action) => action switch
    {
        CreateSchema x => $"CREATE SCHEMA IF NOT EXISTS \"{x.SchemaName}\"",
        DropSchema x => $"""DROP SCHEMA "{x.SchemaName}" CASCADE""",
        RenameSchema x => $"ALTER SCHEMA \"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        CreateTable x => BuildCreateTable(x),
        DropTable x => $"DROP TABLE \"{x.SchemaName}\".\"{x.TableName}\"",
        RenameTable x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        AddColumn x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ADD COLUMN {BuildColumnDef(x.Column)}""",
        DropColumn x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.TableName}\" DROP COLUMN \"{x.ColumnName}\"",
        RenameColumn x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.TableName}\" RENAME COLUMN \"{x.OldName}\" TO \"{x.NewName}\"",
        AlterColumnType x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ALTER COLUMN "{x.ColumnName}" TYPE {ToPostgresType(x.NewType)}""",
        AlterColumnNullability { NewNullable: false } x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ALTER COLUMN "{x.ColumnName}" SET NOT NULL""",
        AlterColumnNullability x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ALTER COLUMN "{x.ColumnName}" DROP NOT NULL""",
        AlterIdentitySequence x => BuildAlterIdentitySequence(x),
        SetColumnDefault { NewDefault: null } x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ALTER COLUMN "{x.ColumnName}" DROP DEFAULT""",
        SetColumnDefault x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ALTER COLUMN "{x.ColumnName}" SET DEFAULT {x.NewDefault}""",
        AddPrimaryKey x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ADD CONSTRAINT "{x.PrimaryKey.Name}" PRIMARY KEY ({ColList(x.PrimaryKey.ColumnNames)})""",
        DropPrimaryKey x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.TableName}\" DROP CONSTRAINT \"{x.PrimaryKeyName}\"",
        AddForeignKey x => BuildAddForeignKey(x),
        DropForeignKey x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.TableName}\" DROP CONSTRAINT \"{x.ForeignKeyName}\"",
        AddUniqueConstraint x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ADD CONSTRAINT "{x.UniqueConstraint.Name}" UNIQUE ({ColList(x.UniqueConstraint.ColumnNames)})""",
        DropUniqueConstraint x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.TableName}\" DROP CONSTRAINT \"{x.ConstraintName}\"",
        AddCheckConstraint x => $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ADD CONSTRAINT "{x.CheckConstraint.Name}" CHECK ({x.CheckConstraint.Expression})""",
        DropCheckConstraint x => $"ALTER TABLE \"{x.SchemaName}\".\"{x.TableName}\" DROP CONSTRAINT \"{x.ConstraintName}\"",
        CreateIndex x => BuildCreateIndex(x),
        DropIndex x => $"DROP INDEX \"{x.SchemaName}\".\"{x.IndexName}\"",
        // A view Add and a body Modify both arrive as CreateView; CREATE OR REPLACE serves both. An incompatible
        // output-column change (rename/drop/retype/reorder) is rejected loudly by Postgres rather than silently
        // dropping dependents — see CLAUDE.md / the core view-body decision.
        CreateView x => $"""CREATE OR REPLACE VIEW "{x.SchemaName}"."{x.View.Name}" AS {x.View.Body}""",
        DropView x => $"DROP VIEW \"{x.SchemaName}\".\"{x.ViewName}\"",
        RenameView x => $"ALTER VIEW \"{x.SchemaName}\".\"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        SetViewComment x => x.NewComment is null
            ? $"""COMMENT ON VIEW "{x.SchemaName}"."{x.ViewName}" IS NULL"""
            : $"""COMMENT ON VIEW "{x.SchemaName}"."{x.ViewName}" IS $comment${x.NewComment}$comment$""",
        CreateEnum x => BuildCreateEnum(x),
        DropEnum x => $"DROP TYPE \"{x.SchemaName}\".\"{x.EnumName}\"",
        RenameEnum x => $"ALTER TYPE \"{x.SchemaName}\".\"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        SetEnumComment x => x.NewComment is null
            ? $"""COMMENT ON TYPE "{x.SchemaName}"."{x.EnumName}" IS NULL"""
            : $"""COMMENT ON TYPE "{x.SchemaName}"."{x.EnumName}" IS $comment${x.NewComment}$comment$""",
        CreateSequence x => BuildCreateSequence(x),
        DropSequence x => $"DROP SEQUENCE \"{x.SchemaName}\".\"{x.SequenceName}\"",
        RenameSequence x => $"ALTER SEQUENCE \"{x.SchemaName}\".\"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        AlterSequence x => BuildAlterSequence(x),
        SetSequenceComment x => x.NewComment is null
            ? $"""COMMENT ON SEQUENCE "{x.SchemaName}"."{x.SequenceName}" IS NULL"""
            : $"""COMMENT ON SEQUENCE "{x.SchemaName}"."{x.SequenceName}" IS $comment${x.NewComment}$comment$""",
        // A routine Add and a definition-only Modify both arrive as Create; CREATE OR REPLACE serves both. The model
        // has no overloading (one routine per name), so drops, renames and comments omit the signature — Postgres
        // resolves the bare name, and rejects it loudly if an out-of-model overload makes it ambiguous.
        CreateFunction x => $"""CREATE OR REPLACE FUNCTION "{x.SchemaName}"."{x.Function.Name}"({x.Function.Arguments}) {x.Function.Definition}""",
        DropFunction x => $"DROP FUNCTION \"{x.SchemaName}\".\"{x.FunctionName}\"",
        RenameFunction x => $"ALTER FUNCTION \"{x.SchemaName}\".\"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        SetFunctionComment x => x.NewComment is null
            ? $"""COMMENT ON FUNCTION "{x.SchemaName}"."{x.FunctionName}" IS NULL"""
            : $"""COMMENT ON FUNCTION "{x.SchemaName}"."{x.FunctionName}" IS $comment${x.NewComment}$comment$""",
        CreateProcedure x => $"""CREATE OR REPLACE PROCEDURE "{x.SchemaName}"."{x.Procedure.Name}"({x.Procedure.Arguments}) {x.Procedure.Definition}""",
        DropProcedure x => $"DROP PROCEDURE \"{x.SchemaName}\".\"{x.ProcedureName}\"",
        RenameProcedure x => $"ALTER PROCEDURE \"{x.SchemaName}\".\"{x.OldName}\" RENAME TO \"{x.NewName}\"",
        SetProcedureComment x => x.NewComment is null
            ? $"""COMMENT ON PROCEDURE "{x.SchemaName}"."{x.ProcedureName}" IS NULL"""
            : $"""COMMENT ON PROCEDURE "{x.SchemaName}"."{x.ProcedureName}" IS $comment${x.NewComment}$comment$""",
        SetSchemaComment x => x.NewComment is null
            ? $"""COMMENT ON SCHEMA "{x.SchemaName}" IS NULL"""
            : $"""COMMENT ON SCHEMA "{x.SchemaName}" IS $comment${x.NewComment}$comment$""",
        SetTableComment x => x.NewComment is null
            ? $"""COMMENT ON TABLE "{x.SchemaName}"."{x.TableName}" IS NULL"""
            : $"""COMMENT ON TABLE "{x.SchemaName}"."{x.TableName}" IS $comment${x.NewComment}$comment$""",
        SetColumnComment x => x.NewComment is null
            ? $"""COMMENT ON COLUMN "{x.SchemaName}"."{x.TableName}"."{x.ColumnName}" IS NULL"""
            : $"""COMMENT ON COLUMN "{x.SchemaName}"."{x.TableName}"."{x.ColumnName}" IS $comment${x.NewComment}$comment$""",
        SetIndexComment x => x.NewComment is null
            ? $"""COMMENT ON INDEX "{x.SchemaName}"."{x.IndexName}" IS NULL"""
            : $"""COMMENT ON INDEX "{x.SchemaName}"."{x.IndexName}" IS $comment${x.NewComment}$comment$""",
        SetConstraintComment x => x.NewComment is null
            ? $"""COMMENT ON CONSTRAINT "{x.ConstraintName}" ON "{x.SchemaName}"."{x.TableName}" IS NULL"""
            : $"""COMMENT ON CONSTRAINT "{x.ConstraintName}" ON "{x.SchemaName}"."{x.TableName}" IS $comment${x.NewComment}$comment$""",
        GrantSchemaUsage x => $"""GRANT USAGE ON SCHEMA "{x.SchemaName}" TO {x.Role}""",
        RevokeSchemaUsage x => $"""REVOKE USAGE ON SCHEMA "{x.SchemaName}" FROM {x.Role}""",
        GrantTablePrivileges x => $"""GRANT {PrivilegeList(x.Privileges)} ON TABLE "{x.SchemaName}"."{x.TableName}" TO {x.Role}""",
        RevokeTablePrivileges x => $"""REVOKE ALL PRIVILEGES ON TABLE "{x.SchemaName}"."{x.TableName}" FROM {x.Role}""",
        _ => throw new ArgumentOutOfRangeException(nameof(action), $"Unhandled action type: {action.GetType().Name}")
    };

    private static string BuildCreateTable(CreateTable x)
    {
        var parts = x.Table.Columns.Select(BuildColumnDef).ToList();

        // Only the primary key is created inline; unique/check constraints, foreign keys and indexes arrive as
        // separate ALTER TABLE actions from the linearizer (see DefaultPlanLinearizer.EmitTable).
        if (x.Table.PrimaryKey is { } pk)
        {
            parts.Add($"""CONSTRAINT "{pk.Name}" PRIMARY KEY ({ColList(pk.ColumnNames)})""");
        }

        return $"""
            CREATE TABLE "{x.SchemaName}"."{x.Table.Name}" (
                {string.Join(",\n    ", parts)}
            )
            """;
    }

    private static string BuildAddForeignKey(AddForeignKey x)
    {
        var fk = x.ForeignKey;
        var onDelete = ToReferentialAction(fk.OnDelete);
        var onUpdate = ToReferentialAction(fk.OnUpdate);
        return $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ADD CONSTRAINT "{fk.Name}" FOREIGN KEY ({ColList(fk.ColumnNames)}) REFERENCES "{fk.ReferencedSchema}"."{fk.ReferencedTable}" ({ColList(fk.ReferencedColumnNames)}) ON DELETE {onDelete} ON UPDATE {onUpdate}""";
    }

    private static string BuildCreateIndex(CreateIndex x)
    {
        var sql = $"""CREATE {(x.Index.IsUnique ? "UNIQUE " : "")}INDEX "{x.Index.Name}" ON "{x.SchemaName}"."{x.TableName}" ({ColList(x.Index.ColumnNames)})""";
        return x.Index.Predicate is { } pred ? $"{sql} WHERE {pred}" : sql;
    }

    private static string BuildColumnDef(Column col)
    {
        var type = ToPostgresType(col.Type);
        var nullable = col.IsNullable ? "" : " NOT NULL";
        var identity = col.IsIdentity ? BuildIdentityClause(col.IdentityOptions) : "";
        var def = col is { DefaultExpression: { } d, IsIdentity: false } ? $" DEFAULT {d}" : "";
        return $"\"{col.Name}\" {type}{nullable}{identity}{def}";
    }

    private static string BuildIdentityClause(IdentityOptions? options)
    {
        if (options is null)
        {
            return " GENERATED ALWAYS AS IDENTITY";
        }

        var parts = new List<string>();
        if (options.MinValue.HasValue)
        {
            parts.Add($"MINVALUE {options.MinValue}");
        }

        if (options.StartWith.HasValue)
        {
            parts.Add($"START WITH {options.StartWith}");
        }

        if (options.IncrementBy.HasValue)
        {
            parts.Add($"INCREMENT BY {options.IncrementBy}");
        }

        return parts.Count > 0
            ? $" GENERATED ALWAYS AS IDENTITY ({string.Join(" ", parts)})"
            : " GENERATED ALWAYS AS IDENTITY";
    }

    private static string BuildAlterIdentitySequence(AlterIdentitySequence x)
    {
        var opts = x.NewOptions;
        var parts = new List<string>();
        if (opts?.MinValue.HasValue == true)
        {
            parts.Add($"SET MINVALUE {opts.MinValue}");
        }

        if (opts?.StartWith.HasValue == true)
        {
            parts.Add($"SET START {opts.StartWith}");
        }

        if (opts?.IncrementBy.HasValue == true)
        {
            parts.Add($"SET INCREMENT BY {opts.IncrementBy}");
        }

        parts.Add("RESTART");
        return $"""ALTER TABLE "{x.SchemaName}"."{x.TableName}" ALTER COLUMN "{x.ColumnName}" {string.Join(" ", parts)}""";
    }

    private static string BuildCreateEnum(CreateEnum x)
    {
        var values = string.Join(", ", x.Enum.Values.Select(v => $"'{EscapeLiteral(v)}'"));
        return $"""CREATE TYPE "{x.SchemaName}"."{x.Enum.Name}" AS ENUM ({values})""";
    }

    private static string BuildAddEnumValue(AddEnumValue x)
    {
        var sql = $"""ALTER TYPE "{x.SchemaName}"."{x.EnumName}" ADD VALUE '{EscapeLiteral(x.Value)}'""";
        return x switch
        {
            { Before: { } before } => $"{sql} BEFORE '{EscapeLiteral(before)}'",
            { After: { } after } => $"{sql} AFTER '{EscapeLiteral(after)}'",
            _ => sql,
        };
    }

    private static string BuildCreateSequence(CreateSequence x)
    {
        var o = x.Sequence.Options;
        var parts = new List<string>();
        if (o.DataType is { } type)
        {
            parts.Add($"AS {ToPostgresType(type)}");
        }

        if (o.IncrementBy is { } increment)
        {
            parts.Add($"INCREMENT BY {increment}");
        }

        if (o.MinValue is { } min)
        {
            parts.Add($"MINVALUE {min}");
        }

        if (o.MaxValue is { } max)
        {
            parts.Add($"MAXVALUE {max}");
        }

        if (o.StartWith is { } start)
        {
            parts.Add($"START WITH {start}");
        }

        if (o.Cache is { } cache)
        {
            parts.Add($"CACHE {cache}");
        }

        if (o.Cycle)
        {
            parts.Add("CYCLE");
        }

        var clause = parts.Count > 0 ? $" {string.Join(" ", parts)}" : string.Empty;
        return $"""CREATE SEQUENCE "{x.SchemaName}"."{x.Sequence.Name}"{clause}""";
    }

    // One clause per option that differs; a value going back to null resets to the engine default explicitly
    // (AS bigint, INCREMENT BY 1, NO MINVALUE, NO MAXVALUE, CACHE 1, NO CYCLE), so apply → introspect normalizes
    // back to null and shows no residual drift.
    private static string BuildAlterSequence(AlterSequence x)
    {
        var (old, @new) = (x.OldOptions, x.NewOptions);
        var parts = new List<string>();
        if (old.DataType != @new.DataType)
        {
            parts.Add($"AS {ToPostgresType(@new.DataType ?? SqlType.BigInt)}");
        }

        if (old.IncrementBy != @new.IncrementBy)
        {
            parts.Add($"INCREMENT BY {@new.IncrementBy ?? 1}");
        }

        if (old.MinValue != @new.MinValue)
        {
            parts.Add(@new.MinValue is { } min ? $"MINVALUE {min}" : "NO MINVALUE");
        }

        if (old.MaxValue != @new.MaxValue)
        {
            parts.Add(@new.MaxValue is { } max ? $"MAXVALUE {max}" : "NO MAXVALUE");
        }

        if (old.StartWith != @new.StartWith)
        {
            // There is no NO START form; a reset emits the default a fresh sequence with the new options would
            // have (effective minvalue ascending / maxvalue descending), so the next introspection reads null.
            parts.Add($"START WITH {@new.StartWith ?? DefaultStart(@new)}");
        }

        if (old.Cache != @new.Cache)
        {
            parts.Add($"CACHE {@new.Cache ?? 1}");
        }

        if (old.Cycle != @new.Cycle)
        {
            parts.Add(@new.Cycle ? "CYCLE" : "NO CYCLE");
        }

        return $"""ALTER SEQUENCE "{x.SchemaName}"."{x.SequenceName}" {string.Join(" ", parts)}""";
    }

    // A signature change cannot replace in place — CREATE OR REPLACE under a different argument list would create a
    // separate overload rather than replacing the routine. The drop also discards the catalog comment, so it is
    // re-issued from the desired model when one is set. The statements stay separate; the executor runs them inside
    // the same migration transaction.
    private static IEnumerable<SqlStatement> BuildRecreateRoutine(string kind, string schemaName, string name, string arguments, string definition, string? comment)
    {
        yield return new SqlStatement($"DROP {kind} \"{schemaName}\".\"{name}\"");
        yield return new SqlStatement($"""CREATE {kind} "{schemaName}"."{name}"({arguments}) {definition}""");
        if (comment is not null)
        {
            yield return new SqlStatement($"""COMMENT ON {kind} "{schemaName}"."{name}" IS $comment${comment}$comment$""");
        }
    }

    private static long DefaultStart(SequenceOptions options) =>
        (options.IncrementBy ?? 1) > 0 ? options.MinValue ?? 1 : options.MaxValue ?? -1;

    private static string EscapeLiteral(string value) => value.Replace("'", "''");

    private static string ColList(IReadOnlyList<string> cols) =>
        string.Join(", ", cols.Select(c => $"\"{c}\""));

    private static string PrivilegeList(TablePrivilege privileges)
    {
        var parts = new List<string>();
        if (privileges.HasFlag(TablePrivilege.Select))
        {
            parts.Add("SELECT");
        }

        if (privileges.HasFlag(TablePrivilege.Insert))
        {
            parts.Add("INSERT");
        }

        if (privileges.HasFlag(TablePrivilege.Update))
        {
            parts.Add("UPDATE");
        }

        if (privileges.HasFlag(TablePrivilege.Delete))
        {
            parts.Add("DELETE");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "ALL PRIVILEGES";
    }

    // ── Type mapping ──────────────────────────────────────────────────────────

    private static string ToPostgresType(SqlType type) => type.Name switch
    {
        "boolean" => "boolean",
        "tinyint" => "smallint",
        "smallint" => "smallint",
        "int" => "integer",
        "bigint" => "bigint",
        "float" => "real",
        "double" => "double precision",
        "decimal" => $"numeric({type.Precision}, {type.Scale})",
        "char" or "nchar" => $"character({type.Length})",
        "varchar" => type.Length is { } vn ? $"character varying({vn})" : "character varying",
        "nvarchar" => type.Length is { } nvn ? $"character varying({nvn})" : "character varying",
        "text" => "text",
        "date" => "date",
        "time" => "time",
        "datetime" => "timestamp",
        "datetimeoffset" => "timestamptz",
        "guid" => "uuid",
        "binary" => "bytea",
        "varbinary" => "bytea",
        // Any other name is a database-specific or user-defined type (e.g. citext, jsonb);
        // emit it verbatim.
        _ => type.Name,
    };

    private static string ToReferentialAction(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        _ => "NO ACTION",
    };
}

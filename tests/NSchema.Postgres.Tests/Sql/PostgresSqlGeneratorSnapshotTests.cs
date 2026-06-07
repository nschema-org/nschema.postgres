using NSchema.Plan.Model;
using NSchema.Postgres.Sql;
using NSchema.Schema.Model;
using NSchema.Scripts.Model;
using NSchema.Sql;

namespace NSchema.Postgres.Tests.Sql;

/// <summary>
/// Snapshot tests for <see cref="PostgresSqlGenerator"/>. Unlike <see cref="PostgresSqlGeneratorTests"/>
/// (which executes generated DDL against a real database via Testcontainers), these tests assert on the
/// exact SQL text the generator emits — no Docker required. Snapshots live alongside this file as
/// <c>*.verified.txt</c>; review and commit them when the generated SQL intentionally changes.
/// </summary>
public sealed class PostgresSqlGeneratorSnapshotTests
{
    private static readonly ISqlGenerator Generator = new PostgresSqlGenerator();

    private static Task VerifyPlan(params MigrationAction[] actions) =>
        Verify(Generator.Generate(new MigrationPlan(actions)));

    // ── Schema operations ─────────────────────────────────────────────────────

    [Fact]
    public Task SchemaOperations() => VerifyPlan(
        new CreateSchema("sales"),
        new RenameSchema("sales", "commerce"),
        new DropSchema("commerce"));

    // ── Table operations ──────────────────────────────────────────────────────

    [Fact]
    public Task CreateTable_WithColumnsAndPrimaryKey() => VerifyPlan(
        new CreateTable("public", Table.Create("users",
            primaryKey: new PrimaryKey("pk_users", ["id"]),
            columns:
            [
                Column.Create("id", SqlType.BigInt, isNullable: false, isIdentity: true),
                Column.Create("email", SqlType.VarChar(255), isNullable: false),
                Column.Create("created_at", SqlType.DateTimeOffset, isNullable: false, defaultExpression: "now()"),
                Column.Create("notes", SqlType.Text),
            ])));

    [Fact]
    public Task CreateTable_WithIdentityOptions() => VerifyPlan(
        new CreateTable("public", Table.Create("counters",
            columns:
            [
                Column.Create("id", SqlType.BigInt, isNullable: false, isIdentity: true,
                    identityOptions: new IdentityOptions(StartWith: 1000, MinValue: 1000, IncrementBy: 5)),
            ])));

    [Fact]
    public Task TableLifecycle() => VerifyPlan(
        new RenameTable("public", "old_users", "users"),
        new DropTable("public", "legacy"));

    // ── Column operations ─────────────────────────────────────────────────────

    [Fact]
    public Task ColumnOperations() => VerifyPlan(
        new AddColumn("public", "users", Column.Create("age", SqlType.Int)),
        new RenameColumn("public", "users", "age", "years"),
        new AlterColumnType("public", "users", "years", SqlType.Int, SqlType.BigInt),
        new AlterColumnNullability("public", "users", "years", OldNullable: true, NewNullable: false),
        new AlterColumnNullability("public", "users", "notes", OldNullable: false, NewNullable: true),
        new SetColumnDefault("public", "users", "years", null, "0"),
        new SetColumnDefault("public", "users", "years", "0", null),
        new DropColumn("public", "users", Column.Create("years", SqlType.BigInt)));

    [Fact]
    public Task AlterIdentitySequence() => VerifyPlan(
        new AlterIdentitySequence("public", "users", "id",
            OldOptions: new IdentityOptions(StartWith: 1, MinValue: 1, IncrementBy: 1),
            NewOptions: new IdentityOptions(StartWith: 500, MinValue: 100, IncrementBy: 2)));

    // ── Keys, indexes and constraints ───────────────────────────────────────────

    [Fact]
    public Task PrimaryKeyOperations() => VerifyPlan(
        new AddPrimaryKey("public", "users", new PrimaryKey("pk_users", ["id", "tenant_id"])),
        new DropPrimaryKey("public", "users", "pk_users"));

    [Fact]
    public Task ForeignKeyOperations() => VerifyPlan(
        new AddForeignKey("public", "orders", ForeignKey.Create(
            "fk_orders_user", ["user_id"], "public", "users", ["id"],
            onDelete: ReferentialAction.Cascade, onUpdate: ReferentialAction.SetNull)),
        new DropForeignKey("public", "orders", "fk_orders_user"));

    [Fact]
    public Task IndexOperations() => VerifyPlan(
        new CreateIndex("public", "users", TableIndex.Create("idx_users_email", ["email"], isUnique: true)),
        new CreateIndex("public", "users", TableIndex.Create("idx_users_active", ["created_at"], predicate: "notes IS NOT NULL")),
        new DropIndex("public", "users", "idx_users_email"));

    // ── Comments ────────────────────────────────────────────────────────────────

    [Fact]
    public Task CommentOperations() => VerifyPlan(
        new SetSchemaComment("public", null, "Application schema"),
        new SetTableComment("public", "users", null, "Registered users"),
        new SetColumnComment("public", "users", "email", null, "Unique login address"),
        new SetIndexComment("public", "users", "idx_users_email", null, "Lookup by email"),
        new SetTableComment("public", "users", "Registered users", null));

    // ── Grants ────────────────────────────────────────────────────────────────

    [Fact]
    public Task GrantOperations() => VerifyPlan(
        new GrantSchemaUsage("public", "app_role"),
        new GrantTablePrivileges("public", "users", "app_role", TablePrivilege.Select | TablePrivilege.Insert),
        new GrantTablePrivileges("public", "users", "readonly", TablePrivilege.Select),
        new RevokeTablePrivileges("public", "users", "app_role", TablePrivilege.All),
        new RevokeSchemaUsage("public", "app_role"));

    // ── Scripts ─────────────────────────────────────────────────────────────────

    [Fact]
    public Task RunScript_PreservesSqlAndTransactionFlag() => VerifyPlan(
        new RunScript(new Script("seed", "INSERT INTO users (email) VALUES ('a@b.com')", ScriptType.PreDeployment)),
        new RunScript(new Script("reindex", "REINDEX TABLE users", ScriptType.PostDeployment)
        {
            RunOutsideTransaction = true,
        }));

    // ── Type mapping ────────────────────────────────────────────────────────────

    [Fact]
    public Task TypeMapping_CoversAllSqlTypes() => VerifyPlan(
        Alter(SqlType.Boolean),
        Alter(SqlType.TinyInt),
        Alter(SqlType.SmallInt),
        Alter(SqlType.Int),
        Alter(SqlType.BigInt),
        Alter(SqlType.Float),
        Alter(SqlType.Double),
        Alter(SqlType.Decimal(18, 4)),
        Alter(SqlType.Char(10)),
        Alter(SqlType.NChar(10)),
        Alter(SqlType.VarChar(null)),
        Alter(SqlType.VarChar(100)),
        Alter(SqlType.NVarChar(null)),
        Alter(SqlType.NVarChar(100)),
        Alter(SqlType.Text),
        Alter(SqlType.Date),
        Alter(SqlType.Time),
        Alter(SqlType.DateTime),
        Alter(SqlType.DateTimeOffset),
        Alter(SqlType.Guid),
        Alter(SqlType.Binary(16)),
        Alter(SqlType.VarBinary(null)),
        Alter(SqlType.Custom("citext")));

    private static AlterColumnType Alter(SqlType type) =>
        new("public", "t", "c", SqlType.Int, type);
}

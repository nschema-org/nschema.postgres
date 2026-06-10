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
        Verify(Generator.Generate(new MigrationPlan(actions, [], [])));

    // ── Schema operations ─────────────────────────────────────────────────────

    [Fact]
    public Task SchemaOperations() => VerifyPlan(
        new CreateSchema("sales"),
        new RenameSchema("sales", "commerce"),
        new DropSchema("commerce"));

    // ── Table operations ──────────────────────────────────────────────────────

    [Fact]
    public Task CreateTable_WithColumnsAndPrimaryKey() => VerifyPlan(
        new CreateTable("public", new Table("users",
            PrimaryKey: new PrimaryKey("pk_users", ["id"]),
            Columns:
            [
                new Column("id", SqlType.BigInt, IsNullable: false, IsIdentity: true),
                new Column("email", SqlType.VarChar(255), IsNullable: false),
                new Column("created_at", SqlType.DateTimeOffset, IsNullable: false, DefaultExpression: "now()"),
                new Column("notes", SqlType.Text),
            ])));

    [Fact]
    public Task CreateTable_WithIdentityOptions() => VerifyPlan(
        new CreateTable("public", new Table("counters",
            Columns:
            [
                new Column("id", SqlType.BigInt, IsNullable: false, IsIdentity: true,
                    IdentityOptions: new IdentityOptions(StartWith: 1000, MinValue: 1000, IncrementBy: 5)),
            ])));

    [Fact]
    public Task TableLifecycle() => VerifyPlan(
        new RenameTable("public", "old_users", "users"),
        new DropTable("public", "legacy"));

    // ── Column operations ─────────────────────────────────────────────────────

    [Fact]
    public Task ColumnOperations() => VerifyPlan(
        new AddColumn("public", "users", new Column("age", SqlType.Int)),
        new RenameColumn("public", "users", "age", "years"),
        new AlterColumnType("public", "users", "years", SqlType.Int, SqlType.BigInt),
        new AlterColumnNullability("public", "users", "years", OldNullable: true, NewNullable: false),
        new AlterColumnNullability("public", "users", "notes", OldNullable: false, NewNullable: true),
        new SetColumnDefault("public", "users", "years", null, "0"),
        new SetColumnDefault("public", "users", "years", "0", null),
        new DropColumn("public", "users", new Column("years", SqlType.BigInt)));

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
        new AddForeignKey("public", "orders", new ForeignKey(
            "fk_orders_user", ["user_id"], "public", "users", ["id"],
            OnDelete: ReferentialAction.Cascade, OnUpdate: ReferentialAction.SetNull)),
        new DropForeignKey("public", "orders", "fk_orders_user"));

    [Fact]
    public Task IndexOperations() => VerifyPlan(
        new CreateIndex("public", "users", new TableIndex("idx_users_email", ["email"], IsUnique: true)),
        new CreateIndex("public", "users", new TableIndex("idx_users_active", ["created_at"], Predicate: "notes IS NOT NULL")),
        new DropIndex("public", "users", "idx_users_email"));

    // ── Views ─────────────────────────────────────────────────────────────────

    [Fact]
    public Task ViewOperations() => VerifyPlan(
        new CreateView("public", new View("active_users", "SELECT id, email FROM public.users WHERE active")),
        new RenameView("public", "legacy_active", "active_users"),
        new SetViewComment("public", "active_users", null, "Active users only"),
        new SetViewComment("public", "active_users", "Active users only", null),
        new DropView("public", "active_users"));

    // ── Enums ──────────────────────────────────────────────────────────────────

    [Fact]
    public Task EnumOperations() => VerifyPlan(
        new CreateEnum("public", new EnumType("order_status", ["pending", "shipped", "won't_ship"])),
        new RenameEnum("public", "order_state", "order_status"),
        new AddEnumValue("public", "order_status", "delivered"),
        new AddEnumValue("public", "order_status", "draft", Before: "pending"),
        new AddEnumValue("public", "order_status", "in_transit", After: "shipped"),
        new SetEnumComment("public", "order_status", null, "Order lifecycle"),
        new SetEnumComment("public", "order_status", "Order lifecycle", null),
        new DropEnum("public", "order_status"));

    // ── Sequences ──────────────────────────────────────────────────────────────

    [Fact]
    public Task SequenceOperations() => VerifyPlan(
        new CreateSequence("public", new Sequence("order_id")),
        new CreateSequence("public", new Sequence("invoice_id", new SequenceOptions(
            SqlType.SmallInt, StartWith: 100, IncrementBy: 5, MinValue: 10, MaxValue: 30000, Cache: 20, Cycle: true))),
        new RenameSequence("public", "bill_id", "invoice_id"),
        // A mixed delta: one option changes value, every other resets to its engine default explicitly.
        new AlterSequence("public", "invoice_id",
            OldOptions: new SequenceOptions(SqlType.SmallInt, StartWith: 100, IncrementBy: 5, MinValue: 10, MaxValue: 30000, Cache: 20, Cycle: true),
            NewOptions: new SequenceOptions(IncrementBy: 50)),
        new SetSequenceComment("public", "invoice_id", null, "Invoice numbers"),
        new SetSequenceComment("public", "invoice_id", "Invoice numbers", null),
        new DropSequence("public", "invoice_id"));

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

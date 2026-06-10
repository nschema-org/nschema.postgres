using Npgsql;
using NSchema.Plan.Model;
using NSchema.Postgres.Sql;
using NSchema.Postgres.Tests.Fixtures;
using NSchema.Schema.Model;
using NSchema.Scripts.Model;
using NSchema.Sql;
using NSchema.Sql.Model;

namespace NSchema.Postgres.Tests.Sql;

[Collection("postgres")]
public sealed class PostgresSqlGeneratorTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private readonly NpgsqlDataSource _dataSource = fixture.DataSource;
    private readonly string _schema = $"test_{Guid.NewGuid():N}";
    private NpgsqlConnection _conn = null!;
    private PostgresSqlGenerator _generator = null!;
    private StatementRunner _executor = null!;

    public async ValueTask InitializeAsync()
    {
        _conn = await _dataSource.OpenConnectionAsync();
        _generator = new PostgresSqlGenerator();
        _executor = new StatementRunner(_dataSource);
        await Exec($"""CREATE SCHEMA "{_schema}" """);
    }

    public async ValueTask DisposeAsync()
    {
        await Exec($"""DROP SCHEMA IF EXISTS "{_schema}" CASCADE""");
        await _conn.DisposeAsync();
    }

    // ── Schema operations ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSchema_CreatesSchemaInDatabase()
    {
        // Arrange
        var name = $"test_{Guid.NewGuid():N}";

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateSchema(name)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.schemata WHERE schema_name = '{name}'");
        exists.ShouldBeTrue();

        await Exec($"""DROP SCHEMA "{name}" CASCADE""");
    }

    [Fact]
    public async Task DropSchema_RemovesSchemaFromDatabase()
    {
        // Arrange
        var name = $"test_{Guid.NewGuid():N}";
        await Exec($"""CREATE SCHEMA "{name}" """);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropSchema(name)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.schemata WHERE schema_name = '{name}'");
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task RenameSchema_RenamesSchemaInDatabase()
    {
        // Arrange
        var oldName = $"test_{Guid.NewGuid():N}";
        var newName = $"test_{Guid.NewGuid():N}";
        await Exec($"""CREATE SCHEMA "{oldName}" """);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new RenameSchema(oldName, newName)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var oldExists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.schemata WHERE schema_name = '{oldName}'");
        var newExists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.schemata WHERE schema_name = '{newName}'");
        oldExists.ShouldBeFalse();
        newExists.ShouldBeTrue();

        await Exec($"""DROP SCHEMA "{newName}" CASCADE""");
    }

    // ── Table operations ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTable_CreatesTableInDatabase()
    {
        // Arrange
        var table = new Table("users",
            Columns: [new Column("id", SqlType.BigInt, IsNullable: false)]);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateTable(_schema, table)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.tables WHERE table_schema = '{_schema}' AND table_name = 'users'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateTable_WithPrimaryKey_CreatesPrimaryKeyConstraint()
    {
        // Arrange
        var table = new Table("orders",
            PrimaryKey: new PrimaryKey("pk_orders", ["id"]), Columns: [new Column("id", SqlType.BigInt, IsNullable: false)]);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateTable(_schema, table)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'orders' AND constraint_type = 'PRIMARY KEY' AND constraint_name = 'pk_orders'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DropTable_RemovesTableFromDatabase()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."products" (id integer)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropTable(_schema, "products")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.tables WHERE table_schema = '{_schema}' AND table_name = 'products'");
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task RenameTable_RenamesTableInDatabase()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."old_name" (id integer)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new RenameTable(_schema, "old_name", "new_name")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var oldExists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.tables WHERE table_schema = '{_schema}' AND table_name = 'old_name'");
        var newExists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.tables WHERE table_schema = '{_schema}' AND table_name = 'new_name'");
        oldExists.ShouldBeFalse();
        newExists.ShouldBeTrue();
    }

    // ── Column operations ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddColumn_AddsColumnToTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer)""");
        var column = new Column("name", SqlType.VarChar(100), IsNullable: false);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AddColumn(_schema, "items", column)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'name'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DropColumn_RemovesColumnFromTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, name text)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropColumn(_schema, "items", new Column("name", SqlType.Text))], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'name'");
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task RenameColumn_RenamesColumnInTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, old_col text)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new RenameColumn(_schema, "items", "old_col", "new_col")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var oldExists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'old_col'");
        var newExists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'new_col'");
        oldExists.ShouldBeFalse();
        newExists.ShouldBeTrue();
    }

    [Fact]
    public async Task AlterColumnType_ChangesColumnDataType()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, value integer)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AlterColumnType(_schema, "items", "value", SqlType.Int, SqlType.BigInt)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var dataType = await ScalarString(
            $"SELECT data_type FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'value'");
        dataType.ShouldBe("bigint");
    }

    [Fact]
    public async Task AlterColumnNullability_MakesColumnNotNull()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, name text)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AlterColumnNullability(_schema, "items", "name", OldNullable: true, NewNullable: false)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var isNullable = await ScalarString(
            $"SELECT is_nullable FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'name'");
        isNullable.ShouldBe("NO");
    }

    [Fact]
    public async Task AlterColumnNullability_MakesColumnNullable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, name text NOT NULL)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AlterColumnNullability(_schema, "items", "name", OldNullable: false, NewNullable: true)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var isNullable = await ScalarString(
            $"SELECT is_nullable FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'name'");
        isNullable.ShouldBe("YES");
    }

    [Fact]
    public async Task SetColumnDefault_SetsDefaultExpression()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, quantity integer)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new SetColumnDefault(_schema, "items", "quantity", null, "0")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var hasDefault = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'quantity' AND column_default IS NOT NULL");
        hasDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task SetColumnDefault_DropsDefaultExpression()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, quantity integer DEFAULT 0)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new SetColumnDefault(_schema, "items", "quantity", "0", null)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var hasDefault = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.columns WHERE table_schema = '{_schema}' AND table_name = 'items' AND column_name = 'quantity' AND column_default IS NOT NULL");
        hasDefault.ShouldBeFalse();
    }

    // ── Primary key operations ────────────────────────────────────────────────

    [Fact]
    public async Task AddPrimaryKey_AddsConstraintToTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer NOT NULL)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AddPrimaryKey(_schema, "items", new PrimaryKey("pk_items", ["id"]))], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'items' AND constraint_type = 'PRIMARY KEY' AND constraint_name = 'pk_items'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DropPrimaryKey_RemovesConstraintFromTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer NOT NULL, CONSTRAINT pk_items PRIMARY KEY (id))""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropPrimaryKey(_schema, "items", "pk_items")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'items' AND constraint_type = 'PRIMARY KEY'");
        exists.ShouldBeFalse();
    }

    // ── Foreign key operations ────────────────────────────────────────────────

    [Fact]
    public async Task AddForeignKey_AddsReferentialConstraint()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."parents" (id integer NOT NULL, CONSTRAINT pk_parents PRIMARY KEY (id))""");
        await Exec($"""CREATE TABLE "{_schema}"."children" (id integer NOT NULL, parent_id integer)""");
        var fk = new ForeignKey("fk_children_parent", ["parent_id"], _schema, "parents", ["id"]);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AddForeignKey(_schema, "children", fk)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.referential_constraints WHERE constraint_schema = '{_schema}' AND constraint_name = 'fk_children_parent'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DropForeignKey_RemovesReferentialConstraint()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."parents" (id integer NOT NULL, CONSTRAINT pk_parents PRIMARY KEY (id))""");
        await Exec($"""CREATE TABLE "{_schema}"."children" (id integer, parent_id integer, CONSTRAINT fk_children_parent FOREIGN KEY (parent_id) REFERENCES "{_schema}"."parents" (id))""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropForeignKey(_schema, "children", "fk_children_parent")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.referential_constraints WHERE constraint_schema = '{_schema}' AND constraint_name = 'fk_children_parent'");
        exists.ShouldBeFalse();
    }

    // ── Unique constraint operations ──────────────────────────────────────────

    [Fact]
    public async Task AddUniqueConstraint_AddsConstraintToTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, code text)""");
        var unique = new UniqueConstraint("uq_items_code", ["code"]);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AddUniqueConstraint(_schema, "items", unique)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'items' AND constraint_type = 'UNIQUE' AND constraint_name = 'uq_items_code'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DropUniqueConstraint_RemovesConstraintFromTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, code text, CONSTRAINT uq_items_code UNIQUE (code))""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropUniqueConstraint(_schema, "items", "uq_items_code")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'items' AND constraint_type = 'UNIQUE'");
        exists.ShouldBeFalse();
    }

    // ── Check constraint operations ───────────────────────────────────────────

    [Fact]
    public async Task AddCheckConstraint_AddsConstraintToTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."accounts" (id integer, balance integer)""");
        var check = new CheckConstraint("ck_balance", "balance >= 0");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new AddCheckConstraint(_schema, "accounts", check)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'accounts' AND constraint_type = 'CHECK' AND constraint_name = 'ck_balance'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DropCheckConstraint_RemovesConstraintFromTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."accounts" (id integer, balance integer, CONSTRAINT ck_balance CHECK (balance >= 0))""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropCheckConstraint(_schema, "accounts", "ck_balance")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.table_constraints WHERE table_schema = '{_schema}' AND table_name = 'accounts' AND constraint_name = 'ck_balance'");
        exists.ShouldBeFalse();
    }

    // ── Constraint comments ───────────────────────────────────────────────────

    [Fact]
    public async Task SetConstraintComment_SetsCommentOnConstraint()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, code text, CONSTRAINT uq_items_code UNIQUE (code))""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new SetConstraintComment(_schema, "items", "uq_items_code", null, "one row per code")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var comment = await ScalarString(
            $"SELECT obj_description(oid, 'pg_constraint') FROM pg_constraint WHERE conname = 'uq_items_code' AND connamespace = '{_schema}'::regnamespace");
        comment.ShouldBe("one row per code");
    }

    [Fact]
    public async Task SetConstraintComment_ClearsCommentWhenNull()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, code text, CONSTRAINT uq_items_code UNIQUE (code))""");
        await Exec($"""COMMENT ON CONSTRAINT uq_items_code ON "{_schema}"."items" IS 'old comment'""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new SetConstraintComment(_schema, "items", "uq_items_code", "old comment", null)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var hasComment = await ScalarBool(
            $"SELECT obj_description(oid, 'pg_constraint') IS NOT NULL FROM pg_constraint WHERE conname = 'uq_items_code' AND connamespace = '{_schema}'::regnamespace");
        hasComment.ShouldBeFalse();
    }

    // ── Index operations ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateIndex_CreatesIndexOnTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, name text)""");
        var index = new TableIndex("idx_items_name", ["name"]);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateIndex(_schema, "items", index)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM pg_indexes WHERE schemaname = '{_schema}' AND tablename = 'items' AND indexname = 'idx_items_name'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateIndex_Unique_CreatesUniqueIndexOnTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, code text)""");
        var index = new TableIndex("idx_items_code_unique", ["code"], IsUnique: true);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateIndex(_schema, "items", index)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var isUnique = await ScalarBool(
            $"SELECT ix.indisunique FROM pg_indexes pi JOIN pg_class t ON t.relname = pi.tablename JOIN pg_index ix ON ix.indexrelid = (SELECT oid FROM pg_class WHERE relname = 'idx_items_code_unique') WHERE pi.schemaname = '{_schema}' AND pi.indexname = 'idx_items_code_unique'");
        isUnique.ShouldBeTrue();
    }

    [Fact]
    public async Task DropIndex_RemovesIndexFromTable()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."items" (id integer, name text)""");
        await Exec($"""CREATE INDEX "idx_items_name" ON "{_schema}"."items" (name)""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropIndex(_schema, "items", "idx_items_name")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM pg_indexes WHERE schemaname = '{_schema}' AND indexname = 'idx_items_name'");
        exists.ShouldBeFalse();
    }

    // ── Views ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateView_CreatesViewInDatabase()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."users" (id integer, active boolean)""");
        var view = new View("active_users", $"""SELECT id FROM "{_schema}"."users" WHERE active""");

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateView(_schema, view)], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.views WHERE table_schema = '{_schema}' AND table_name = 'active_users'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateView_OnExistingView_ReplacesDefinition()
    {
        // Arrange — CreateView serves both add and body-modify; the second create must replace, not error.
        await Exec($"""CREATE TABLE "{_schema}"."users" (id integer, email text)""");
        await Exec($"""CREATE VIEW "{_schema}"."u" AS SELECT id FROM "{_schema}"."users" """);
        var replacement = new View("u", $"""SELECT id, email FROM "{_schema}"."users" """);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new CreateView(_schema, replacement)], [], [])), TestContext.Current.CancellationToken);

        // Assert — the definition now includes the email column.
        var def = await ScalarString(
            $"SELECT pg_get_viewdef('\"{_schema}\".\"u\"'::regclass)");
        def.ShouldContain("email");
    }

    [Fact]
    public async Task DropView_RemovesView()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."users" (id integer)""");
        await Exec($"""CREATE VIEW "{_schema}"."u" AS SELECT id FROM "{_schema}"."users" """);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new DropView(_schema, "u")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.views WHERE table_schema = '{_schema}' AND table_name = 'u'");
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task RenameView_RenamesView()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."users" (id integer)""");
        await Exec($"""CREATE VIEW "{_schema}"."old_u" AS SELECT id FROM "{_schema}"."users" """);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new RenameView(_schema, "old_u", "new_u")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.views WHERE table_schema = '{_schema}' AND table_name = 'new_u'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task SetViewComment_SetsComment()
    {
        // Arrange
        await Exec($"""CREATE TABLE "{_schema}"."users" (id integer)""");
        await Exec($"""CREATE VIEW "{_schema}"."u" AS SELECT id FROM "{_schema}"."users" """);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([new SetViewComment(_schema, "u", null, "the view")], [], [])), TestContext.Current.CancellationToken);

        // Assert
        var comment = await ScalarString(
            $"SELECT obj_description('\"{_schema}\".\"u\"'::regclass)");
        comment.ShouldBe("the view");
    }

    // ── Deployment scripts ────────────────────────────────────────────────────

    [Fact]
    public async Task RunScript_PreDeployment_ExecutesSql()
    {
        // Arrange
        var script = new Script("seed", $"""CREATE TABLE "{_schema}"."seeded" (id integer)""", ScriptType.PreDeployment);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([], [script], [])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.tables WHERE table_schema = '{_schema}' AND table_name = 'seeded'");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task RunScript_PostDeployment_ExecutesSql()
    {
        // Arrange
        var script = new Script("seed", $"""CREATE TABLE "{_schema}"."seeded_post" (id integer)""", ScriptType.PostDeployment);

        // Act
        await _executor.Execute(_generator.Generate(new MigrationPlan([], [], [script])), TestContext.Current.CancellationToken);

        // Assert
        var exists = await ScalarBool(
            $"SELECT COUNT(*) > 0 FROM information_schema.tables WHERE table_schema = '{_schema}' AND table_name = 'seeded_post'");
        exists.ShouldBeTrue();
    }

    // Transaction/rollback semantics are the core executor's behaviour (DefaultSqlExecutor, now internal) and are
    // tested in the core, not here — this suite covers the Postgres SQL the generator emits.

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Runs the generated statements directly (the core executor is internal). This is only a vehicle for asserting
    // the generated SQL is valid Postgres; it intentionally does not replicate the executor's transaction handling.
    private sealed class StatementRunner(NpgsqlDataSource dataSource)
    {
        public async Task Execute(SqlPlan plan, CancellationToken cancellationToken = default)
        {
            foreach (var statement in plan.Statements)
            {
                await using var command = dataSource.CreateCommand(statement.Sql);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private async Task Exec(string sql)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> ScalarBool(string sql)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> ScalarString(string sql)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}

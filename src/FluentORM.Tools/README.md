# FluentORM CLI — Migration Tool

A standalone command-line tool for managing database migrations in any project that uses [FluentORM.Migrations](https://github.com/marinusmaurice/FluentORM). Supports SQLite and SQL Server.

---

## Installation

### As a global dotnet tool (recommended)

```bash
dotnet tool install -g FluentORM.Tools
```

After installation the `fluentorm` command is available everywhere in your terminal.

### As a local project tool

```bash
dotnet new tool-manifest   # only once per repo
dotnet tool install FluentORM.Tools
dotnet fluentorm --help
```

### From source (development / pre-release)

```bash
cd path/to/FluentORM
dotnet pack src/FluentORM.Tools
dotnet tool install -g --add-source src/FluentORM.Tools/nupkg FluentORM.Tools
```

### Upgrade / uninstall

```bash
dotnet tool update -g FluentORM.Tools
dotnet tool uninstall -g FluentORM.Tools
```

---

## Quick Start (2 minutes)

**1. Go to your project folder and generate a config file:**

```bash
cd MyApp/
fluentorm init
```

This creates `fluentorm.json`:

```json
{
  "provider": "sqlite",
  "connectionString": "Data Source=myapp.db",
  "assembly": "./bin/Debug/net8.0/MyApp.dll",
  "migrationsNamespace": "MyApp.Migrations"
}
```

Edit the four values to match your project, then every command below picks up the config automatically — no flags needed.

**2. Build your project, then scaffold your first migration:**

```bash
dotnet build
fluentorm migrations scaffold create_initial_schema --output ./Migrations
```

On the very first run, no migration is generated — the tool creates an initial snapshot (`_FluentORM_Snapshot.json`) of your current entities.

**3. Now change an entity class, rebuild, and scaffold again:**

```bash
# (edit an entity, e.g. add a property)
dotnet build
fluentorm migrations scaffold add_my_new_column --output ./Migrations
```

This time the tool diffs the current model against the snapshot and generates a migration file. Review it before applying.

**4. Check what will run:**

```bash
fluentorm migrations preview
```

**5. Apply it:**

```bash
fluentorm migrations apply
```

---

## How It Works

The tool loads your compiled `.dll` at runtime via reflection. It discovers:

- **Migration classes** — any class that inherits `Migration` and has a `[Migration(version, description)]` attribute.
- **Entity classes** — any class with a `[Table("...")]` attribute (used for auto-scaffolding).

**Important:** you must `dotnet build` your project before running the tool. The tool reads the `.dll`, not `.cs` source files.

### The assembly must be built before every command

```
Add/change entity or migration  →  dotnet build  →  fluentorm migrations <command>
```

### How auto-scaffold works (no database required)

`fluentorm migrations scaffold` works entirely from your C# entity classes — no live database connection needed. It uses a **model snapshot** file (`_FluentORM_Snapshot.json`) stored alongside your migration files:

1. **First run** — no snapshot exists yet. The tool creates an initial snapshot from your current entities and tells you what it captured. No migration file is generated on the first run.
2. **Subsequent runs** — the tool loads the snapshot, compares it against your current entity classes, generates a migration for everything that changed, and updates the snapshot.

```
First run   →  creates _FluentORM_Snapshot.json  (no migration yet)
Change entity  →  dotnet build  →  scaffold  →  migration generated  →  snapshot updated
```

The snapshot file should be committed to source control alongside your migration files. It acts as the "last known state" baseline for the next scaffold.

---

## Configuration

### `fluentorm.json`

Place this file in the directory where you run `fluentorm`. Run `fluentorm init` to generate a template.

```json
{
  "provider": "sqlite",
  "connectionString": "Data Source=./data/myapp.db",
  "assembly": "./bin/Release/net8.0/MyApp.dll",
  "migrationsNamespace": "MyApp.Migrations"
}
```

| Field | Values | Description |
|---|---|---|
| `provider` | `sqlite` \| `sqlserver` | Database engine |
| `connectionString` | any ADO.NET connection string | Connection to your database |
| `assembly` | relative or absolute path | Path to your compiled `.dll` |
| `migrationsNamespace` | any C# namespace | Namespace used when generating new migration files |

### Environment variables

Override any config value without editing the file — useful for CI/CD:

| Variable | Overrides |
|---|---|
| `FLUENTORM_PROVIDER` | `provider` |
| `FLUENTORM_CONNECTION` | `connectionString` |
| `FLUENTORM_ASSEMBLY` | `assembly` |

### CLI flags

Flags override both the config file and environment variables:

```bash
fluentorm migrations status \
  --provider sqlserver \
  --connection "Server=.;Database=MyApp;Integrated Security=true;TrustServerCertificate=true;" \
  --assembly ./bin/Release/net8.0/MyApp.dll
```

**Priority (highest wins):** CLI flags → environment variables → `fluentorm.json` → defaults

---

## All Commands

### `fluentorm init`

Creates a `fluentorm.json` template in the current directory.

```bash
fluentorm init
fluentorm init --config ./config/fluentorm.json   # write to a specific path
```

---

### `migrations status`

Shows applied, pending (safe), and pending (destructive) migrations.

```bash
fluentorm migrations status
```

```
  Migration Status
  ──────────────────────────────────────────────────────────────
  Applied                   3
  Pending (safe)            2
  Pending (destructive)     1  ← requires --allow-destructive

  Applied:
    ✓ 20240601001  create_users_table                         2024-06-01 09:12
    ✓ 20240601002  add_email_index                            2024-06-01 09:12
    ✓ 20240601003  create_orders_table                        2024-06-15 14:30

  Pending:
    · 20240701001  add_status_to_orders                       [safe]
    · 20240701002  add_archived_orders_index                  [safe]
    ⚠ 20240702001  drop_legacy_notes                          [DESTRUCTIVE]
          └─ Drops Orders.LegacyNotes — data permanently lost.
```

---

### `migrations apply`

Applies pending migrations in version order. Stops before any destructive migration unless you explicitly allow it.

```bash
# Apply all safe pending migrations
fluentorm migrations apply

# Also apply destructive migrations
fluentorm migrations apply --allow-destructive

# Apply up to and including a specific version
fluentorm migrations apply --to 20240701002
fluentorm migrations apply --to 20240701002 --allow-destructive
```

The command is safe to re-run — it only applies what is pending.

---

### `migrations rollback`

Rolls back migrations by calling their `Down()` method.

```bash
# Roll back the single most-recently applied migration
fluentorm migrations rollback

# Roll back everything newer than version 20240601003
# (20240601003 stays applied; everything after it is undone)
fluentorm migrations rollback --to 20240601003
```

> **Note:** A migration can declare itself irreversible by throwing `IrreversibleMigrationException` in `Down()`. If you try to roll it back, the command fails with a clear error and no changes are made.

---

### `migrations preview`

Prints the SQL that *would* be executed without touching the database. Useful before applying to production.

```bash
fluentorm migrations preview
```

---

### `migrations list`

Lists every migration class found in the assembly with its current status.

```bash
fluentorm migrations list
```

```
  All Migrations  (5 total)
  ──────────────────────────────────────────────────────────────
  Version              Description                              Status       Applied At
  ------------------------------------------------------------------------------------------
  20240601001          create_users_table                       Applied      2024-06-01 09:12:00
  20240601002          add_email_index                          Applied      2024-06-01 09:12:00
  20240701001          add_status_to_orders                     Pending
  20240701002          add_archived_orders_index                Pending
  20240702001          drop_legacy_notes                        DESTRUCTIVE
```

---

### `migrations history`

Shows the applied history recorded in the database's `__FluentMigrations` table.

```bash
fluentorm migrations history
```

---

### `migrations validate`

Detects schema drift — differences between your C# entity definitions and the actual database schema.

```bash
# Check for schema drift only
fluentorm migrations validate

# Also verify no applied migration was modified after being applied
fluentorm migrations validate --check-checksums
```

Sample drift output:

```
FluentORM Schema Drift Detected — 2 issue(s)
════════════════════════════════════════════
[ERROR] User  →  Column 'PhoneNumber' exists in C# mapping but not in the database.
        Suggested fix:
            schema.AddColumn<User>(p => p.PhoneNumber).Nullable();
[WARNING] Order  →  Column 'legacy_ref' exists in database but has no C# property mapping.
```

Exit code 1 if any ERROR-severity drift is found; 0 for warnings only.

---

### `migrations new`

Creates a blank migration file from a template with the correct version number and PascalCase class name.

```bash
# Create in current directory
fluentorm migrations new add_phone_to_users

# Create in a specific directory
fluentorm migrations new add_phone_to_users --output ./src/Migrations

# Override the namespace
fluentorm migrations new add_phone_to_users --output ./src/Migrations --namespace MyApp.Data.Migrations
```

Generated file (`Migration_20240618001_AddPhoneToUsers.cs`):

```csharp
using FluentORM.Core.Attributes;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace MyApp.Migrations;

[Migration(20240618001, "add_phone_to_users")]
public sealed class AddPhoneToUsers : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        // TODO: implement migration
        //
        // Examples:
        //   schema.CreateTable<MyEntity>(t => { ... });
        //   schema.AddColumn<MyEntity>(x => x.NewColumn).NotNull().Default("default");
        //   schema.AddIndex<MyEntity>(x => x.Column);
        //   schema.Sql("UPDATE ...");
    }

    public override void Down(SchemaBuilder schema)
    {
        // TODO: implement rollback
        //
        // If this migration cannot be reversed, throw:
        //   throw new IrreversibleMigrationException("Reason why rollback is not possible.");
    }
}
```

Version numbers are timestamp-based (`yyyyMMddNNN`). If you create multiple migrations on the same day, they get `001`, `002`, `003`, etc. in order.

---

### `migrations scaffold`

Automatically generates a migration file by comparing your current C# entity classes against a stored model snapshot. **No database connection is required.**

```bash
# First run — creates the initial snapshot (no migration generated yet)
fluentorm migrations scaffold initial --output ./Migrations

# Subsequent runs — generates a migration for any entity changes
fluentorm migrations scaffold add_new_entity_columns --output ./Migrations

# Preview the migration that would be generated without writing any files
fluentorm migrations scaffold add_new_entity_columns --output ./Migrations --dry-run

# Override the namespace used in the generated file
fluentorm migrations scaffold add_fields_table --output ./Migrations --namespace MyApp.Data.Migrations
```

#### The snapshot file

On first run, the tool writes `_FluentORM_Snapshot.json` to the output directory. This file records the shape of every entity at the time of last scaffold. **Commit it to source control** — it is the baseline for every future scaffold.

```
./Migrations/
  _FluentORM_Snapshot.json          ← commit this
  Migration_20240601001_Initial.cs  ← commit this
  Migration_20240618001_AddPhone.cs ← commit this
```

#### What changes are detected

| Entity change | Generated code |
|---|---|
| New entity class | `schema.CreateTable<T>(...)` |
| Removed entity class | `schema.DropTable<T>()` ⚠ destructive |
| New property | `schema.AddColumn<T>(x => x.Prop)...` |
| Removed property | `schema.DropColumn<T>(x => x.Prop)` ⚠ destructive |
| Nullability changed | `schema.AlterColumn<T>(x => x.Prop).NotNull()` |
| MaxLength changed | `schema.AlterColumn<T>(x => x.Prop).MaxLength(n)` |
| New `[Index]` attribute | `schema.AddIndex<T>(x => x.Prop)` |
| Removed `[Index]` attribute | `schema.DropIndex<T>("name")` |

Destructive changes are flagged with `[Destructive("...")]` in the generated file and require `--allow-destructive` when applied.

> Always review the generated file before applying. Scaffold output is a starting point, not a final answer.

---

## Writing Migrations

Migrations live in your project. Add a reference to `FluentORM.Migrations`:

```bash
dotnet add package FluentORM.Migrations
```

### Basic structure

```csharp
using FluentORM.Core.Attributes;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace MyApp.Migrations;

[Migration(20240601001, "create_users_table")]
public sealed class CreateUsersTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<User>(t =>
        {
            t.PrimaryKey(x => x.Id).AutoIncrement();
            t.Column(x => x.Email).NotNull().MaxLength(255);
            t.Column(x => x.Name).NotNull().MaxLength(100);
            t.Column(x => x.CreatedAt).NotNull();
            t.UniqueIndex(x => x.Email);
        });
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<User>();
    }
}
```

### Version numbers

Version numbers must be unique across all migrations. Use the pattern `yyyyMMddNNN`:

```
20240601001   first migration on June 1 2024
20240601002   second migration on June 1 2024
20240615001   first migration on June 15 2024
```

The `fluentorm migrations new` command assigns the next available number automatically.

### Schema operations

```csharp
// Tables
schema.CreateTable<T>(builder => { ... });
schema.DropTable<T>();
schema.RenameTable<T>("NewTableName");
schema.TruncateTable<T>();

// Columns
schema.AddColumn<T>(x => x.Column).NotNull().Default(0);
schema.AddColumn<T>(x => x.Column).Nullable().MaxLength(500);
schema.AlterColumn<T>(x => x.Column).NotNull();
schema.DropColumn<T>(x => x.Column);
schema.RenameColumn<T>(old: "OldName", @new: "NewName");

// Indexes
schema.AddIndex<T>(x => x.Column);
schema.AddIndex<T>(x => x.Column).Clustered();
schema.AddUniqueIndex<T>(x => x.Column);
schema.DropIndex<T>("IndexName");

// Foreign keys
schema.AddForeignKey<TChild, TParent>(
    child: x => x.ParentId,
    parent: x => x.Id,
    onDelete: CascadeRule.Restrict);
schema.DropForeignKey<T>("FK_ConstraintName");

// Raw SQL escape hatch
schema.Sql("UPDATE Settings SET Version = 2 WHERE Version = 1");
```

### Destructive migrations

Mark any migration that loses data with `[Destructive]`. The tool will refuse to run it without `--allow-destructive`:

```csharp
[Migration(20240702001, "drop_legacy_notes")]
[Destructive("Drops Orders.LegacyNotes — export this column before applying.")]
public sealed class DropLegacyNotes : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.DropColumn<Order>(x => x.LegacyNotes);
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.AddColumn<Order>(x => x.LegacyNotes).Nullable();
    }
}
```

### Irreversible migrations

If a migration cannot be rolled back, throw in `Down()`:

```csharp
public override void Down(SchemaBuilder schema)
{
    throw new IrreversibleMigrationException(
        "Cannot restore dropped data. Restore from backup instead.");
}
```

---

## Typical Workflows

### Daily development loop

```bash
# 1. Change your entity / add a new one
# 2. Rebuild
dotnet build

# 3a. Auto-generate the migration from your entity changes (no DB needed)
fluentorm migrations scaffold describe_your_change --output ./Migrations

# 3b. Or preview first without writing any files
fluentorm migrations scaffold describe_your_change --output ./Migrations --dry-run

# 3c. Or write a blank template yourself
fluentorm migrations new describe_your_change --output ./Migrations

# 4. Review the generated file, edit as needed, rebuild
dotnet build

# 5. Apply to your local database
fluentorm migrations apply
```

### Deploying to production / staging

```bash
# Always preview first
fluentorm migrations preview \
  --connection "$PROD_CONNECTION_STRING" \
  --assembly ./publish/MyApp.dll

# Apply safe migrations
fluentorm migrations apply \
  --connection "$PROD_CONNECTION_STRING" \
  --assembly ./publish/MyApp.dll

# Destructive migrations require an explicit flag — review before running
fluentorm migrations apply --allow-destructive \
  --connection "$PROD_CONNECTION_STRING" \
  --assembly ./publish/MyApp.dll
```

### CI/CD

```yaml
# GitHub Actions example
- name: Apply migrations
  env:
    FLUENTORM_CONNECTION: ${{ secrets.DB_CONNECTION_STRING }}
    FLUENTORM_PROVIDER: sqlserver
    FLUENTORM_ASSEMBLY: ./publish/MyApp.dll
  run: |
    dotnet tool restore
    fluentorm migrations validate --check-checksums
    fluentorm migrations apply
```

The tool returns non-zero exit codes on failure (see [Exit Codes](#exit-codes)) so CI will fail on errors automatically.

---

## Version Compatibility

The tool ships with a specific version of `FluentORM.Migrations`. Your project should reference the **same version**:

| Tool version | FluentORM.Migrations version |
|---|---|
| 1.0.1 | 1.0.1 |

If there's a mismatch the tool will warn you and migration type discovery may fail. Use `dotnet tool update -g FluentORM.Tools` to align versions.

---

## SQLite Notes

SQLite has limited `ALTER TABLE` support. Some schema operations are rendered as SQL comments (no-ops) with guidance on a manual table rebuild:

| Operation | SQLite behaviour |
|---|---|
| `DropColumn` | No-op comment — SQLite 3.35+ supports it natively, but FluentORM currently does not emit it |
| `DropForeignKey` | No-op comment — requires full table rebuild |
| `AlterColumn` nullability | No-op comment — requires full table rebuild |

These operations work fully on SQL Server.

**Important for rollbacks on SQLite:** if a migration's `Up()` adds a column and the `Down()` drops it, the rollback will record in `__FluentMigrations` that the migration was rolled back — but the column will still be present in the database because `DropColumn` is a no-op. If you then re-apply the migration it will fail with "duplicate column name". To recover, either delete and recreate the SQLite database, or manually drop the column with `sqlite3` / a table rebuild.

---

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | General error (bad args, file not found, unexpected exception) |
| 2 | Destructive migration blocked — re-run with `--allow-destructive` |
| 3 | Migration tampered with — checksum mismatch |
| 4 | Irreversible migration — rollback not possible |
| 5 | Migration order violation — a migration is older than the last applied |
| 6 | Migration execution failed — SQL error during Up/Down |

---

## History Table

The tool creates a `__FluentMigrations` table in your database automatically on first run:

| Column | Type | Description |
|---|---|---|
| `Version` | BIGINT | Migration version number (primary key) |
| `Description` | NVARCHAR(500) | Human-readable description |
| `AppliedAt` | DATETIME2 | When it was applied (UTC) |
| `AppliedBy` | NVARCHAR(200) | Machine name |
| `DurationMs` | INT | How long it took to run |
| `Checksum` | NVARCHAR(64) | SHA-256 hash of the Up() SQL — used to detect tampering |

Do not modify this table manually. Use `fluentorm migrations validate --check-checksums` to verify integrity.

---

## FAQ

**Q: Do I need to drop the tool exe into my project folder?**

No. Install it once globally with `dotnet tool install -g FluentORM.Tools`. Then run `fluentorm` from any project folder. Put a `fluentorm.json` in the project root so it knows where your assembly and database are.

**Q: Can I use it with multiple projects / databases?**

Yes. Each project has its own `fluentorm.json`. Or skip the config file and pass `--connection`, `--provider`, and `--assembly` flags each time.

**Q: What if my project's DLL has dependencies the tool doesn't know about?**

The tool automatically looks for unresolved assemblies in the same directory as your `.dll`. As long as your project is built (i.e., the `bin/` folder is populated), all dependencies will be found.

**Q: Can I use it in a Docker container?**

Yes. Install it as a local tool in your project and restore it during the build:

```dockerfile
RUN dotnet tool restore
RUN dotnet fluentorm migrations apply
```

**Q: What does `scaffold` do that `new` doesn't?**

`new` creates a blank file you fill in yourself. `scaffold` compares your current C# entity classes against a stored model snapshot (`_FluentORM_Snapshot.json`) and writes the migration with the necessary `AddColumn`, `CreateTable`, etc. calls already filled in. No database connection required — it works purely from your compiled assembly. You still review and can edit the file before applying.

**Q: When should I use `scaffold` vs `new`?**

Use `scaffold` for the typical case: you changed an entity class and want the migration auto-generated. Use `new` when writing a migration that can't be derived from entity shape — raw SQL data transformations, renaming columns, custom indexes, seeding data, etc.

**Q: What is `_FluentORM_Snapshot.json` and should I commit it?**

Yes, commit it. It is the "last known model state" used by `scaffold` to detect what changed since the last migration was generated. Without it, scaffold can't produce a diff — it would just re-create the initial snapshot. Keep it alongside your migration files in source control.

**Q: Where should I put my migration files?**

Anywhere in your project — they just need to be compiled into your assembly. A `Migrations/` folder at the project root is the convention.

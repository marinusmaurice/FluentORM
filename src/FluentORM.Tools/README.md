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

**2. Create your first migration:**

```bash
dotnet build
fluentorm migrations new create_users_table --output ./Migrations
```

Edit the generated file, then rebuild:

```bash
dotnet build
```

**3. Check what will run:**

```bash
fluentorm migrations preview
```

**4. Apply it:**

```bash
fluentorm migrations apply
```

---

## How It Works

The tool loads your compiled `.dll` at runtime via reflection. It discovers:

- **Migration classes** — any class that inherits `Migration` and has a `[Migration(version, description)]` attribute.
- **Entity classes** — any class with a `[Table("...")]` attribute (used for schema drift detection and scaffolding).

**Important:** you must `dotnet build` your project before running the tool. The tool reads the `.dll`, not `.cs` source files.

### The assembly must be built before every command

```
Add/change entity or migration  →  dotnet build  →  fluentorm migrations <command>
```

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

Automatically generates a migration file by detecting schema drift between your C# entities and the database. Useful when you've added or changed entity classes and want a migration generated for you.

```bash
# Generate and write to current directory
fluentorm migrations scaffold add_new_entity_columns

# Preview what would be generated without writing a file
fluentorm migrations scaffold add_new_entity_columns --dry-run

# Write to a specific directory
fluentorm migrations scaffold add_new_entity_columns --output ./src/Migrations
```

> Always review the generated file before applying. Scaffold output is a starting point, not a final answer — especially for destructive operations.

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

# 3a. Let the tool generate the migration for you
fluentorm migrations scaffold describe_your_change --output ./Migrations

# 3b. Or write it yourself from a blank template
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

SQLite has limited `ALTER TABLE` support. Some operations are rendered as no-ops with a comment explaining a manual table-rebuild is required:

- `DropColumn` — not supported directly in SQLite < 3.35
- `DropForeignKey` — requires table rebuild
- `AlterColumn` (nullability changes) — requires table rebuild

These operations work fully on SQL Server. For SQLite, the generated comments will guide you on how to perform the rebuild manually if needed.

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

`new` creates a blank file. `scaffold` connects to the database, compares your C# entity classes against the actual schema, and writes a migration with the necessary `AddColumn`, `CreateTable`, etc. calls pre-filled in. You still review and edit before applying.

**Q: Where should I put my migration files?**

Anywhere in your project — they just need to be compiled into your assembly. A `Migrations/` folder at the project root is the convention.

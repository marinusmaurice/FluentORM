---
title: CLI Tool
nav_order: 9
---

# `fluentorm` CLI — Migration Tool
{: .no_toc }

A standalone command-line tool for managing database migrations in any project that uses `FluentORM.Migrations`. Supports SQLite and SQL Server. Does **not** require you to modify your application code.

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Installation

### Global tool (recommended)

```bash
dotnet tool install -g FluentORM.Tools
```

After installation the `fluentorm` command is available everywhere in your terminal.

### Local project tool

```bash
dotnet new tool-manifest          # only once per repo — creates .config/dotnet-tools.json
dotnet tool install FluentORM.Tools
dotnet fluentorm --help           # invoke with dotnet prefix for local tools
```

Commit `.config/dotnet-tools.json` so teammates get the same version with `dotnet tool restore`.

### From source (development / pre-release)

```bash
cd path/to/FluentORM
dotnet pack src/FluentORM.Tools
dotnet tool install -g --add-source src/FluentORM.Tools/nupkg FluentORM.Tools
```

### Upgrade / uninstall

```bash
dotnet tool update  -g FluentORM.Tools
dotnet tool uninstall -g FluentORM.Tools
```

---

## How it works

The tool loads your compiled `.dll` at runtime via reflection. It discovers:

- **Migration classes** — any class that inherits `Migration` and has a `[Migration(version, description)]` attribute.
- **Entity classes** — any class with a `[Table("...")]` attribute (used for auto-scaffolding and drift detection).

**You must `dotnet build` before running any command.** The tool reads the compiled `.dll`, not `.cs` source files.

```
Add/change entity or migration  →  dotnet build  →  fluentorm migrations <command>
```

### Dependency resolution

If your assembly has transitive dependencies (database providers, domain libraries) the tool probes the **same directory as your `.dll`** automatically. As long as `dotnet build` has run and your `bin/` folder is populated, all dependencies will resolve.

### Version mismatch warning

The tool ships with a specific version of `FluentORM.Migrations` compiled in. If the version in your project differs, the tool prints a warning:

```
⚠ FluentORM.Migrations version mismatch: tool=1.0.2, project=1.0.1
  Run 'dotnet add package FluentORM.Migrations --version 1.0.2' to align.
```

---

## Configuration — `fluentorm.json`

Run `fluentorm init` to generate a template, then edit the four values:

```json
{
  "provider": "sqlite",
  "connectionString": "Data Source=/absolute/path/to/app.db",
  "assembly": "./bin/Debug/net8.0/MyApp.dll",
  "migrationsNamespace": "MyApp.Migrations"
}
```

Place this file in the directory where you run `fluentorm`. Every command picks it up automatically — no flags needed.

| Field | Values | Description |
|---|---|---|
| `provider` | `sqlite` \| `sqlserver` | Database engine |
| `connectionString` | any ADO.NET connection string | Connection to your database |
| `assembly` | relative or absolute path | Compiled `.dll` containing your migrations and entities |
| `migrationsNamespace` | any C# namespace | Namespace written into generated migration files |

{: .important }
**Use absolute paths in `connectionString`.** A relative path like `Data Source=app.db` is resolved against the process working directory, not the config file's directory.

Relative paths in `assembly` **are** resolved relative to the config file's directory, so `./bin/Debug/net8.0/MyApp.dll` works regardless of where you call `fluentorm` from.

### Environment variables

Override any config value without editing the file — useful for CI/CD:

| Variable | Overrides |
|---|---|
| `FLUENTORM_PROVIDER` | `provider` |
| `FLUENTORM_CONNECTION` | `connectionString` |
| `FLUENTORM_ASSEMBLY` | `assembly` |

```bash
export FLUENTORM_CONNECTION="Server=prod;Database=MyApp;Integrated Security=true"
export FLUENTORM_PROVIDER=sqlserver
export FLUENTORM_ASSEMBLY=./publish/MyApp.dll
fluentorm migrations apply
```

### Priority order (highest wins)

```
CLI flags  >  FLUENTORM_* env vars  >  fluentorm.json  >  defaults
```

---

## Quick start (2 minutes)

```bash
# 1. Go to your project folder
cd MyApp/

# 2. Create fluentorm.json
fluentorm init

# 3. Edit the four fields, then build
dotnet build

# 4. Bootstrap the model snapshot (first scaffold — no migration generated yet)
fluentorm migrations scaffold create_initial_schema --output ./Migrations

# 5. Change an entity property, rebuild, scaffold
dotnet build
fluentorm migrations scaffold add_farm_notes --output ./Migrations
# → Generates Migration_YYYYMMDDNNN_AddFarmNotes.cs

# 6. Review the file, apply
fluentorm migrations apply
```

---

## Command reference

### `fluentorm init`

Creates a `fluentorm.json` template in the current directory.

```bash
fluentorm init
fluentorm init --config ./config/fluentorm.json   # write to a specific path
```

---

### `migrations status`

Shows applied, pending (safe), and pending (destructive) migrations at a glance.

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

### `migrations list`

Lists every migration class found in the assembly with version, description, and status.

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

### `migrations preview`

Prints the SQL that *would* be executed without touching the database. Always run this before applying to production.

```bash
fluentorm migrations preview
```

```sql
-- 20240701001: add_status_to_orders [safe]
ALTER TABLE Orders ADD COLUMN status TEXT NOT NULL DEFAULT 'pending';

-- 20240702001: drop_legacy_notes [DESTRUCTIVE]
ALTER TABLE Orders DROP COLUMN legacy_notes;
```

---

### `migrations apply`

Applies pending migrations in version order. Stops before any destructive migration unless you explicitly opt in.

```bash
# Apply all safe pending migrations
fluentorm migrations apply

# Also apply destructive migrations
fluentorm migrations apply --allow-destructive

# Apply up to and including a specific version
fluentorm migrations apply --to 20240701002
fluentorm migrations apply --to 20240701002 --allow-destructive
```

Safe to re-run — already-applied migrations are skipped.

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

A migration can declare itself irreversible by throwing `IrreversibleMigrationException` in `Down()`. If you attempt to roll it back, the command exits with code 4 and no changes are made.

---

### `migrations history`

Shows the full applied-migration log recorded in `__FluentMigrations`.

```bash
fluentorm migrations history
```

```
  Applied History
  ──────────────────────────────────────────────────────────────
  #    Version              Description                              Applied At             By
  ----------------------------------------------------------------------------------------------------
  1    20240601001          create_users_table                       2024-06-01 09:12:00    BUILDSERVER
  2    20240601002          add_email_index                          2024-06-01 09:12:00    BUILDSERVER
  3    20240601003          create_orders_table                      2024-06-15 14:30:00    DEV01
```

---

### `migrations validate`

Detects schema drift (differences between your C# entity definitions and the actual database schema) and optionally verifies migration checksums.

```bash
# Drift detection only
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
════════════════════════════════════════════
```

Exit code 1 for ERROR-severity drift; 0 for warnings only; 3 for tampered checksums.

---

### `migrations new`

Creates a blank migration file from a template with the correct version number and PascalCase class name.

```bash
fluentorm migrations new add_phone_to_users
fluentorm migrations new add_phone_to_users --output ./src/Migrations
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

Version numbers use the pattern `yyyyMMddNNN`. Multiple migrations on the same day get `001`, `002`, `003`, etc.

---

### `migrations scaffold`

Auto-generates a migration file by comparing your C# entity classes against a stored model snapshot. **No database connection required.**

```bash
# First run — creates the initial snapshot (no migration generated yet)
fluentorm migrations scaffold initial --output ./Migrations

# Subsequent runs — generates a migration for any entity changes
fluentorm migrations scaffold add_new_columns --output ./Migrations

# Preview without writing any files
fluentorm migrations scaffold add_new_columns --output ./Migrations --dry-run

# Override the namespace
fluentorm migrations scaffold add_new_columns --output ./Migrations --namespace MyApp.Data.Migrations

# Fall back to live-DB drift detection instead of snapshot
fluentorm migrations scaffold add_new_columns --no-snapshot
```

#### The snapshot file

On first run, the tool writes `_FluentORM_Snapshot.json` to the output directory. **Commit it to source control** alongside your migration files.

```
./Migrations/
  _FluentORM_Snapshot.json          ← commit this
  Migration_20240601001_Initial.cs  ← commit this
  Migration_20240618001_AddPhone.cs ← commit this
```

#### Detected changes

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

Always review the generated file before applying. See [Auto-Scaffold](auto-scaffold) for the full workflow.

---

## Global flags

Apply to every migration command:

```bash
fluentorm migrations status \
  --config     ./config/fluentorm.json \
  --provider   sqlserver \
  --connection "Server=.;Database=MyApp;Integrated Security=true;TrustServerCertificate=true" \
  --assembly   ./publish/MyApp.dll \
  --namespace  MyApp.Migrations
```

---

## Workflows

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
  --assembly   ./publish/MyApp.dll

# Apply safe migrations
fluentorm migrations apply \
  --connection "$PROD_CONNECTION_STRING" \
  --assembly   ./publish/MyApp.dll

# Destructive migrations require an explicit flag — review before running
fluentorm migrations apply --allow-destructive \
  --connection "$PROD_CONNECTION_STRING" \
  --assembly   ./publish/MyApp.dll
```

### GitHub Actions

```yaml
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

### Docker

```dockerfile
# Install as a local tool (checked in via dotnet-tools.json)
RUN dotnet tool restore
RUN dotnet fluentorm migrations apply
```

---

## Exit codes

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

## The `__FluentMigrations` table

Created automatically on first run:

| Column | Type | Description |
|---|---|---|
| `Version` | BIGINT | Migration version number (primary key) |
| `Description` | NVARCHAR(500) | Human-readable description |
| `AppliedAt` | DATETIME2 | When it was applied (UTC) |
| `AppliedBy` | NVARCHAR(200) | Machine name |
| `DurationMs` | INT | How long the migration took to run |
| `Checksum` | NVARCHAR(64) | SHA-256 hash of the Up() SQL — used to detect tampering |

Do not modify this table manually. Use `fluentorm migrations validate --check-checksums` to verify integrity.

---

## SQLite limitations

| Operation | SQLite behaviour |
|---|---|
| `DropColumn` | No-op comment — column stays in the DB |
| `DropForeignKey` | No-op comment — requires table rebuild |
| `AlterColumn` nullability | No-op comment — requires table rebuild |

All three operations work fully on SQL Server.

**Rollback caveat on SQLite:** if a migration's `Up()` adds a column and `Down()` drops it, the rollback records success in `__FluentMigrations` but the column remains (because `DropColumn` is a no-op). Re-applying the migration will then fail with "duplicate column name". To recover: delete and recreate the SQLite file, or manually rebuild the table using `sqlite3`.

---

## Version compatibility

| FluentORM.Tools | FluentORM.Migrations |
|---|---|
| 1.0.2 | 1.0.2 |
| 1.0.1 | 1.0.1 |

If the versions differ the tool will warn and migration type discovery may fail. Use `dotnet tool update -g FluentORM.Tools` to align.

---

## FAQ

**Q: Do I need to drop the exe into my project folder?**

No. Install once globally with `dotnet tool install -g FluentORM.Tools`. Run `fluentorm` from any project folder and put a `fluentorm.json` in the project root so it knows where your assembly and database are.

**Q: Can I use it with multiple projects / databases?**

Yes. Each project has its own `fluentorm.json`. Or skip the config file and pass `--connection`, `--provider`, and `--assembly` flags each time.

**Q: What if my project's DLL has dependencies the tool doesn't know about?**

The tool automatically resolves unrecognised assemblies from the same directory as your `.dll`. As long as the project is built and the `bin/` folder is populated, all dependencies will be found.

**Q: Can I use it in a Docker container?**

Yes. Install it as a local tool:

```dockerfile
RUN dotnet tool restore
RUN dotnet fluentorm migrations apply
```

**Q: What does `scaffold` do that `new` doesn't?**

`new` creates a blank file you fill in yourself. `scaffold` compares your current entity classes against a stored model snapshot and writes the migration with the necessary `AddColumn`, `CreateTable`, etc. calls already filled in — no database connection required. You still review and can edit the file before applying.

**Q: When should I use `scaffold` vs `new`?**

Use `scaffold` when you've changed an entity class and want the column change generated for you. Use `new` for data migrations, column renames, raw SQL, custom index strategies, or anything that can't be inferred from entity shape alone.

**Q: What is `_FluentORM_Snapshot.json` and should I commit it?**

Yes, commit it. It is the "last known model state" used by `scaffold` to detect what changed since the last migration was generated. Without it, scaffold re-bootstraps from scratch and produces no diff. Keep it alongside your migration files in source control.

**Q: Where should I put my migration files?**

Anywhere in your project — they just need to be compiled into your assembly. A `Migrations/` folder at the project root is the convention.

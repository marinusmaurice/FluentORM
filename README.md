# FluentORM

A developer-first C# ORM targeting SQL Server and SQLite (.NET 8+).

## Design Goals

- **Explicit mutations** — no change tracking; every write names its columns
- **Readable SQL** — formatted, aliased, human-readable output always
- **Structural multi-tenancy** — TenantId injected by the framework, not convention
- **No magic, full escape hatch** — raw SQL always available, never required

## Project Structure

```
FluentORM/
├── docs/
│   └── FluentORM_Specification.docx   # Complete technical specification
├── src/
│   ├── FluentORM.Core/                # Interfaces, query builder, abstractions
│   ├── FluentORM.SqlServer/           # SQL Server dialect & connection factory
│   ├── FluentORM.Sqlite/              # SQLite dialect & connection factory
│   ├── FluentORM.Migrations/          # Schema migration engine
│   └── FluentORM.Testing/             # InMemory provider, query assertions
└── tests/
    ├── FluentORM.Core.Tests/
    ├── FluentORM.SqlServer.Tests/
    ├── FluentORM.Sqlite.Tests/
    └── FluentORM.Migrations.Tests/
```

## Status

📄 Specification complete — see `docs/FluentORM_Specification.docx`  
🔧 Implementation not yet started

## Target Platforms

| Provider     | Min Version |
|--------------|-------------|
| SQL Server   | 2019+       |
| SQLite       | 3.35+       |
| .NET         | 8.0+        |
| C#           | 12+         |

## NuGet Packages (planned)

| Package                  | Purpose                                      |
|--------------------------|----------------------------------------------|
| `FluentORM.Core`         | Interfaces, query builder, base abstractions |
| `FluentORM.SqlServer`    | SQL Server dialect & connection factory      |
| `FluentORM.Sqlite`       | SQLite dialect & connection factory          |
| `FluentORM.Migrations`   | Schema migration engine                      |
| `FluentORM.Testing`      | InMemory provider, query capture, assertions |

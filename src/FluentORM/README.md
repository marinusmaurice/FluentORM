# FluentORM

A fluent, type-safe ORM for .NET with support for SQLite and SQL Server, including database migrations and schema management.

## Features

✨ **Fluent API** - Intuitive, chainable query building  
🔒 **Type-Safe** - Full compile-time type checking  
📦 **Multi-Database** - SQLite and SQL Server support  
🚀 **High Performance** - Expression compilation and result caching  
📝 **Migrations** - Built-in schema versioning and drift detection  
🧪 **Testing Utilities** - In-memory database helpers  

## Installation

```bash
dotnet add package FluentORM
```

## Quick Start

### Basic Query

```csharp
using FluentORM.Core;

// Create a fluent database instance
var db = new FluentDb(connectionFactory);

// Query with fluent syntax
var users = await db.Query<User>()
	.Where(u => u.IsActive)
	.OrderBy(u => u.Name)
	.ToListAsync();
```

### Entity Mapping

```csharp
using FluentORM.Core.Mapping;

var map = new EntityMap<User>();
map.HasKey(u => u.Id);
map.Property(u => u.Name).HasColumnName("full_name");
map.Ignore(u => u.IgnoreMe);
```

### Migrations

```csharp
using FluentORM.Migrations;

var migration = new Migration("2024_01_01_CreateUsersTable", async context =>
{
	await context.CreateTableAsync<User>(table =>
	{
		table.Column(u => u.Id).PrimaryKey();
		table.Column(u => u.Name).NotNull();
		table.Column(u => u.Email).Unique();
	});
});
```

## Supported Databases

- **SQLite** - `FluentORM.Sqlite`
- **SQL Server** - `FluentORM.SqlServer`

## Packages Included

| Package | Purpose |
|---------|---------|
| **FluentORM.Core** | Core ORM framework |
| **FluentORM.Migrations** | Database migrations engine |
| **FluentORM.Sqlite** | SQLite provider |
| **FluentORM.SqlServer** | SQL Server provider |

## Documentation

Visit the [GitHub repository](https://github.com/marinusmaurice/FluentORM) for detailed documentation and examples.

## License

MIT License - See LICENSE file for details

## Authors

Marinus Maurice

## Support

For issues, questions, or contributions, visit:
- GitHub Issues: https://github.com/marinusmaurice/FluentORM/issues
- GitHub Discussions: https://github.com/marinusmaurice/FluentORM/discussions

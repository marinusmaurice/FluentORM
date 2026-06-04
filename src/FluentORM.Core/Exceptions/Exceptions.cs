using System;

namespace FluentORM.Core.Exceptions;

public class FluentOrmException(string message, Exception? inner = null)
    : Exception(message, inner) { }

public class ConcurrencyException(string table, object id)
    : FluentOrmException($"Concurrency conflict on '{table}' with id '{id}'. The record was modified by another process.")
{
    public string Table { get; } = table;
    public object Id { get; } = id;
}

public class TenantMismatchException(string? entityTenant, string? contextTenant)
    : FluentOrmException($"Entity TenantId '{entityTenant}' does not match context TenantId '{contextTenant}'.")
{
    public string? EntityTenant { get; } = entityTenant;
    public string? ContextTenant { get; } = contextTenant;
}

public class TenantNotResolvedException()
    : FluentOrmException("Could not resolve the current tenant. Ensure ITenantContextProvider is registered.") { }

public class EntityNotFoundException(Type entityType, object id)
    : FluentOrmException($"Entity '{entityType.Name}' with id '{id}' was not found.")
{
    public Type EntityType { get; } = entityType;
    public object Id { get; } = id;
}

public class DuplicateKeyException(string table, string message, Exception? inner = null)
    : FluentOrmException($"Duplicate key violation on '{table}': {message}", inner)
{
    public string Table { get; } = table;
}

public class MigrationConflictException(long version)
    : FluentOrmException($"Two migrations share version number {version}.")
{
    public long Version { get; } = version;
}

public class DestructiveMigrationException(long version, string description, string reason)
    : FluentOrmException($"Migration {version} ('{description}') is destructive and requires allowDestructive: true. Reason: {reason}")
{
    public long Version { get; } = version;
    public string Reason { get; } = reason;
}

public class IrreversibleMigrationException(string message)
    : FluentOrmException(message) { }

public class MigrationTamperedWithException(long version)
    : FluentOrmException($"Migration {version} checksum mismatch — the migration class was modified after being applied.")
{
    public long Version { get; } = version;
}

public class SchemaDriftException(string report)
    : FluentOrmException($"Schema drift detected:\n{report}")
{
    public string Report { get; } = report;
}

public class MigrationExecutionException(long version, string message, Exception inner)
    : FluentOrmException($"Migration {version} failed: {message}", inner)
{
    public long Version { get; } = version;
}

public class ForeignKeyViolationException(string message, Exception? inner = null)
    : FluentOrmException(message, inner) { }

public class MigrationOrderException(long attempted, long lastApplied)
    : FluentOrmException($"Cannot apply migration {attempted}: it is older than the last applied migration {lastApplied}.")
{
    public long Attempted { get; } = attempted;
    public long LastApplied { get; } = lastApplied;
}

public class NotNullWithoutDefaultException(string table, string column)
    : FluentOrmException($"Column '{column}' on table '{table}' is NOT NULL without a DEFAULT, but the table has existing rows.")
{
    public string Table { get; } = table;
    public string Column { get; } = column;
}

public class NPlusOneException(Type entityType, int count)
    : FluentOrmException($"N+1 query detected: entity '{entityType.Name}' queried {count} times in a single operation.")
{
    public Type EntityType { get; } = entityType;
    public int Count { get; } = count;
}

public class ConnectionPoolExhaustedException(string message, Exception? inner = null)
    : FluentOrmException(message, inner) { }

public class QueryTimeoutException(string message, Exception? inner = null)
    : FluentOrmException(message, inner) { }

public class UnmappedPropertyException(Type type, string property)
    : FluentOrmException($"Property '{property}' on type '{type.Name}' has no column mapping.")
{
    public Type EntityType { get; } = type;
    public string Property { get; } = property;
}

public class UnsupportedExpressionException(System.Linq.Expressions.Expression node)
    : FluentOrmException($"Expression node '{node.NodeType}' is not supported in FluentORM query compilation.") { }

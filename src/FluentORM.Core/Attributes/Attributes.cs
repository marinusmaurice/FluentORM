using System;

namespace FluentORM.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute(bool autoIncrement = true) : Attribute
{
    public bool AutoIncrement { get; } = autoIncrement;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class TenantKeyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class RowVersionAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SoftDeleteAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class AuditAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class ComputedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotNullAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class IndexAttribute : Attribute
{
    public string? Name { get; set; }
    public bool IsUnique { get; set; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class UniqueIndexAttribute : Attribute
{
    public string? Name { get; set; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class DefaultValueAttribute(object value) : Attribute
{
    public object Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class NoCacheAttribute : Attribute { }

/// <summary>Opts a table out of the global audit trail even when AuditAll() is configured.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoAuditAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class EncryptedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class DestructiveAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class MigrationAttribute(long version, string description) : Attribute
{
    public long Version { get; } = version;
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ForeignKeyAttribute<TRef>(string? constraintName = null) : Attribute
{
    public Type ReferencedType { get; } = typeof(TRef);
    public string? ConstraintName { get; } = constraintName;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxLengthAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}

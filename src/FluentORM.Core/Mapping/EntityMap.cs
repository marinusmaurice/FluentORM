using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace FluentORM.Core.Mapping;

/// <summary>
/// Fluent code-first mapping base class. Derive from this to map an entity class.
/// </summary>
public abstract class EntityMap<T> where T : class
{
    private string? _tableName;
    private readonly List<ColumnMapBuilder> _columns = new();

    protected void ToTable(string name) => _tableName = name;

    protected PrimaryKeyBuilder Key(Expression<Func<T, object?>> expr)
    {
        var property = GetProperty(expr);
        var builder = new PrimaryKeyBuilder(property);
        _columns.Add(builder);
        return builder;
    }

    protected ColumnMapBuilder Column(Expression<Func<T, object?>> expr)
    {
        var property = GetProperty(expr);
        var builder = new ColumnMapBuilder(property);
        _columns.Add(builder);
        return builder;
    }

    protected void Index(Expression<Func<T, object?>> expr) { }

    internal EntityDescriptor<T> Build()
    {
        var type = typeof(T);
        var tableName = _tableName ?? type.Name;

        var allProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetCustomAttribute<Attributes.IgnoreAttribute>() == null)
            .ToArray();

        var builtCols = new List<ColumnMap>();
        var coveredProps = new HashSet<string>();

        foreach (var b in _columns)
        {
            builtCols.Add(b.Build());
            coveredProps.Add(b.PropertyName);
        }

        foreach (var prop in allProperties)
        {
            if (!coveredProps.Contains(prop.Name))
                builtCols.Add(ColumnMap.FromProperty(prop));
        }

        return new EntityDescriptor<T>(tableName, builtCols);
    }

    private static PropertyInfo GetProperty(LambdaExpression expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression unary) body = unary.Operand;
        if (body is MemberExpression member && member.Member is PropertyInfo pi)
            return pi;
        throw new InvalidOperationException("Expression must be a property access.");
    }
}

public class ColumnMapBuilder
{
    protected readonly PropertyInfo _property;
    protected string? _columnName;
    protected bool _isTenantKey;
    protected bool _isRowVersion;
    protected bool _isSoftDelete;
    protected bool _isAudited;
    protected bool _isComputed;
    protected bool _notNull;
    protected int? _maxLength;
    protected object? _defaultValue;
    protected bool _hasDefault;

    internal string PropertyName => _property.Name;

    internal ColumnMapBuilder(PropertyInfo property) => _property = property;

    public ColumnMapBuilder HasColumnName(string name) { _columnName = name; return this; }
    public ColumnMapBuilder IsTenantKey() { _isTenantKey = true; return this; }
    public ColumnMapBuilder IsRowVersion() { _isRowVersion = true; return this; }
    public ColumnMapBuilder IsSoftDelete() { _isSoftDelete = true; return this; }
    public ColumnMapBuilder IsAudited() { _isAudited = true; return this; }
    public ColumnMapBuilder IsComputed() { _isComputed = true; return this; }
    public ColumnMapBuilder NotNull() { _notNull = true; return this; }
    public ColumnMapBuilder MaxLength(int len) { _maxLength = len; return this; }
    public ColumnMapBuilder Default(object value) { _defaultValue = value; _hasDefault = true; return this; }

    internal virtual ColumnMap Build()
    {
        var colName = _columnName ?? _property.Name;
        return new ColumnMap(_property, colName)
        {
            IsTenantKey = _isTenantKey,
            IsRowVersion = _isRowVersion,
            IsSoftDelete = _isSoftDelete,
            IsAudited = _isAudited,
            IsComputed = _isComputed,
            IsNotNull = _notNull,
            MaxLength = _maxLength,
            DefaultValue = _defaultValue,
            HasDefaultValue = _hasDefault,
        };
    }
}

public sealed class PrimaryKeyBuilder : ColumnMapBuilder
{
    private bool _autoIncrement;

    internal PrimaryKeyBuilder(PropertyInfo property) : base(property) { }

    public PrimaryKeyBuilder AutoIncrement() { _autoIncrement = true; return this; }
    public new PrimaryKeyBuilder HasColumnName(string name) { _columnName = name; return this; }

    internal override ColumnMap Build()
    {
        var colName = _columnName ?? _property.Name;
        return new ColumnMap(_property, colName)
        {
            IsPrimaryKey = true,
            AutoIncrement = _autoIncrement,
            IsNotNull = true,
        };
    }
}

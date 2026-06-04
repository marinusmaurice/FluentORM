using System;
using System.Collections.Generic;
using System.Reflection;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Mapping;

namespace FluentORM.Migrations.Schema;

public sealed class CreateTableBuilder<T> where T : class
{
    private readonly EntityDescriptor<T> _descriptor;
    private readonly List<ColumnDefinition> _columns = new();
    private readonly List<IndexDefinition> _indexes = new();

    public CreateTableBuilder(EntityDescriptor<T> descriptor) => _descriptor = descriptor;

    public PrimaryKeyColumnBuilder PrimaryKey(System.Linq.Expressions.Expression<Func<T, object?>> expr)
    {
        var prop = GetProp(expr);
        var col = new ColumnDefinition
        {
            Name = prop.Name,
            SqlType = prop.PropertyType.Name,
            IsPrimaryKey = true,
            IsNullable = false
        };
        _columns.Add(col);
        return new PrimaryKeyColumnBuilder(col);
    }

    public CreateColumnBuilder Column(System.Linq.Expressions.Expression<Func<T, object?>> expr)
    {
        var prop = GetProp(expr);
        var existing = _descriptor.TryResolve(prop.Name);
        var col = new ColumnDefinition
        {
            Name = existing?.ColumnName ?? prop.Name,
            SqlType = prop.PropertyType.Name,
            IsNullable = true
        };
        _columns.Add(col);
        return new CreateColumnBuilder(col);
    }

    public void Index(System.Linq.Expressions.Expression<Func<T, object?>> expr)
    {
        var names = ExtractNames(expr);
        _indexes.Add(new IndexDefinition
        {
            Name = $"IX_{_descriptor.TableName}_{string.Join("_", names)}",
            Columns = names,
            IsUnique = false
        });
    }

    public void UniqueIndex(System.Linq.Expressions.Expression<Func<T, object?>> expr)
    {
        var names = ExtractNames(expr);
        _indexes.Add(new IndexDefinition
        {
            Name = $"UIX_{_descriptor.TableName}_{string.Join("_", names)}",
            Columns = names,
            IsUnique = true
        });
    }

    public (List<ColumnDefinition> Columns, List<IndexDefinition> Indexes) Build(ISqlDialect dialect) =>
        (_columns, _indexes);

    private static PropertyInfo GetProp(System.Linq.Expressions.LambdaExpression expr)
    {
        var body = expr.Body;
        if (body is System.Linq.Expressions.UnaryExpression u) body = u.Operand;
        if (body is System.Linq.Expressions.MemberExpression m && m.Member is PropertyInfo pi) return pi;
        throw new InvalidOperationException("Must be a property expression.");
    }

    private static IReadOnlyList<string> ExtractNames(System.Linq.Expressions.LambdaExpression expr)
    {
        var body = expr.Body;
        if (body is System.Linq.Expressions.UnaryExpression u) body = u.Operand;
        if (body is System.Linq.Expressions.NewExpression newExpr)
            return newExpr.Members?.Select(m => m.Name).ToList() ?? [];
        if (body is System.Linq.Expressions.MemberExpression member)
            return [member.Member.Name];
        return [];
    }
}

public sealed class CreateColumnBuilder
{
    private readonly ColumnDefinition _col;
    internal CreateColumnBuilder(ColumnDefinition col) => _col = col;

    public CreateColumnBuilder NotNull() { _col.IsNullable = false; return this; }
    public CreateColumnBuilder Nullable() { _col.IsNullable = true; return this; }
    public CreateColumnBuilder MaxLength(int len) { _col.MaxLength = len; return this; }
    public CreateColumnBuilder Default(object value) { _col.Default = value; _col.HasDefault = true; return this; }
    public CreateColumnBuilder IsRowVersion() { _col.IsRowVersion = true; return this; }
    public CreateColumnBuilder IsTenantKey() { _col.IsNullable = false; return this; }
    public CreateColumnBuilder IsSoftDelete() { _col.IsNullable = true; return this; }
    public CreateColumnBuilder IsAudited() => this;
    public CreateColumnBuilder IsComputed() => this;
    public CreateColumnBuilder References<TParent>() where TParent : class => this;
}

public sealed class PrimaryKeyColumnBuilder
{
    private readonly ColumnDefinition _col;
    internal PrimaryKeyColumnBuilder(ColumnDefinition col) => _col = col;

    public PrimaryKeyColumnBuilder AutoIncrement() { _col.IsAutoIncrement = true; return this; }
}

public sealed class AddColumnBuilder<T> where T : class
{
    private readonly ColumnMap _col;
    private readonly EntityDescriptor<T> _descriptor;
    private readonly ISqlDialect _dialect;
    private readonly List<string> _statements;
    private readonly ColumnDefinition _def;

    internal AddColumnBuilder(ColumnMap col, EntityDescriptor<T> descriptor, ISqlDialect dialect, List<string> statements)
    {
        _col = col;
        _descriptor = descriptor;
        _dialect = dialect;
        _statements = statements;
        _def = new ColumnDefinition
        {
            Name = col.ColumnName,
            SqlType = col.ClrType.Name,
            IsNullable = !col.IsNotNull
        };
        Emit();
    }

    private void Emit() =>
        _statements.Add(_dialect.RenderAddColumn(_descriptor.TableName, _def));

    public AddColumnBuilder<T> AsType<TType>() { _def.SqlType = typeof(TType).Name; Refresh(); return this; }
    public AddColumnBuilder<T> Nullable() { _def.IsNullable = true; Refresh(); return this; }
    public AddColumnBuilder<T> MaxLength(int len) { _def.MaxLength = len; Refresh(); return this; }
    public AddColumnBuilder<T> IsRowVersion() { _def.IsRowVersion = true; Refresh(); return this; }

    /// <summary>
    /// Mark the new column as NOT NULL.
    /// Requires .Default(value) to also be called, because existing rows would otherwise fail the constraint.
    /// Throws <see cref="NotNullWithoutDefaultException"/> if Default() was not called before this.
    /// </summary>
    public AddColumnBuilder<T> NotNull()
    {
        _def.IsNullable = false;
        if (!_def.HasDefault)
            throw new Core.Exceptions.NotNullWithoutDefaultException(
                _descriptor.TableName, _def.Name);
        Refresh();
        return this;
    }

    public AddColumnBuilder<T> Default(object value)
    {
        _def.Default = value;
        _def.HasDefault = true;
        Refresh();
        return this;
    }

    private void Refresh()
    {
        _statements.RemoveAt(_statements.Count - 1);
        Emit();
    }
}

public sealed class AlterColumnBuilder<T> where T : class
{
    private readonly PropertyInfo _prop;
    private readonly EntityDescriptor<T> _descriptor;
    private readonly ISqlDialect _dialect;
    private readonly List<string> _statements;
    private readonly ColumnDefinition _def;

    internal AlterColumnBuilder(PropertyInfo prop, EntityDescriptor<T> descriptor, ISqlDialect dialect, List<string> statements)
    {
        _prop = prop;
        _descriptor = descriptor;
        _dialect = dialect;
        _statements = statements;
        _def = new ColumnDefinition
        {
            Name = prop.Name,
            SqlType = prop.PropertyType.Name,
            IsNullable = true
        };
    }

    public AlterColumnBuilder<T> AsType<TType>() { _def.SqlType = typeof(TType).Name; return this; }
    public AlterColumnBuilder<T> NotNull() { _def.IsNullable = false; Emit(); return this; }
    public AlterColumnBuilder<T> Nullable() { _def.IsNullable = true; Emit(); return this; }
    public AlterColumnBuilder<T> Default(object value) { _def.Default = value; _def.HasDefault = true; Emit(); return this; }

    private void Emit() =>
        _statements.Add(_dialect.RenderAlterColumnNullability(_descriptor.TableName, _def));
}

public sealed class IndexBuilder<T> where T : class
{
    private readonly IndexDefinition _index;
    private readonly EntityDescriptor<T> _descriptor;
    private readonly ISqlDialect _dialect;
    private readonly List<string> _statements;

    internal IndexBuilder(IndexDefinition index, EntityDescriptor<T> descriptor, ISqlDialect dialect, List<string> statements)
    {
        _index = index;
        _descriptor = descriptor;
        _dialect = dialect;
        _statements = statements;
        Emit();
    }

    public IndexBuilder<T> Clustered() { _index.IsClustered = true; Refresh(); return this; }

    private void Emit() =>
        _statements.Add(_dialect.RenderAddIndex(_descriptor.TableName, _index));

    private void Refresh()
    {
        _statements.RemoveAt(_statements.Count - 1);
        Emit();
    }
}

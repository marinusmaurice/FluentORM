using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Mapping;

namespace FluentORM.Migrations.Schema;

public sealed class SchemaBuilder
{
    private readonly ISqlDialect _dialect;
    private readonly EntityMapRegistry _registry;
    private readonly List<string> _statements = new();

    public DbProvider Provider => _dialect.Provider;

    public SchemaBuilder(ISqlDialect dialect, EntityMapRegistry registry)
    {
        _dialect = dialect;
        _registry = registry;
    }

    public IReadOnlyList<string> Statements => _statements;

    // ── Table operations ──────────────────────────────────────────────────────

    public void CreateTable<T>(Action<CreateTableBuilder<T>> configure) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var tableBuilder = new CreateTableBuilder<T>(descriptor);
        configure(tableBuilder);

        var (cols, indexes) = tableBuilder.Build(_dialect);
        var pkCol = cols.FirstOrDefault(c => c.IsPrimaryKey);
        var sql = _dialect.RenderCreateTable(descriptor.TableName, cols, indexes, pkCol?.Name);
        _statements.Add(sql);
    }

    public void DropTable<T>() where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.RenderDropTable(descriptor.TableName));
    }

    public void DropTable(string tableName) =>
        _statements.Add(_dialect.RenderDropTable(tableName));

    public void RenameTable<T>(string newName) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.RenderRenameTable(descriptor.TableName, newName));
    }

    public void TruncateTable<T>() where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.Provider == DbProvider.SqlServer
            ? $"TRUNCATE TABLE [{descriptor.TableName}];"
            : $"DELETE FROM {descriptor.TableName};");
    }

    // ── Column operations ─────────────────────────────────────────────────────

    public AddColumnBuilder<T> AddColumn<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var prop = GetProperty(expr);
        var colMap = ColumnMap.FromProperty(prop);
        return new AddColumnBuilder<T>(colMap, descriptor, _dialect, _statements);
    }

    public void DropColumn<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var prop = GetProperty(expr);
        _statements.Add(_dialect.RenderDropColumn(descriptor.TableName, prop.Name));
    }

    public void RenameColumn<T>(string old, string @new) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.RenderRenameColumn(descriptor.TableName, old, @new));
    }

    public AlterColumnBuilder<T> AlterColumn<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var prop = GetProperty(expr);
        return new AlterColumnBuilder<T>(prop, descriptor, _dialect, _statements);
    }

    // ── Key & Constraint operations ───────────────────────────────────────────

    public void AddPrimaryKey<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var prop = GetProperty(expr);
        _statements.Add(_dialect.Provider == DbProvider.SqlServer
            ? $"ALTER TABLE [{descriptor.TableName}] ADD PRIMARY KEY ([{prop.Name}]);"
            : $"-- SQLite: PK must be defined in CREATE TABLE");
    }

    public void DropPrimaryKey<T>() where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.Provider == DbProvider.SqlServer
            ? $"ALTER TABLE [{descriptor.TableName}] DROP CONSTRAINT [PK_{descriptor.TableName}];"
            : $"-- SQLite: PK removal requires table rebuild");
    }

    public void AddForeignKey<TChild, TParent>(
        Expression<Func<TChild, object?>> childCol,
        Expression<Func<TParent, object?>> parentCol,
        CascadeRule onDelete = CascadeRule.Restrict)
        where TChild : class where TParent : class
    {
        var childDesc = _registry.GetDescriptor<TChild>();
        var parentDesc = _registry.GetDescriptor<TParent>();
        var childProp = GetProperty(childCol);
        var parentProp = GetProperty(parentCol);
        var fk = new ForeignKeyDefinition
        {
            ConstraintName = $"FK_{childDesc.TableName}_{childProp.Name}",
            Table = childDesc.TableName,
            Column = childProp.Name,
            ReferencedTable = parentDesc.TableName,
            ReferencedColumn = parentProp.Name,
            OnDelete = onDelete.ToString().ToUpper()
        };
        _statements.Add(_dialect.RenderAddForeignKey(fk));
    }

    public void DropForeignKey<T>(string constraintName) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.RenderDropForeignKey(descriptor.TableName, constraintName));
    }

    public void AddUniqueConstraint<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var idx = new IndexDefinition
        {
            Name = $"UQ_{descriptor.TableName}_{System.Guid.NewGuid():N}",
            Columns = ExtractColumnNames(expr),
            IsUnique = true
        };
        _statements.Add(_dialect.RenderAddIndex(descriptor.TableName, idx));
    }

    // ── Index operations ──────────────────────────────────────────────────────

    public IndexBuilder<T> AddIndex<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var cols = ExtractColumnNames(expr);
        var indexDef = new IndexDefinition
        {
            Name = $"IX_{descriptor.TableName}_{string.Join("_", cols)}",
            Columns = cols,
            IsUnique = false
        };
        return new IndexBuilder<T>(indexDef, descriptor, _dialect, _statements);
    }

    public void AddUniqueIndex<T>(Expression<Func<T, object?>> expr) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        var cols = ExtractColumnNames(expr);
        var indexDef = new IndexDefinition
        {
            Name = $"UIX_{descriptor.TableName}_{string.Join("_", cols)}",
            Columns = cols,
            IsUnique = true
        };
        _statements.Add(_dialect.RenderAddIndex(descriptor.TableName, indexDef));
    }

    public void DropIndex<T>(string indexName) where T : class
    {
        var descriptor = _registry.GetDescriptor<T>();
        _statements.Add(_dialect.RenderDropIndex(indexName, descriptor.TableName));
    }

    // ── Raw SQL ───────────────────────────────────────────────────────────────

    public void Sql(string rawSql) => _statements.Add(rawSql.TrimEnd() + ";");

    public string ToSql() => string.Join("\n\n", _statements);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PropertyInfo GetProperty(LambdaExpression expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression unary) body = unary.Operand;
        if (body is MemberExpression member && member.Member is PropertyInfo pi) return pi;
        throw new InvalidOperationException("Expression must be a property access.");
    }

    private static IReadOnlyList<string> ExtractColumnNames(LambdaExpression expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression unary) body = unary.Operand;
        if (body is NewExpression newExpr)
            return newExpr.Members?.Select(m => m.Name).ToList() ?? [];
        if (body is MemberExpression member)
            return [member.Member.Name];
        return [];
    }
}

public enum CascadeRule { Restrict, Cascade, SetNull, NoAction }

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Mapping;

namespace FluentORM.Core.Mutations;

public sealed class MutationCompiler
{
    private readonly ISqlDialect _dialect;

    public MutationCompiler(ISqlDialect dialect) => _dialect = dialect;

    public CompiledQuery CompileInsert<T>(T entity, EntityDescriptor<T> map,
        IReadOnlyList<ColumnMap>? explicitCols = null) where T : class
    {
        var cols = explicitCols ?? map.InsertColumns;
        var parameters = new ParameterBag();
        var sb = new StringBuilder();

        sb.AppendLine($"INSERT INTO {_dialect.QualifyTable(null, map.TableName)}");
        sb.Append("    (");
        sb.Append(string.Join(", ", cols.Select(c => c.ColumnName)));
        sb.AppendLine(")");
        sb.Append("VALUES (");
        sb.Append(string.Join(", ", cols.Select(c =>
            parameters.Add(c.GetValue(entity), c.PropertyName.ToLower()))));
        sb.Append(')');

        return new CompiledQuery { Sql = sb.ToString(), Parameters = parameters.Parameters };
    }

    public CompiledQuery CompileInsertAndReturn<T>(T entity, EntityDescriptor<T> map) where T : class
    {
        var compiled = CompileInsert(entity, map);
        var sql = compiled.Sql + ";\n" + _dialect.ReturnInsertedIdSql();
        return new CompiledQuery { Sql = sql, Parameters = compiled.Parameters };
    }

    public CompiledQuery CompileInsertOrIgnore<T>(T entity, EntityDescriptor<T> map,
        IReadOnlyList<string> conflictColumns) where T : class
    {
        if (_dialect.Provider == DbProvider.Sqlite)
        {
            var compiled = CompileInsert(entity, map);
            var sql = compiled.Sql.Replace("INSERT INTO", "INSERT OR IGNORE INTO");
            return new CompiledQuery { Sql = sql, Parameters = compiled.Parameters };
        }
        else
        {
            // SQL Server: use MERGE or IF NOT EXISTS pattern
            var insertCompiled = CompileInsert(entity, map);
            return insertCompiled; // simplified — full MERGE for production
        }
    }

    public CompiledQuery CompileUpdate<T>(T entity, EntityDescriptor<T> map,
        IReadOnlyList<ColumnMap>? explicitCols = null) where T : class
    {
        // Always exclude RowVersion from the SET columns — the ORM auto-increments it
        var setCols = (explicitCols
            ?? map.WriteableColumns.Where(c => !c.IsPrimaryKey && !c.IsTenantKey && !c.IsRowVersion))
            .Where(c => !c.IsRowVersion)
            .ToList();

        var parameters = new ParameterBag();
        var sb = new StringBuilder();
        sb.AppendLine($"UPDATE {_dialect.QualifyTable(null, map.TableName)}");
        sb.AppendLine("SET");

        var setFragments = setCols
            .Select(c => $"{c.ColumnName} = {parameters.Add(c.GetValue(entity), c.PropertyName.ToLower())}")
            .ToList();
        if (map.RowVersionColumn != null)
            setFragments.Add($"{map.RowVersionColumn.ColumnName} = {map.RowVersionColumn.ColumnName} + 1");
        sb.AppendLine("    " + string.Join(",\n    ", setFragments));

        sb.AppendLine("WHERE");
        sb.AppendLine($"    {map.PrimaryKey!.ColumnName} = {parameters.Add(map.PrimaryKey!.GetValue(entity), "id")}");

        if (map.TenantKeyColumn != null)
            sb.AppendLine($"    AND {map.TenantKeyColumn.ColumnName} = {parameters.Add(map.TenantKeyColumn.GetValue(entity), "tenantId")}");

        if (map.RowVersionColumn != null)
            sb.Append($"    AND {map.RowVersionColumn.ColumnName}" +
                $" = {parameters.Add(map.RowVersionColumn.GetValue(entity), "version")}");

        return new CompiledQuery { Sql = sb.ToString().TrimEnd(), Parameters = parameters.Parameters };
    }

    public CompiledQuery CompileDelete<T>(object id, EntityDescriptor<T> map,
        string? tenantId = null, bool softDelete = false) where T : class
    {
        var parameters = new ParameterBag();
        var sb = new StringBuilder();

        if (softDelete && map.SoftDeleteColumn != null)
        {
            sb.AppendLine($"UPDATE {_dialect.QualifyTable(null, map.TableName)}");
            sb.AppendLine("SET");
            sb.AppendLine($"    {map.SoftDeleteColumn.ColumnName} = {_dialect.NowExpression}");
            sb.AppendLine("WHERE");
            sb.AppendLine($"    {map.PrimaryKey!.ColumnName} = {parameters.Add(id, "id")}");
        }
        else
        {
            sb.AppendLine($"DELETE FROM {_dialect.QualifyTable(null, map.TableName)}");
            sb.AppendLine("WHERE");
            sb.AppendLine($"    {map.PrimaryKey!.ColumnName} = {parameters.Add(id, "id")}");
        }

        if (tenantId != null && map.TenantKeyColumn != null)
            sb.Append($"    AND {map.TenantKeyColumn.ColumnName} = {parameters.Add(tenantId, "tenantId")}");

        return new CompiledQuery { Sql = sb.ToString().TrimEnd(), Parameters = parameters.Parameters };
    }

    public CompiledQuery CompileUpsert<T>(T entity, EntityDescriptor<T> map,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<ColumnMap>? updateOnlyCols = null) where T : class
    {
        var insertCols = map.InsertColumns;
        var updateCols = updateOnlyCols
            ?? map.WriteableColumns.Where(c => !c.IsPrimaryKey && !c.IsTenantKey).ToList();

        var parameters = new ParameterBag();

        if (_dialect.Provider == DbProvider.Sqlite)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"INSERT INTO {_dialect.QualifyTable(null, map.TableName)}");
            sb.Append("    (");
            sb.Append(string.Join(", ", insertCols.Select(c => c.ColumnName)));
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("    (" + string.Join(", ", insertCols.Select(c =>
                parameters.Add(c.GetValue(entity), c.PropertyName.ToLower()))) + ")");
            sb.AppendLine($"ON CONFLICT ({string.Join(", ", conflictColumns)}) DO UPDATE SET");
            sb.Append("    " + string.Join(",\n    ", updateCols.Select(c =>
                $"{c.ColumnName} = excluded.{c.ColumnName}")));
            return new CompiledQuery { Sql = sb.ToString().TrimEnd(), Parameters = parameters.Parameters };
        }
        else
        {
            // SQL Server MERGE
            var sb = new StringBuilder();
            var targetAlias = "target";
            var sourceAlias = "source";
            sb.AppendLine($"MERGE INTO {_dialect.QualifyTable(null, map.TableName)} AS {targetAlias}");
            sb.AppendLine($"USING (SELECT {string.Join(", ", insertCols.Select(c =>
                parameters.Add(c.GetValue(entity), c.PropertyName.ToLower()) + " AS " + c.ColumnName))}) AS {sourceAlias}");
            sb.AppendLine($"    ON ({string.Join(" AND ", conflictColumns.Select(col => $"{targetAlias}.{col} = {sourceAlias}.{col}"))})");
            sb.AppendLine("WHEN MATCHED THEN");
            sb.AppendLine($"    UPDATE SET {string.Join(", ", updateCols.Select(c => $"{targetAlias}.{c.ColumnName} = {sourceAlias}.{c.ColumnName}"))}");
            sb.AppendLine("WHEN NOT MATCHED THEN");
            sb.AppendLine($"    INSERT ({string.Join(", ", insertCols.Select(c => c.ColumnName))})");
            sb.Append($"    VALUES ({string.Join(", ", insertCols.Select(c => $"{sourceAlias}.{c.ColumnName}"))});");
            return new CompiledQuery { Sql = sb.ToString(), Parameters = parameters.Parameters };
        }
    }

    public CompiledQuery CompileRestore<T>(object id, EntityDescriptor<T> map,
        string? tenantId = null) where T : class
    {
        if (map.SoftDeleteColumn == null)
            throw new InvalidOperationException($"Entity '{typeof(T).Name}' does not have a [SoftDelete] column.");

        var parameters = new ParameterBag();
        var sb = new StringBuilder();
        sb.AppendLine($"UPDATE {_dialect.QualifyTable(null, map.TableName)}");
        sb.AppendLine("SET");
        sb.AppendLine($"    {map.SoftDeleteColumn.ColumnName} = NULL");
        sb.AppendLine("WHERE");
        sb.Append($"    {map.PrimaryKey!.ColumnName} = {parameters.Add(id, "id")}");
        if (tenantId != null && map.TenantKeyColumn != null)
            sb.Append($"\n    AND {map.TenantKeyColumn.ColumnName} = {parameters.Add(tenantId, "tenantId")}");

        return new CompiledQuery { Sql = sb.ToString().TrimEnd(), Parameters = parameters.Parameters };
    }

    private static IReadOnlyList<string> ExtractPropertyNames<T>(Expression<Func<T, object>> expr) where T : class
    {
        if (expr.Body is NewExpression newExpr)
            return newExpr.Members?.Select(m => m.Name).ToList() ?? [];
        if (expr.Body is MemberExpression memberExpr)
            return [memberExpr.Member.Name];
        return [];
    }
}

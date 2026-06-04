using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Mapping;

namespace FluentORM.Sqlite;

public sealed class SqliteDialect : ISqlDialect
{
    public DbProvider Provider => DbProvider.Sqlite;

    public string NowExpression => "datetime('now')";

    public string RenderPaging(int? skip, int? take)
    {
        var sb = new StringBuilder();
        if (take.HasValue) sb.AppendLine($"LIMIT @take");
        if (skip.HasValue) sb.Append($"OFFSET @skip");
        return sb.ToString().TrimEnd();
    }

    public string IdentityColumnDefinition() => "INTEGER PRIMARY KEY AUTOINCREMENT";

    public string ReturnInsertedIdSql() => "SELECT last_insert_rowid();";

    public string RowVersionColumnDefinition() => "INTEGER NOT NULL DEFAULT 1";

    public string RowVersionUpdateFragment(string alias, string columnName) =>
        $"{alias}.{columnName}";

    public string Concat(string a, string b) => $"{a} || {b}";

    public string JsonExtract(string col, string path) => $"json_extract({col}, '{path}')";

    public string QualifyTable(string? schema, string table) => table;

    public string CurrentTimestampSql() => "datetime('now')";

    public string RenderBulkInsert<T>(IEnumerable<T> rows, EntityDescriptor<T> map) where T : class
    {
        var colNames = map.InsertColumns.Select(c => c.ColumnName).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"INSERT INTO {map.TableName} ({string.Join(", ", colNames)})");
        sb.Append("VALUES");
        bool first = true;
        int paramIdx = 0;
        foreach (var row in rows)
        {
            if (!first) sb.Append(",");
            sb.AppendLine();
            var paramList = map.InsertColumns.Select(_ => $"@p{paramIdx++}");
            sb.Append($"    ({string.Join(", ", paramList)})");
            first = false;
        }
        return sb.ToString();
    }

    public string RenderCreateTable(string tableName, IEnumerable<ColumnDefinition> columns,
        IEnumerable<IndexDefinition> indexes, string? primaryKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {tableName} (");
        var cols = columns.ToList();
        var colDefs = cols.Select(c => "    " + RenderColumnDef(c)).ToList();
        sb.AppendLine(string.Join(",\n", colDefs));
        sb.AppendLine(");");
        foreach (var idx in indexes)
            sb.AppendLine(RenderAddIndex(tableName, idx));
        return sb.ToString().TrimEnd();
    }

    private string RenderColumnDef(ColumnDefinition col)
    {
        var sb = new StringBuilder();
        sb.Append($"{col.Name} ");
        if (col.IsAutoIncrement)
            sb.Append("INTEGER PRIMARY KEY AUTOINCREMENT");
        else if (col.IsPrimaryKey)
            sb.Append("INTEGER PRIMARY KEY");
        else if (col.IsRowVersion)
            sb.Append("INTEGER NOT NULL DEFAULT 1");
        else
            sb.Append(MapClrToSql(col.SqlType, col.MaxLength));

        if (!col.IsNullable && !col.IsAutoIncrement && !col.IsPrimaryKey && !col.IsRowVersion)
            sb.Append(" NOT NULL");

        if (col.HasDefault && col.Default != null && !col.IsAutoIncrement)
            sb.Append($" DEFAULT {FormatDefault(col.Default)}");

        return sb.ToString();
    }

    private static string MapClrToSql(string clrType, int? maxLength) => clrType switch
    {
        "Int32" or "int" or "Int64" or "long" or "Int16" or "short" => "INTEGER",
        "String" or "string" => "TEXT",
        "Boolean" or "bool" => "INTEGER",
        "DateTime" => "TEXT",
        "Decimal" or "decimal" or "Double" or "double" or "Single" or "float" => "REAL",
        "Guid" => "TEXT",
        "Byte[]" => "BLOB",
        _ => "TEXT"
    };

    private static string FormatDefault(object value) => value switch
    {
        string s => $"'{s}'",
        bool b => b ? "1" : "0",
        _ => value.ToString()!
    };

    public string RenderDropTable(string tableName) => $"DROP TABLE {tableName};";

    public string RenderAddColumn(string tableName, ColumnDefinition column) =>
        $"ALTER TABLE {tableName} ADD COLUMN {RenderColumnDef(column)};";

    public string RenderDropColumn(string tableName, string columnName) =>
        BuildTableRebuildComment(tableName, $"DROP COLUMN {columnName}");

    public string RenderRenameColumn(string tableName, string oldName, string newName) =>
        $"ALTER TABLE {tableName} RENAME COLUMN {oldName} TO {newName};";

    public string RenderAlterColumnNullability(string tableName, ColumnDefinition column) =>
        BuildTableRebuildComment(tableName, $"ALTER COLUMN {column.Name}");

    public string RenderRenameTable(string oldName, string newName) =>
        $"ALTER TABLE {oldName} RENAME TO {newName};";

    public string RenderAddIndex(string tableName, IndexDefinition index)
    {
        var unique = index.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ", index.Columns);
        return $"CREATE {unique}INDEX IF NOT EXISTS {index.Name} ON {tableName} ({cols});";
    }

    public string RenderDropIndex(string indexName, string tableName) =>
        $"DROP INDEX IF EXISTS {indexName};";

    public string RenderAddForeignKey(ForeignKeyDefinition fk) =>
        $"-- FK [{fk.ConstraintName}] requires table rebuild on SQLite";

    public string RenderDropForeignKey(string tableName, string constraintName) =>
        $"-- DROP FK [{constraintName}] requires table rebuild on SQLite";

    private static string BuildTableRebuildComment(string tableName, string operation) =>
        $"-- SQLite: {operation} on {tableName} requires table rebuild\n" +
        $"-- See FluentORM SQLite migration docs for the rebuild pattern";
}

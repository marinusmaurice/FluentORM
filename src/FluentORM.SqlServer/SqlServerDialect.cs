using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Mapping;

namespace FluentORM.SqlServer;

public sealed class SqlServerDialect : ISqlDialect
{
    public DbProvider Provider => DbProvider.SqlServer;

    public string NowExpression => "GETUTCDATE()";

    public string RenderPaging(int? skip, int? take)
    {
        var sb = new StringBuilder();
        if (skip.HasValue)
            sb.AppendLine($"OFFSET @skip ROWS");
        else
            sb.AppendLine("OFFSET 0 ROWS");

        if (take.HasValue)
            sb.Append($"FETCH NEXT @take ROWS ONLY");
        return sb.ToString().TrimEnd();
    }

    public string IdentityColumnDefinition() => "INT IDENTITY(1,1)";

    public string ReturnInsertedIdSql() => "SELECT SCOPE_IDENTITY();";

    public string RowVersionColumnDefinition() => "ROWVERSION";

    public string RowVersionUpdateFragment(string alias, string columnName) =>
        $"{alias}.{columnName}";

    public string Concat(string a, string b) => $"{a} + {b}";

    public string JsonExtract(string col, string path) => $"JSON_VALUE({col}, '{path}')";

    public string QualifyTable(string? schema, string table) =>
        schema != null ? $"[{schema}].[{table}]" : $"[{table}]";

    public string CurrentTimestampSql() => "GETUTCDATE()";

    public string RenderBulkInsert<T>(IEnumerable<T> rows, EntityDescriptor<T> map) where T : class
    {
        // Actual bulk insert uses SqlBulkCopy — this generates standard batched INSERT
        var colNames = map.InsertColumns.Select(c => c.ColumnName).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"INSERT INTO [{map.TableName}] ({string.Join(", ", colNames)})");
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
        sb.AppendLine($"CREATE TABLE [{tableName}] (");
        var cols = columns.ToList();
        var colDefs = cols.Select(c => "    " + RenderColumnDef(c)).ToList();
        if (primaryKey != null)
            colDefs.Add($"    CONSTRAINT [PK_{tableName}] PRIMARY KEY ({primaryKey})");
        sb.AppendLine(string.Join(",\n", colDefs));
        sb.AppendLine(");");
        foreach (var idx in indexes)
            sb.AppendLine(RenderAddIndex(tableName, idx));
        return sb.ToString().TrimEnd();
    }

    private string RenderColumnDef(ColumnDefinition col)
    {
        var sb = new StringBuilder();
        sb.Append($"[{col.Name}] ");
        if (col.IsAutoIncrement)
            sb.Append("INT IDENTITY(1,1)");
        else if (col.IsRowVersion)
            sb.Append("ROWVERSION");
        else
            sb.Append(MapClrToSql(col.SqlType, col.MaxLength));

        if (!col.IsNullable && !col.IsRowVersion)
            sb.Append(" NOT NULL");
        else if (!col.IsAutoIncrement && !col.IsRowVersion)
            sb.Append(" NULL");

        if (col.HasDefault && col.Default != null)
            sb.Append($" DEFAULT {FormatDefault(col.Default)}");

        return sb.ToString();
    }

    private static string MapClrToSql(string clrType, int? maxLength) => clrType switch
    {
        "Int32" or "int" => "INT",
        "Int64" or "long" => "BIGINT",
        "Int16" or "short" => "SMALLINT",
        "String" or "string" => maxLength.HasValue ? $"NVARCHAR({maxLength})" : "NVARCHAR(MAX)",
        "Boolean" or "bool" => "BIT",
        "DateTime" => "DATETIME2",
        "Decimal" or "decimal" => "DECIMAL(18,4)",
        "Double" or "double" => "FLOAT",
        "Single" or "float" => "REAL",
        "Guid" => "UNIQUEIDENTIFIER",
        "Byte[]" => "VARBINARY(MAX)",
        _ => "NVARCHAR(MAX)"
    };

    private static string FormatDefault(object value) => value switch
    {
        string s => $"N'{s}'",
        bool b => b ? "1" : "0",
        _ => value.ToString()!
    };

    public string RenderDropTable(string tableName) => $"DROP TABLE [{tableName}];";

    public string RenderAddColumn(string tableName, ColumnDefinition column) =>
        $"ALTER TABLE [{tableName}] ADD {RenderColumnDef(column)};";

    public string RenderDropColumn(string tableName, string columnName) =>
        $"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}];";

    public string RenderRenameColumn(string tableName, string oldName, string newName) =>
        $"EXEC sp_rename '[{tableName}].[{oldName}]', '{newName}', 'COLUMN';";

    public string RenderAlterColumnNullability(string tableName, ColumnDefinition column) =>
        $"ALTER TABLE [{tableName}] ALTER COLUMN {RenderColumnDef(column)};";

    public string RenderRenameTable(string oldName, string newName) =>
        $"EXEC sp_rename '[{oldName}]', '{newName}';";

    public string RenderAddIndex(string tableName, IndexDefinition index)
    {
        var unique = index.IsUnique ? "UNIQUE " : "";
        var clustered = index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
        var cols = string.Join(", ", index.Columns.Select(c => $"[{c}]"));
        return $"CREATE {unique}{clustered}INDEX [{index.Name}] ON [{tableName}] ({cols});";
    }

    public string RenderDropIndex(string indexName, string tableName) =>
        $"DROP INDEX [{indexName}] ON [{tableName}];";

    public string RenderAddForeignKey(ForeignKeyDefinition fk) =>
        $"ALTER TABLE [{fk.Table}] ADD CONSTRAINT [{fk.ConstraintName}] " +
        $"FOREIGN KEY ([{fk.Column}]) REFERENCES [{fk.ReferencedTable}] ([{fk.ReferencedColumn}]) " +
        $"ON DELETE {fk.OnDelete};";

    public string RenderDropForeignKey(string tableName, string constraintName) =>
        $"ALTER TABLE [{tableName}] DROP CONSTRAINT [{constraintName}];";
}

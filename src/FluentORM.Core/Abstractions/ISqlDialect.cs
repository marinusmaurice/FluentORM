using System;
using System.Collections.Generic;
using FluentORM.Core.Mapping;

namespace FluentORM.Core.Abstractions;

public enum DbProvider { SqlServer, Sqlite }

public interface ISqlDialect
{
    DbProvider Provider { get; }

    string RenderPaging(int? skip, int? take);
    string IdentityColumnDefinition();
    string ReturnInsertedIdSql();
    string RowVersionColumnDefinition();
    string RowVersionUpdateFragment(string alias, string columnName);
    string Concat(string a, string b);
    string JsonExtract(string col, string path);
    string QualifyTable(string? schema, string table);
    string CurrentTimestampSql();
    string RenderBulkInsert<T>(IEnumerable<T> rows, EntityDescriptor<T> map) where T : class;
    string NowExpression { get; }

    string RenderCreateTable(string tableName, IEnumerable<ColumnDefinition> columns,
        IEnumerable<IndexDefinition> indexes, string? primaryKey);
    string RenderDropTable(string tableName);
    string RenderAddColumn(string tableName, ColumnDefinition column);
    string RenderDropColumn(string tableName, string columnName);
    string RenderRenameColumn(string tableName, string oldName, string newName);
    string RenderAlterColumnNullability(string tableName, ColumnDefinition column);
    string RenderRenameTable(string oldName, string newName);
    string RenderAddIndex(string tableName, IndexDefinition index);
    string RenderDropIndex(string indexName, string tableName);
    string RenderAddForeignKey(ForeignKeyDefinition fk);
    string RenderDropForeignKey(string tableName, string constraintName);
}

public sealed class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsAutoIncrement { get; set; }
    public bool IsRowVersion { get; set; }
    public object? Default { get; set; }
    public bool HasDefault { get; set; }
    public int? MaxLength { get; set; }
}

public sealed class IndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
    public bool IsUnique { get; set; }
    public bool IsClustered { get; set; }
}

public sealed class ForeignKeyDefinition
{
    public string ConstraintName { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
    public string OnDelete { get; set; } = "RESTRICT";
}

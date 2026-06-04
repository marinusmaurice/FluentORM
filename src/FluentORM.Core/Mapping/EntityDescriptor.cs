using System;
using System.Collections.Generic;
using System.Linq;

namespace FluentORM.Core.Mapping;

public sealed class EntityDescriptor<T> : IEntityDescriptor where T : class
{
    public Type EntityType => typeof(T);
    public string TableName { get; }
    public string Alias { get; }
    public ColumnMap? PrimaryKey { get; }
    public IReadOnlyList<ColumnMap> Columns { get; }
    public ColumnMap? TenantKeyColumn { get; }
    public ColumnMap? SoftDeleteColumn { get; }
    public ColumnMap? RowVersionColumn { get; }
    public IReadOnlyList<ColumnMap> AuditedColumns { get; }
    public IReadOnlyList<ColumnMap> ComputedColumns { get; }
    public bool IsNoCache { get; }

    private readonly Dictionary<string, ColumnMap> _byPropertyName;
    private readonly Dictionary<string, ColumnMap> _byColumnName;

    public EntityDescriptor(string tableName, IReadOnlyList<ColumnMap> columns)
    {
        TableName = tableName;
        Alias = tableName.Length > 0 ? tableName[0].ToString().ToLower() : "t";
        Columns = columns;

        PrimaryKey = columns.FirstOrDefault(c => c.IsPrimaryKey);

        TenantKeyColumn = columns.FirstOrDefault(c => c.IsTenantKey);
        SoftDeleteColumn = columns.FirstOrDefault(c => c.IsSoftDelete);
        RowVersionColumn = columns.FirstOrDefault(c => c.IsRowVersion);
        AuditedColumns = columns.Where(c => c.IsAudited).ToList();
        ComputedColumns = columns.Where(c => c.IsComputed).ToList();
        IsNoCache = typeof(T).GetCustomAttributes(typeof(Attributes.NoCacheAttribute), false).Length > 0;

        _byPropertyName = columns.ToDictionary(c => c.PropertyName, StringComparer.OrdinalIgnoreCase);
        _byColumnName = columns.ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);
    }

    public ColumnMap Resolve(string propertyName) =>
        _byPropertyName.TryGetValue(propertyName, out var col)
            ? col
            : throw new Exceptions.UnmappedPropertyException(typeof(T), propertyName);

    public ColumnMap? TryResolve(string propertyName) =>
        _byPropertyName.TryGetValue(propertyName, out var col) ? col : null;

    public ColumnMap? ResolveByColumn(string columnName) =>
        _byColumnName.TryGetValue(columnName, out var col) ? col : null;

    public IReadOnlyList<ColumnMap> InsertColumns =>
        Columns.Where(c => !c.IsComputed && (!c.IsPrimaryKey || !c.AutoIncrement)).ToList();

    public IReadOnlyList<ColumnMap> WriteableColumns =>
        Columns.Where(c => !c.IsComputed).ToList();
}

public interface IEntityDescriptor
{
    Type EntityType { get; }
    string TableName { get; }
    string Alias { get; }
    ColumnMap? PrimaryKey { get; }
    IReadOnlyList<ColumnMap> Columns { get; }
    ColumnMap? TenantKeyColumn { get; }
    ColumnMap? SoftDeleteColumn { get; }
    ColumnMap? RowVersionColumn { get; }
}

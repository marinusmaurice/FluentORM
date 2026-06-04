using System;
using System.Reflection;
using FluentORM.Core.Attributes;

namespace FluentORM.Core.Mapping;

public sealed class ColumnMap
{
    public string PropertyName { get; }
    public string ColumnName { get; }
    public Type ClrType { get; }
    public PropertyInfo PropertyInfo { get; }

    public bool IsPrimaryKey { get; init; }
    public bool AutoIncrement { get; init; }
    public bool IsTenantKey { get; init; }
    public bool IsRowVersion { get; init; }
    public bool IsSoftDelete { get; init; }
    public bool IsAudited { get; init; }
    public bool IsComputed { get; init; }
    public bool IsNotNull { get; init; }
    public bool IsIgnored { get; init; }
    public bool IsEncrypted { get; init; }
    public int? MaxLength { get; init; }
    public object? DefaultValue { get; init; }
    public bool HasDefaultValue { get; init; }

    public ColumnMap(PropertyInfo property, string columnName)
    {
        PropertyInfo = property;
        PropertyName = property.Name;
        ColumnName = columnName;
        ClrType = property.PropertyType;
    }

    public object? GetValue(object entity) => PropertyInfo.GetValue(entity);
    public void SetValue(object entity, object? value) => PropertyInfo.SetValue(entity, value);

    public static ColumnMap FromProperty(PropertyInfo property, object? _ = null)
    {
        var colAttr = property.GetCustomAttribute<ColumnAttribute>();
        var columnName = colAttr?.Name ?? property.Name;

        var pkAttr = property.GetCustomAttribute<PrimaryKeyAttribute>();
        var tenantAttr = property.GetCustomAttribute<TenantKeyAttribute>();
        var rvAttr = property.GetCustomAttribute<RowVersionAttribute>();
        var sdAttr = property.GetCustomAttribute<SoftDeleteAttribute>();
        var auditAttr = property.GetCustomAttribute<AuditAttribute>();
        var computedAttr = property.GetCustomAttribute<ComputedAttribute>();
        var notNullAttr = property.GetCustomAttribute<NotNullAttribute>();
        var encAttr = property.GetCustomAttribute<EncryptedAttribute>();
        var maxLenAttr = property.GetCustomAttribute<MaxLengthAttribute>();
        var defAttr = property.GetCustomAttribute<DefaultValueAttribute>();

        return new ColumnMap(property, columnName)
        {
            IsPrimaryKey = pkAttr != null,
            AutoIncrement = pkAttr?.AutoIncrement ?? false,
            IsTenantKey = tenantAttr != null,
            IsRowVersion = rvAttr != null,
            IsSoftDelete = sdAttr != null,
            IsAudited = auditAttr != null,
            IsComputed = computedAttr != null,
            IsNotNull = notNullAttr != null || (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null),
            IsEncrypted = encAttr != null,
            MaxLength = maxLenAttr?.Length,
            DefaultValue = defAttr?.Value,
            HasDefaultValue = defAttr != null,
        };
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append('_');
            sb.Append(char.ToLower(name[i]));
        }
        return sb.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Mapping;

namespace FluentORM.Core.Interceptors;

public enum MutationKind { Insert, Update, Delete }

public sealed class AuditInterceptor
{
    private readonly ITenantContextProvider? _tenantProvider;
    private readonly ICurrentUserProvider? _userProvider;
    private readonly System.Collections.Generic.HashSet<Type>? _excludedTypes;

    public AuditInterceptor(ITenantContextProvider? tenantProvider, ICurrentUserProvider? userProvider,
        System.Collections.Generic.HashSet<Type>? excludedTypes = null)
    {
        _tenantProvider = tenantProvider;
        _userProvider = userProvider;
        _excludedTypes = excludedTypes;
    }

    public AuditEntry? CapturePreMutation<T>(T? entity, EntityDescriptor<T> map, MutationKind kind)
        where T : class
    {
        if (!ShouldAudit(map)) return null;
        if (entity == null) return null;
        if (kind == MutationKind.Insert) return null;

        return new AuditEntry
        {
            TenantId = _tenantProvider?.GetCurrentTenantId() ?? string.Empty,
            UserId = _userProvider?.GetCurrentUserId() ?? string.Empty,
            Operation = kind.ToString().ToUpper(),
            TableName = map.TableName,
            PrimaryKey = map.PrimaryKey!.GetValue(entity)?.ToString() ?? string.Empty,
            OldValues = SerializeAuditedColumns(entity, map),
            Timestamp = DateTime.UtcNow,
            IpAddress = _userProvider?.GetCurrentIpAddress()
        };
    }

    public AuditEntry? CapturePostMutation<T>(T entity, EntityDescriptor<T> map, MutationKind kind, AuditEntry? pre)
        where T : class
    {
        if (!ShouldAudit(map)) return null;

        var entry = pre ?? new AuditEntry
        {
            TenantId = _tenantProvider?.GetCurrentTenantId() ?? string.Empty,
            UserId = _userProvider?.GetCurrentUserId() ?? string.Empty,
            Operation = kind.ToString().ToUpper(),
            TableName = map.TableName,
            PrimaryKey = map.PrimaryKey!.GetValue(entity)?.ToString() ?? string.Empty,
            Timestamp = DateTime.UtcNow,
            IpAddress = _userProvider?.GetCurrentIpAddress()
        };

        if (kind != MutationKind.Delete)
            entry.NewValues = SerializeAuditedColumns(entity, map);

        return entry;
    }

    private bool ShouldAudit<T>(EntityDescriptor<T> map) where T : class
    {
        // Check [NoAudit] attribute
        if (typeof(T).GetCustomAttributes(typeof(Attributes.NoAuditAttribute), false).Length > 0)
            return false;
        // Check cfg.NoAudit<T>() exclusions
        if (_excludedTypes?.Contains(typeof(T)) == true)
            return false;
        return map.AuditedColumns.Count > 0 ||
               typeof(T).GetCustomAttributes(typeof(Attributes.AuditAttribute), false).Length > 0;
    }

    private static string SerializeAuditedColumns<T>(T entity, EntityDescriptor<T> map) where T : class
    {
        var cols = map.AuditedColumns.Count > 0 ? map.AuditedColumns : map.Columns;
        var dict = new Dictionary<string, object?>();
        foreach (var col in cols)
            dict[col.ColumnName] = col.GetValue(entity);
        return JsonSerializer.Serialize(dict);
    }
}

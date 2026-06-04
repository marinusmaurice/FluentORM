using System;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Mapping;
using FluentORM.Core.Query;

namespace FluentORM.Core.Interceptors;

public sealed class TenantInjector
{
    private readonly ITenantContextProvider _provider;

    public TenantInjector(ITenantContextProvider provider) => _provider = provider;

    public void InjectQuery<T>(QueryBuilder<T> builder) where T : class
    {
        if (builder.AllTenants) return;
        var map = builder.EntityDescriptor;
        if (map.TenantKeyColumn is null) return;

        var tenantId = _provider.GetCurrentTenantId()
            ?? throw new TenantNotResolvedException();

        // Capture provider reference (not the resolved value) so plan cache re-evaluates per execution
        var capturedProvider = _provider;
        builder.WhereClauses.Insert(0, new TenantWhereClause(
            () => capturedProvider.GetCurrentTenantId(),
            map.TenantKeyColumn.ColumnName,
            map.Alias));
    }

    public void ValidateMutation<T>(T entity, EntityDescriptor<T> map) where T : class
    {
        if (map.TenantKeyColumn == null) return;
        var tenantId = _provider.GetCurrentTenantId();
        var entityTenant = map.TenantKeyColumn.GetValue(entity) as string;
        if (entityTenant != tenantId)
            throw new TenantMismatchException(entityTenant, tenantId);
    }
}

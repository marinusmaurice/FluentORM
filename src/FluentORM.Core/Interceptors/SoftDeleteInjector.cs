using FluentORM.Core.Mapping;
using FluentORM.Core.Query;

namespace FluentORM.Core.Interceptors;

public sealed class SoftDeleteInjector
{
    public void InjectQuery<T>(QueryBuilder<T> builder) where T : class
    {
        var map = builder.EntityDescriptor;
        if (map.SoftDeleteColumn == null) return;
        if (builder.IncludeDeleted) return;

        builder.WhereClauses.Insert(0, new SoftDeleteWhereClause(
            map.SoftDeleteColumn.ColumnName,
            map.Alias,
            builder.OnlyDeletedFlag));
    }
}

using System;
using System.Linq;
using System.Reflection;
using FluentORM.Core.Attributes;
using FluentORM.Core.Mapping;

namespace FluentORM.Migrations.Snapshot;

public static class SnapshotBuilder
{
    public static ModelSnapshot Build(EntityMapRegistry registry, long version = 0)
    {
        var snapshot = new ModelSnapshot { Version = version };

        foreach (var descriptor in registry.AllDescriptors)
        {
            var table = BuildTable(descriptor);
            snapshot.Tables[descriptor.TableName] = table;
        }

        return snapshot;
    }

    private static SnapshotTable BuildTable(Core.Mapping.IEntityDescriptor descriptor)
    {
        var entityType = descriptor.EntityType;

        var table = new SnapshotTable
        {
            TableName = descriptor.TableName,
            EntityTypeName = entityType.Name,
            EntityNamespace = entityType.Namespace ?? string.Empty
        };

        foreach (var col in descriptor.Columns)
        {
            table.Columns.Add(new SnapshotColumn
            {
                PropertyName   = col.PropertyName,
                ColumnName     = col.ColumnName,
                ClrType        = SerializeClrType(col.ClrType),
                IsNullable     = !col.IsNotNull,
                IsPrimaryKey   = col.IsPrimaryKey,
                IsAutoIncrement = col.AutoIncrement,
                IsRowVersion   = col.IsRowVersion,
                MaxLength      = col.MaxLength,
                DefaultValue   = col.HasDefaultValue ? col.DefaultValue?.ToString() : null
            });
        }

        // Capture indexes declared via property attributes
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var idx in property.GetCustomAttributes<IndexAttribute>())
            {
                var name = idx.Name ?? $"IX_{descriptor.TableName}_{property.Name}";
                table.Indexes.Add(new SnapshotIndex
                {
                    Name         = name,
                    PropertyName = property.Name,
                    IsUnique     = idx.IsUnique
                });
            }

            var unique = property.GetCustomAttribute<UniqueIndexAttribute>();
            if (unique != null)
            {
                var name = unique.Name ?? $"UIX_{descriptor.TableName}_{property.Name}";
                // avoid duplicate if also has [Index(IsUnique=true)]
                if (!table.Indexes.Any(i => i.Name == name))
                    table.Indexes.Add(new SnapshotIndex
                    {
                        Name         = name,
                        PropertyName = property.Name,
                        IsUnique     = true
                    });
            }
        }

        return table;
    }

    private static string SerializeClrType(Type type)
    {
        // Store as a short, human-readable name so the snapshot is easy to read
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null) return FriendlyName(underlying) + "?";
        return FriendlyName(type);
    }

    private static string FriendlyName(Type type) => type.FullName ?? type.Name;
}

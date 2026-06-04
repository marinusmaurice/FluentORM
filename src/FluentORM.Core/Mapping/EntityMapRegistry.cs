using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentORM.Core.Attributes;

namespace FluentORM.Core.Mapping;

public sealed class EntityMapRegistry
{
    private readonly ConcurrentDictionary<Type, IEntityDescriptor> _maps = new();

    public EntityDescriptor<T> GetDescriptor<T>() where T : class
    {
        if (_maps.TryGetValue(typeof(T), out var existing))
            return (EntityDescriptor<T>)existing;

        var built = Build<T>();
        _maps[typeof(T)] = built;
        return built;
    }

    public IEntityDescriptor GetDescriptor(Type type)
    {
        if (_maps.TryGetValue(type, out var existing)) return existing;
        var built = BuildUntyped(type);
        _maps[type] = built;
        return built;
    }

    public bool TryGetDescriptor(Type type, out IEntityDescriptor? map) =>
        _maps.TryGetValue(type, out map);

    internal void Register<T>(EntityDescriptor<T> descriptor) where T : class =>
        _maps[typeof(T)] = descriptor;

    public void RegisterFluentMap<TMap, TEntity>()
        where TMap : EntityMap<TEntity>, new()
        where TEntity : class
    {
        var map = new TMap();
        var descriptor = map.Build();
        _maps[typeof(TEntity)] = descriptor;
    }

    public void ScanAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (type.GetCustomAttribute<TableAttribute>() != null)
                GetDescriptor(type);
        }
    }

    private EntityDescriptor<T> Build<T>() where T : class
    {
        var type = typeof(T);
        var tableAttr = type.GetCustomAttribute<TableAttribute>();
        var tableName = tableAttr?.Name ?? type.Name;

        var columns = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetCustomAttribute<IgnoreAttribute>() == null)
            .Select(p => ColumnMap.FromProperty(p))
            .ToList();

        return new EntityDescriptor<T>(tableName, columns);
    }

    private IEntityDescriptor BuildUntyped(Type type)
    {
        var tableAttr = type.GetCustomAttribute<TableAttribute>();
        var tableName = tableAttr?.Name ?? type.Name;

        var columns = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetCustomAttribute<IgnoreAttribute>() == null)
            .Select(p => ColumnMap.FromProperty(p))
            .ToList();

        var descriptorType = typeof(EntityDescriptor<>).MakeGenericType(type);
        return (IEntityDescriptor)Activator.CreateInstance(descriptorType, tableName, columns)!;
    }

    public IEnumerable<IEntityDescriptor> AllDescriptors => _maps.Values;
}

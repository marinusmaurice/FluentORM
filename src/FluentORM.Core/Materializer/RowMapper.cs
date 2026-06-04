using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using FluentORM.Core.Mapping;

namespace FluentORM.Core.Materializer;

public sealed class MaterializerCache
{
    private static readonly ConcurrentDictionary<Type, Delegate> _cache = new();

    public static Func<IDataReader, int[], T> GetMaterializer<T>(EntityDescriptor<T> descriptor) where T : class
    {
        return (Func<IDataReader, int[], T>)_cache.GetOrAdd(typeof(T), _ => Build<T>(descriptor));
    }

    private static Func<IDataReader, int[], T> Build<T>(EntityDescriptor<T> descriptor) where T : class
    {
        var readerParam = Expression.Parameter(typeof(IDataReader), "reader");
        var ordinalsParam = Expression.Parameter(typeof(int[]), "ordinals");
        var bindings = new List<MemberBinding>();

        var writableColumns = descriptor.Columns
            .Where(c => !c.IsComputed || true) // include computed for reads
            .ToList();

        for (int i = 0; i < writableColumns.Count; i++)
        {
            var col = writableColumns[i];
            var ordinalExpr = Expression.ArrayIndex(ordinalsParam, Expression.Constant(i));
            var isNull = Expression.Call(readerParam, IsDbNullMethod, ordinalExpr);
            var getter = GetReaderMethod(col.ClrType);
            var getValue = getter != null
                ? (Expression)Expression.Call(readerParam, getter, ordinalExpr)
                : GetValueMethod(readerParam, ordinalExpr, col.ClrType);
            var defaultVal = Expression.Default(col.ClrType);
            var withNull = Expression.Condition(isNull, defaultVal, EnsureType(getValue, col.ClrType));
            bindings.Add(Expression.Bind(col.PropertyInfo, withNull));
        }

        var newExpr = Expression.MemberInit(Expression.New(typeof(T)), bindings);
        var lambda = Expression.Lambda<Func<IDataReader, int[], T>>(newExpr, readerParam, ordinalsParam);
        return lambda.CompileFast();
    }

    private static Expression EnsureType(Expression expr, Type targetType)
    {
        if (expr.Type == targetType) return expr;
        return Expression.Convert(expr, targetType);
    }

    private static readonly MethodInfo IsDbNullMethod =
        typeof(IDataRecord).GetMethod(nameof(IDataRecord.IsDBNull))!;

    private static MethodInfo? GetReaderMethod(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(int)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetInt32))!;
        if (underlying == typeof(long)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetInt64))!;
        if (underlying == typeof(short)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetInt16))!;
        if (underlying == typeof(string)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetString))!;
        if (underlying == typeof(bool)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetBoolean))!;
        if (underlying == typeof(DateTime)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetDateTime))!;
        if (underlying == typeof(decimal)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetDecimal))!;
        if (underlying == typeof(double)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetDouble))!;
        if (underlying == typeof(float)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetFloat))!;
        if (underlying == typeof(Guid)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetGuid))!;
        if (underlying == typeof(byte)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetByte))!;
        if (underlying == typeof(char)) return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetChar))!;
        return null; // fallback to GetValue + cast
    }

    private static Expression GetValueMethod(Expression readerParam, Expression ordinalExpr, Type targetType)
    {
        var getVal = Expression.Call(readerParam,
            typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetValue))!, ordinalExpr);
        return Expression.Convert(getVal, targetType);
    }
}

public sealed class RowMapper<T> where T : class
{
    private readonly EntityDescriptor<T> _descriptor;
    private readonly Func<IDataReader, int[], T> _materializer;

    public RowMapper(EntityDescriptor<T> descriptor)
    {
        _descriptor = descriptor;
        _materializer = MaterializerCache.GetMaterializer(descriptor);
    }

    public IReadOnlyList<T> MapAll(IDataReader reader)
    {
        var ordinals = ResolveOrdinals(reader);
        var results = new List<T>();
        while (reader.Read())
            results.Add(_materializer(reader, ordinals));
        return results;
    }

    public T? MapSingle(IDataReader reader)
    {
        var ordinals = ResolveOrdinals(reader);
        return reader.Read() ? _materializer(reader, ordinals) : null;
    }

    private int[] ResolveOrdinals(IDataReader reader)
    {
        var cols = _descriptor.Columns.ToList();
        var ordinals = new int[cols.Count];
        for (int i = 0; i < cols.Count; i++)
        {
            try { ordinals[i] = reader.GetOrdinal(cols[i].ColumnName); }
            catch { ordinals[i] = -1; }
        }
        return ordinals;
    }
}

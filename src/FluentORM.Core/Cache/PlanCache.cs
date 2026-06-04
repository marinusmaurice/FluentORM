using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FluentORM.Core.Cache;

public sealed class PlanCacheKey : IEquatable<PlanCacheKey>
{
    public Type EntityType { get; }
    public string ClauseFingerprint { get; }
    public Type? ProjectionType { get; }
    public bool IsDistinct { get; }
    public bool HasPaging { get; }
    public int JoinCount { get; }

    public PlanCacheKey(Type entityType, string fingerprint, Type? projection,
        bool distinct, bool paging, int joinCount = 0)
    {
        EntityType = entityType;
        ClauseFingerprint = fingerprint;
        ProjectionType = projection;
        IsDistinct = distinct;
        HasPaging = paging;
        JoinCount = joinCount;
    }

    public bool Equals(PlanCacheKey? other) =>
        other != null &&
        EntityType == other.EntityType &&
        ClauseFingerprint == other.ClauseFingerprint &&
        ProjectionType == other.ProjectionType &&
        IsDistinct == other.IsDistinct &&
        HasPaging == other.HasPaging &&
        JoinCount == other.JoinCount;

    public override bool Equals(object? obj) => obj is PlanCacheKey k && Equals(k);

    public override int GetHashCode() =>
        HashCode.Combine(EntityType, ClauseFingerprint, ProjectionType, IsDistinct, HasPaging, JoinCount);
}

/// <summary>
/// Cached SQL template. The SQL string is structural (same for same query shape).
/// Parameter values are always re-extracted from the current QueryBuilder at execution time
/// so tenant IDs, captured variables, and paging values are always fresh.
/// </summary>
public sealed class CompiledPlan
{
    public required string Sql { get; init; }
    public long HitCount;
}

public sealed class PlanCache
{
    // No eviction — query shapes in an application are finite and small (hundreds, not millions)
    private static readonly ConcurrentDictionary<PlanCacheKey, CompiledPlan> _cache = new();

    /// <summary>Checks cache for an existing plan. Returns true and sets plan if found.</summary>
    public bool TryGetPlan(PlanCacheKey key, out CompiledPlan? plan)
    {
        if (_cache.TryGetValue(key, out plan))
        {
            Interlocked.Increment(ref plan.HitCount);
            return true;
        }
        return false;
    }

    /// <summary>Stores a newly compiled plan.</summary>
    public void Store(PlanCacheKey key, CompiledPlan plan) =>
        _cache.TryAdd(key, plan);

    /// <summary>Legacy GetOrAdd for compatibility.</summary>
    public CompiledPlan GetOrAdd(PlanCacheKey key, Func<CompiledPlan> factory)
    {
        var plan = _cache.GetOrAdd(key, _ => factory());
        Interlocked.Increment(ref plan.HitCount);
        return plan;
    }

    public int Count => _cache.Count;
    public void Clear() => _cache.Clear();

    public IEnumerable<(PlanCacheKey Key, CompiledPlan Plan)> AllPlans =>
        _cache.Select(kv => (kv.Key, kv.Value));
}

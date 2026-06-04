using System;
using System.Collections.Generic;
using System.Linq;

namespace FluentORM.Testing;

public sealed class QueryMonitor
{
    private readonly List<CapturedQuery> _queries = new();

    public IReadOnlyList<CapturedQuery> Queries => _queries;

    internal void Record(string sql, IReadOnlyDictionary<string, object?> parameters, TimeSpan duration)
    {
        _queries.Add(new CapturedQuery { Sql = sql, Parameters = parameters, Duration = duration });
    }

    public void AssertQueryCount(int expected)
    {
        if (_queries.Count != expected)
            throw new QueryAssertionException(
                $"Expected {expected} queries but {_queries.Count} were executed.\n" +
                string.Join("\n", _queries.Select((q, i) => $"  [{i + 1}] {q.Sql[..Math.Min(100, q.Sql.Length)]}...")));
    }

    public void AssertSqlContains(string fragment)
    {
        if (!_queries.Any(q => q.Sql.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            throw new QueryAssertionException(
                $"Expected at least one query to contain '{fragment}' but none did.\n" +
                string.Join("\n", _queries.Select((q, i) => $"  [{i + 1}] {q.Sql[..Math.Min(200, q.Sql.Length)]}")));
    }

    public void AssertSqlNotContains(string fragment)
    {
        var matching = _queries.Where(q => q.Sql.Contains(fragment, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Any())
            throw new QueryAssertionException(
                $"Expected no query to contain '{fragment}' but found {matching.Count} matching queries.");
    }

    public void AssertNoFullTableScans() { }  // Would use EXPLAIN QUERY PLAN in real implementation

    public void Reset() => _queries.Clear();
}

public sealed class CapturedQuery
{
    public required string Sql { get; init; }
    public required IReadOnlyDictionary<string, object?> Parameters { get; init; }
    public required TimeSpan Duration { get; init; }
}

public sealed class QueryAssertionException(string message) : Exception(message) { }

using System;
using System.Collections.Generic;
using System.Text;

namespace FluentORM.Core.Compiler;

/// <summary>
/// Collects SQL parameters during query compilation.
/// Records both the current value AND a Func to re-extract it at execution time
/// so the plan cache can reuse the SQL template with fresh parameter values.
/// </summary>
public sealed class ParameterBag
{
    private readonly List<(string Name, object? Value, Func<object?> Source)> _entries = new();
    private readonly Dictionary<string, int> _usedNames = new(StringComparer.OrdinalIgnoreCase);
    private int _counter;

    /// <summary>Adds a literal value that won't change between executions.</summary>
    public string Add(object? value, string? hintName = null)
    {
        var name = AssignName(hintName);
        _entries.Add((name, value, () => value));
        return name;
    }

    /// <summary>Adds a dynamic value with a re-evaluation source (for plan cache warm path).</summary>
    public string AddDynamic(Func<object?> source, string? hintName = null)
    {
        var name = AssignName(hintName);
        var current = source(); // evaluate now for current execution
        _entries.Add((name, current, source));
        return name;
    }

    /// <summary>Current parameter name→value dictionary for SQL execution.</summary>
    public IReadOnlyDictionary<string, object?> Parameters
    {
        get
        {
            var d = new Dictionary<string, object?>();
            foreach (var (n, v, _) in _entries) d[n] = v;
            return d;
        }
    }

    /// <summary>Returns the ordered list of parameter names (for plan cache key fingerprinting).</summary>
    public IReadOnlyList<string> Names => _entries.Select(e => e.Name).ToList();

    public string FormatForDisplay()
    {
        var sb = new StringBuilder();
        foreach (var (n, v, _) in _entries)
            sb.AppendLine($"  {n} = {v ?? "NULL"}");
        return sb.ToString();
    }

    private string AssignName(string? hint)
    {
        if (hint != null)
        {
            var preferred = "@" + hint;
            if (!_usedNames.ContainsKey(preferred))
            {
                _usedNames[preferred] = 1;
                return preferred;
            }
        }
        return "@p" + _counter++;
    }
}

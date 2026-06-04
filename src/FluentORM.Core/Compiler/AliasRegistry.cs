using System;
using System.Collections.Generic;

namespace FluentORM.Core.Compiler;

public sealed class AliasRegistry
{
    private readonly Dictionary<Type, string> _aliases = new();
    private readonly Dictionary<string, int> _usedAliases = new(StringComparer.OrdinalIgnoreCase);

    public string GetAlias(Type type)
    {
        if (_aliases.TryGetValue(type, out var alias)) return alias;
        var newAlias = GenerateAlias(type.Name);
        _aliases[type] = newAlias;
        return newAlias;
    }

    public string Register(Type type, string preferredAlias)
    {
        if (_aliases.TryGetValue(type, out var existing)) return existing;
        var alias = EnsureUnique(preferredAlias);
        _aliases[type] = alias;
        return alias;
    }

    private string GenerateAlias(string typeName)
    {
        var preferred = typeName.Length > 0 ? typeName[0].ToString().ToLower() : "t";
        return EnsureUnique(preferred);
    }

    private string EnsureUnique(string preferred)
    {
        if (!_usedAliases.ContainsKey(preferred))
        {
            _usedAliases[preferred] = 1;
            return preferred;
        }
        var count = ++_usedAliases[preferred];
        return preferred + count;
    }
}

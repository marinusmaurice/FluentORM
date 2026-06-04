using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Cache;
using FluentORM.Core.Mapping;
using FluentORM.Core.Query;

namespace FluentORM.Core.Compiler;

public sealed class CompiledQuery
{
    public required string Sql { get; init; }
    public required IReadOnlyDictionary<string, object?> Parameters { get; init; }
}

public sealed class SqlCompiler
{
    private readonly EntityMapRegistry _registry;
    private readonly ISqlDialect _dialect;
    private readonly PlanCache _planCache;

    public SqlCompiler(EntityMapRegistry registry, ISqlDialect dialect, PlanCache? planCache = null)
    {
        _registry = registry;
        _dialect = dialect;
        _planCache = planCache ?? new PlanCache();
    }

    public CompiledQuery Compile<T>(QueryBuilder<T> builder) where T : class
    {
        var key = BuildCacheKey(builder);

        // Cache hit: reuse the compiled SQL string, but ALWAYS re-extract parameter values
        // from the current builder (correct tenant ID, current captured variable values, etc.)
        if (_planCache.TryGetPlan(key, out var cached))
            return new CompiledQuery { Sql = cached!.Sql, Parameters = ExtractParams(builder) };

        // Cache miss: full compilation (builds SQL + extracts params simultaneously)
        var (sql, parameters) = CompileFull(builder);
        var plan = new CompiledPlan { Sql = sql };
        _planCache.Store(key, plan);
        return new CompiledQuery { Sql = sql, Parameters = parameters };
    }

    /// <summary>
    /// Lightweight re-extraction of parameter values from the current builder.
    /// Called on every warm-path execution — skips SQL generation, only collects param values.
    /// </summary>
    private IReadOnlyDictionary<string, object?> ExtractParams<T>(QueryBuilder<T> builder) where T : class
    {
        var parameters = new ParameterBag();
        var aliases = new AliasRegistry();
        var descriptor = builder.EntityDescriptor;
        aliases.Register(typeof(T), descriptor.Alias);

        // Re-collect params in the same order as CompileFull
        foreach (var clause in builder.WhereClauses)
        {
            switch (clause)
            {
                case TenantWhereClause tc:
                    parameters.AddDynamic(tc.TenantIdSource, "tenantId");
                    break;
                case SoftDeleteWhereClause:
                    // No params — soft delete uses IS NULL / IS NOT NULL
                    break;
                case ExpressionWhereClause ew:
                    var visitor = new ExpressionToSqlVisitor(parameters, _registry, aliases);
                    visitor.Compile(ew.Expression); // side-effect: adds params
                    break;
                case WhereBetweenClause wb:
                    parameters.Add(wb.Low);
                    parameters.Add(wb.High);
                    break;
                case WhereInClause wi:
                    foreach (var v in wi.Values) parameters.Add(v);
                    break;
                case RawWhereClause rw:
                    foreach (var arg in rw.Args) parameters.Add(arg);
                    break;
            }
        }

        if (builder.Skip.HasValue) parameters.AddDynamic(() => builder.Skip, "skip");
        if (builder.Take.HasValue) parameters.AddDynamic(() => builder.Take, "take");

        return parameters.Parameters;
    }

    // ── Fingerprint & cache key ───────────────────────────────────────────────

    private static PlanCacheKey BuildCacheKey<T>(QueryBuilder<T> builder) where T : class
    {
        var sb = new StringBuilder();

        foreach (var clause in builder.WhereClauses)
        {
            sb.Append(ClauseFingerprint(clause));
            sb.Append(';');
        }
        foreach (var join in builder.Joins)
            sb.Append($"JOIN:{join.JoinType}:{join.JoinedType.Name};");
        foreach (var order in builder.OrderByClauses)
            sb.Append($"ORDER:{(order.Descending ? "DESC" : "ASC")}:{ExprFingerprint(order.Expression)};");
        foreach (var group in builder.GroupByClauses)
            sb.Append($"GROUP:{ExprFingerprint(group.Expression)};");
        if (builder.IsDistinct) sb.Append("DISTINCT;");
        if (builder.SelectProjection != null) sb.Append($"SELECT:{ExprFingerprint(builder.SelectProjection)};");

        return new PlanCacheKey(
            typeof(T),
            sb.ToString(),
            builder.Projection?.TargetType,
            builder.IsDistinct,
            builder.Skip.HasValue || builder.Take.HasValue,
            builder.Joins.Count);
    }

    private static string ClauseFingerprint(WhereClause clause) => clause switch
    {
        TenantWhereClause => "TENANT",
        SoftDeleteWhereClause sd => sd.OnlyDeleted ? "SOFT_DELETE_ONLY" : "SOFT_DELETE",
        ExpressionWhereClause ew => $"WHERE{(ew.IsOr ? "_OR" : "")}:{ExprFingerprint(ew.Expression)}",
        WhereInClause wi => $"IN:{ExprFingerprint(wi.Column)}",
        WhereBetweenClause wb => $"BETWEEN:{ExprFingerprint(wb.Column)}",
        WhereNullClause wn => $"{(wn.IsNull ? "NULL" : "NOT_NULL")}:{ExprFingerprint(wn.Column)}",
        RawWhereClause rw => $"RAW:{rw.Sql}",
        _ => clause.GetType().Name
    };

    private static string ExprFingerprint(LambdaExpression expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression u) body = u.Operand;
        return body switch
        {
            BinaryExpression b  => $"{ExprFingerprint(b.Left)}:{b.NodeType}:{ExprFingerprint(b.Right)}",
            // Entity property reference: use property name (structural)
            MemberExpression m when m.Expression is ParameterExpression => m.Member.Name,
            // Captured variable: always "CAPTURE" — the VALUE differs but the STRUCTURE is the same
            MemberExpression   => "CAPTURE",
            // Literal constant in the expression tree
            ConstantExpression => "CONST",
            MethodCallExpression mc => $"{mc.Method.Name}({string.Join(",", mc.Arguments.Select(a => ExprFingerprint(Expression.Lambda(a, expr.Parameters))))})",
            NewExpression ne => string.Join(",", ne.Members?.Select(m => m.Name) ?? []),
            _ => body.NodeType.ToString()
        };
    }

    private static string ExprFingerprint(Expression expr) => expr switch
    {
        LambdaExpression l    => ExprFingerprint(l),
        MemberExpression m when m.Expression is ParameterExpression => m.Member.Name,
        MemberExpression      => "CAPTURE",
        ConstantExpression    => "CONST",
        BinaryExpression b    => $"{b.NodeType}:{ExprFingerprint(b.Left)}:{ExprFingerprint(b.Right)}",
        _                     => expr.NodeType.ToString()
    };

    // ── Full compilation (cache miss path) ────────────────────────────────────

    private (string Sql, IReadOnlyDictionary<string, object?> Parameters) CompileFull<T>(QueryBuilder<T> builder) where T : class
    {
        var aliases = new AliasRegistry();
        var parameters = new ParameterBag();
        var visitor = new ExpressionToSqlVisitor(parameters, _registry, aliases);

        var descriptor = builder.EntityDescriptor;
        var mainAlias = aliases.Register(typeof(T), descriptor.Alias);

        var sb = new StringBuilder();

        // CTEs
        if (builder.Ctes.Any())
            AppendCtes(sb, builder.Ctes, aliases, parameters);

        // SELECT
        sb.AppendLine("SELECT");
        AppendSelect(sb, builder, descriptor, mainAlias, aliases, visitor);

        // FROM
        sb.AppendLine("FROM");
        sb.Append($"    {_dialect.QualifyTable(null, descriptor.TableName)} {mainAlias}");

        // JOINs
        foreach (var join in builder.Joins)
        {
            var joinAlias = aliases.Register(join.JoinedType, join.JoinedAlias);
            var joinDesc = _registry.GetDescriptor(join.JoinedType);
            sb.AppendLine();
            var joinKeyword = join.JoinType switch
            {
                JoinType.Inner => "INNER JOIN",
                JoinType.Left  => "LEFT JOIN",
                JoinType.Right => "RIGHT JOIN",
                JoinType.Cross => "CROSS JOIN",
                _ => "INNER JOIN"
            };
            sb.Append($"    {joinKeyword} {_dialect.QualifyTable(null, join.JoinedTableName)} {joinAlias}");
            if (join.JoinType != JoinType.Cross)
            {
                sb.AppendLine();
                var onSql = visitor.Compile(join.OnExpression);
                sb.Append($"        ON {onSql}");
            }
        }
        sb.AppendLine();

        // WHERE (soft delete + tenant are already injected into WhereClauses before Compile is called)
        if (builder.WhereClauses.Any())
        {
            sb.AppendLine("WHERE");
            AppendWhere(sb, builder.WhereClauses, visitor, parameters);
        }

        // GROUP BY
        if (builder.GroupByClauses.Any())
        {
            sb.AppendLine("GROUP BY");
            var groupExprs = builder.GroupByClauses.Select(g => "    " + visitor.Compile(g.Expression));
            sb.AppendLine(string.Join(",\n", groupExprs));
        }

        // HAVING
        if (builder.HavingClauses.Any())
        {
            sb.AppendLine("HAVING");
            bool first = true;
            foreach (var having in builder.HavingClauses)
            {
                var prefix = first ? "    " : "    AND ";
                sb.AppendLine(prefix + visitor.Compile(having.Expression));
                first = false;
            }
        }

        // ORDER BY
        if (builder.OrderByClauses.Any())
        {
            sb.AppendLine("ORDER BY");
            bool first = true;
            foreach (var order in builder.OrderByClauses)
            {
                var direction = order.Descending ? " DESC" : " ASC";
                var comma = first ? "" : ",";
                sb.AppendLine($"{comma}    {visitor.Compile(order.Expression)}{direction}");
                first = false;
            }
        }

        // PAGING
        if (builder.Skip.HasValue || builder.Take.HasValue)
        {
            // Use dynamic sources so paging values re-extracted on warm path
            var skipGetter = builder.Skip.HasValue
                ? parameters.AddDynamic(() => builder.Skip, "skip")
                : (string?)null;
            var takeGetter = builder.Take.HasValue
                ? parameters.AddDynamic(() => builder.Take, "take")
                : (string?)null;

            var paging = _dialect.RenderPaging(builder.Skip, builder.Take);
            if (skipGetter != null) paging = paging.Replace("@skip", skipGetter);
            if (takeGetter != null) paging = paging.Replace("@take", takeGetter);
            sb.AppendLine(paging);
        }

        var sql = sb.ToString().TrimEnd();
        return (sql, parameters.Parameters);
    }

    // ── SELECT rendering ─────────────────────────────────────────────────────

    private void AppendSelect<T>(StringBuilder sb, QueryBuilder<T> builder,
        EntityDescriptor<T> descriptor, string mainAlias,
        AliasRegistry aliases, ExpressionToSqlVisitor visitor) where T : class
    {
        if (builder.IsDistinct) sb.AppendLine("    DISTINCT");

        if (builder.SelectProjection != null)
        {
            sb.AppendLine(CompileProjection(builder.SelectProjection, aliases, visitor, builder));
            return;
        }

        if (builder.Projection?.TargetType != null)
        {
            var targetProps = builder.Projection.TargetType
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToArray();

            var cols = new List<string>();
            for (int i = 0; i < targetProps.Length; i++)
            {
                var prop = targetProps[i];
                var comma = i < targetProps.Length - 1 ? "," : "";
                var override_ = builder.Projection.Overrides.FirstOrDefault(o =>
                    string.Equals(o.TargetProperty, prop.Name, StringComparison.OrdinalIgnoreCase));
                if (override_ != null)
                    cols.Add($"    {visitor.Compile(override_.SourceExpression)} AS {prop.Name}{comma}");
                else
                {
                    var col = descriptor.TryResolve(prop.Name);
                    if (col != null) cols.Add($"    {mainAlias}.{col.ColumnName} AS {prop.Name}{comma}");
                }
            }
            sb.AppendLine(string.Join("\n", cols));
            return;
        }

        // Default: all non-ignored columns, one per line
        var allCols = descriptor.Columns.Where(c => !c.IsIgnored).ToList();
        for (int i = 0; i < allCols.Count; i++)
        {
            var c = allCols[i];
            var comma = i < allCols.Count - 1 ? "," : "";
            sb.AppendLine($"    {mainAlias}.{c.ColumnName}{comma}");
        }
    }

    private static string CompileProjection<T>(LambdaExpression projection, AliasRegistry aliases,
        ExpressionToSqlVisitor visitor, QueryBuilder<T> builder) where T : class
    {
        if (projection.Body is not NewExpression newExpr)
            return "    *";

        var cols = new List<string>();
        for (int i = 0; i < newExpr.Arguments.Count; i++)
        {
            var arg = newExpr.Arguments[i];
            var name = newExpr.Members?[i].Name;
            var inner = Expression.Lambda(arg, projection.Parameters);
            var colSql = visitor.Compile(inner);
            var comma = i < newExpr.Arguments.Count - 1 ? "," : "";
            cols.Add(name != null ? $"    {colSql} AS {name}{comma}" : $"    {colSql}{comma}");
        }
        return string.Join("\n", cols);
    }

    // ── WHERE rendering ──────────────────────────────────────────────────────

    private void AppendWhere(StringBuilder sb, List<WhereClause> clauses,
        ExpressionToSqlVisitor visitor, ParameterBag parameters)
    {
        bool first = true;
        foreach (var clause in clauses)
        {
            var connector = first ? "    " : (clause.IsOr ? "    OR  " : "    AND ");
            first = false;

            switch (clause)
            {
                case TenantWhereClause tc:
                    // Use dynamic source so plan cache re-evaluates tenant on warm path
                    var tenantParam = parameters.AddDynamic(tc.TenantIdSource, "tenantId");
                    sb.AppendLine($"{connector}{tc.Alias}.{tc.ColumnName} = {tenantParam}");
                    break;

                case SoftDeleteWhereClause sd:
                    sb.AppendLine(sd.OnlyDeleted
                        ? $"{connector}{sd.Alias}.{sd.ColumnName} IS NOT NULL"
                        : $"{connector}{sd.Alias}.{sd.ColumnName} IS NULL");
                    break;

                case ExpressionWhereClause ew:
                    sb.AppendLine($"{connector}{visitor.Compile(ew.Expression)}");
                    break;

                case WhereNullClause wn:
                    var colNullSql = visitor.Compile(wn.Column);
                    sb.AppendLine($"{connector}{colNullSql} {(wn.IsNull ? "IS NULL" : "IS NOT NULL")}");
                    break;

                case WhereBetweenClause wb:
                    var betweenCol = visitor.Compile(wb.Column);
                    var loParam = parameters.Add(wb.Low);
                    var hiParam = parameters.Add(wb.High);
                    sb.AppendLine($"{connector}{betweenCol} BETWEEN {loParam} AND {hiParam}");
                    break;

                case WhereInClause wi:
                    var inCol = visitor.Compile(wi.Column);
                    var inVals = wi.Values.Select(v => parameters.Add(v)).ToList();
                    if (inVals.Any())
                        sb.AppendLine($"{connector}{inCol} IN ({string.Join(", ", inVals)})");
                    break;

                case WhereNotInClause wni:
                    var notInCol = visitor.Compile(wni.Column);
                    var notInVals = wni.Values.Select(v => parameters.Add(v)).ToList();
                    if (notInVals.Any())
                        sb.AppendLine($"{connector}{notInCol} NOT IN ({string.Join(", ", notInVals)})");
                    break;

                case WhereExistsClause wex:
                    var existsSql = CompileSubquery(wex.Subquery, parameters);
                    sb.AppendLine($"{connector}EXISTS (");
                    foreach (var line in existsSql.Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine("    " + line.TrimEnd());
                    sb.AppendLine(")");
                    break;

                case WhereInSubqueryClause wis:
                    var subCol = visitor.Compile(wis.Column);
                    var subSql = CompileSubquery(wis.Subquery, parameters);
                    var notKw = wis.IsNotIn ? "NOT IN" : "IN";
                    sb.AppendLine($"{connector}{subCol} {notKw} (");
                    foreach (var line in subSql.Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine("    " + line.TrimEnd());
                    sb.AppendLine(")");
                    break;

                case RawWhereClause rw:
                    var rawSql = rw.Sql;
                    for (int i = 0; i < rw.Args.Length; i++)
                    {
                        var p = parameters.Add(rw.Args[i]);
                        rawSql = rawSql.Replace("{" + i + "}", p);
                    }
                    sb.AppendLine($"{connector}{rawSql}");
                    break;
            }
        }
    }

    // ── CTE rendering ────────────────────────────────────────────────────────

    private void AppendCtes(StringBuilder sb, List<CteDescriptor> ctes, AliasRegistry aliases, ParameterBag parameters)
    {
        if (!ctes.Any()) return;
        var hasRecursive = ctes.Any(c => c.IsRecursive);
        var prefix = _dialect.Provider == DbProvider.Sqlite && hasRecursive ? "WITH RECURSIVE " : "WITH ";
        sb.Append(prefix);

        bool first = true;
        foreach (var cte in ctes)
        {
            if (!first) sb.AppendLine(",");
            sb.AppendLine($"{cte.Name} AS (");

            if (cte.Query is QueryBuilder<object> qb)
            {
                // Compile the inner CTE query body
                var innerSql = CompileCteBody(qb, aliases, parameters);
                foreach (var line in innerSql.Split('\n'))
                    sb.AppendLine("    " + line.TrimEnd());
            }

            // Recursive part: anchor UNION ALL recursive
            if (cte.IsRecursive && cte.RecursiveQuery is QueryBuilder<object> rqb)
            {
                sb.AppendLine("    UNION ALL");
                var recursiveSql = CompileCteBody(rqb, aliases, parameters);
                foreach (var line in recursiveSql.Split('\n'))
                    sb.AppendLine("    " + line.TrimEnd());
            }

            sb.Append(')');
            first = false;
        }
        sb.AppendLine();
    }

    private string CompileCteBody<T>(QueryBuilder<T> inner, AliasRegistry aliases, ParameterBag parameters)
        where T : class
    {
        var innerCompiler = new SqlCompiler(_registry, _dialect, _planCache);
        var result = innerCompiler.CompileFull(inner);
        // Merge parameters into outer bag (CTE params belong to same query)
        foreach (var (k, v) in result.Parameters)
            parameters.Add(v, k.TrimStart('@'));
        return result.Sql;
    }

    // Allow CompileFull to be called from outer context for CTE bodies
    internal (string Sql, System.Collections.Generic.IReadOnlyDictionary<string, object?> Parameters)
        CompileFullPublic<T>(QueryBuilder<T> builder) where T : class => CompileFull(builder);

    private string CompileSubquery(IQueryDescriptor query, ParameterBag outerParameters)
    {
        // Compile the subquery and merge its parameters into the outer bag
        if (query is not System.Reflection.IReflect) { } // type erasure workaround
        // Use reflection to call the generic CompileFull
        var queryType = query.GetType();
        var typeArg = queryType.GetGenericArguments().FirstOrDefault();
        if (typeArg == null) return "SELECT 1";

        var method = typeof(SqlCompiler)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .First(m => m.Name == "CompileFull" && m.IsGenericMethod)
            .MakeGenericMethod(typeArg);

        var result = ((string Sql, System.Collections.Generic.IReadOnlyDictionary<string, object?> Parameters))
            method.Invoke(this, [query])!;

        foreach (var (k, v) in result.Parameters)
            outerParameters.Add(v, k.TrimStart('@'));

        return result.Sql;
    }
}

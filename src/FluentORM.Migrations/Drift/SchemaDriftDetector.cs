using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Configuration;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Mapping;

namespace FluentORM.Migrations.Drift;

public enum DriftSeverity { Info, Warning, Error }

public sealed class DriftIssue
{
    public DriftSeverity Severity { get; init; }
    public string EntityName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? SuggestedFix { get; init; }
}

public sealed class DriftReport
{
    public IReadOnlyList<DriftIssue> Issues { get; init; } = [];
    public bool HasErrors => Issues.Any(i => i.Severity == DriftSeverity.Error);

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"FluentORM Schema Drift Detected — {Issues.Count} issue(s)");
        sb.AppendLine(new string('═', 60));
        foreach (var issue in Issues)
        {
            sb.AppendLine($"[{issue.Severity.ToString().ToUpper()}] {issue.EntityName}  →  {issue.Message}");
            if (issue.SuggestedFix != null)
            {
                sb.AppendLine("        Suggested fix:");
                sb.AppendLine($"            {issue.SuggestedFix}");
            }
        }
        sb.AppendLine(new string('═', 60));
        return sb.ToString();
    }
}

public sealed class SchemaDriftDetector
{
    private readonly IConnectionFactory _factory;
    private readonly ISqlDialect _dialect;
    private readonly EntityMapRegistry _registry;

    public SchemaDriftDetector(IConnectionFactory factory, ISqlDialect dialect, EntityMapRegistry registry)
    {
        _factory = factory;
        _dialect = dialect;
        _registry = registry;
    }

    public async Task<DriftReport> DetectAsync(CancellationToken ct = default)
    {
        var issues = new List<DriftIssue>();
        using var conn = await _factory.OpenAsync(ct);

        foreach (var descriptor in _registry.AllDescriptors)
        {
            var dbCols = await GetDbColumnsAsync(conn, descriptor.TableName, ct);
            if (dbCols == null)
            {
                issues.Add(new DriftIssue
                {
                    Severity = DriftSeverity.Error,
                    EntityName = descriptor.EntityType.Name,
                    Message = $"Table '{descriptor.TableName}' does not exist in the database.",
                    SuggestedFix = $"schema.CreateTable<{descriptor.EntityType.Name}>(t => {{ ... }});"
                });
                continue;
            }

            foreach (var col in descriptor.Columns)
            {
                if (!dbCols.TryGetValue(col.ColumnName.ToLower(), out var dbCol) &&
                    !dbCols.TryGetValue(col.PropertyName.ToLower(), out dbCol))
                {
                    // Column in entity but not in DB
                    // Check if there's a similar-named column (possible rename)
                    var similar = dbCols.Keys.FirstOrDefault(k =>
                        LevenshteinDistance(k, col.ColumnName.ToLower()) <= 3);

                    if (similar != null)
                    {
                        issues.Add(new DriftIssue
                        {
                            Severity = DriftSeverity.Error,
                            EntityName = descriptor.EntityType.Name,
                            Message = $"Column name mismatch on {descriptor.EntityType.Name}.{col.PropertyName}\n" +
                                      $"        C# maps to: \"{col.ColumnName}\"\n" +
                                      $"        DB has:     \"{similar}\"  (possible rename?)",
                            SuggestedFix = $"schema.RenameColumn<{descriptor.EntityType.Name}>(old: \"{similar}\", @new: \"{col.ColumnName}\");"
                        });
                    }
                    else
                    {
                        issues.Add(new DriftIssue
                        {
                            Severity = DriftSeverity.Error,
                            EntityName = descriptor.EntityType.Name,
                            Message = $"Column '{col.ColumnName}' exists in C# mapping but not in the database.",
                            SuggestedFix = $"schema.AddColumn<{descriptor.EntityType.Name}>(p => p.{col.PropertyName}).NotNull();"
                        });
                    }
                }
                else if (dbCol != null)
                {
                    // SQLite always reports INTEGER PRIMARY KEY as nullable in PRAGMA table_info
                    if (col.IsPrimaryKey) continue;

                    // Check nullability
                    var cSharpNotNull = col.IsNotNull;
                    var dbNotNull = dbCol.IsNotNull;
                    if (cSharpNotNull && !dbNotNull)
                    {
                        issues.Add(new DriftIssue
                        {
                            Severity = DriftSeverity.Error,
                            EntityName = descriptor.EntityType.Name,
                            Message = $"Nullability mismatch on {descriptor.EntityType.Name}.{col.PropertyName}\n" +
                                      $"        C# maps:    {col.PropertyName}  →  NOT NULL\n" +
                                      $"        DB has:     {col.ColumnName}  →  NULL",
                            SuggestedFix = $"schema.AlterColumn<{descriptor.EntityType.Name}>(p => p.{col.PropertyName}).NotNull();"
                        });
                    }
                    else if (!cSharpNotNull && dbNotNull)
                    {
                        issues.Add(new DriftIssue
                        {
                            Severity = DriftSeverity.Warning,
                            EntityName = descriptor.EntityType.Name,
                            Message = $"Nullable in C# but NOT NULL in DB on {descriptor.EntityType.Name}.{col.PropertyName}. " +
                                      "DB is stricter than C# — safe but surprising."
                        });
                    }
                }
            }

            // Warn about orphan columns
            var entityColNames = descriptor.Columns
                .SelectMany(c => new[] { c.ColumnName.ToLower(), c.PropertyName.ToLower() })
                .ToHashSet();

            foreach (var dbColName in dbCols.Keys)
            {
                if (!entityColNames.Contains(dbColName))
                {
                    issues.Add(new DriftIssue
                    {
                        Severity = DriftSeverity.Warning,
                        EntityName = descriptor.EntityType.Name,
                        Message = $"Column '{dbColName}' exists in database but has no C# property mapping. " +
                                  "Safe to ignore if intentional. Add [Ignore] property to silence."
                    });
                }
            }
        }

        return new DriftReport { Issues = issues };
    }

    public async Task ValidateAsync(DriftMode mode, CancellationToken ct = default)
    {
        if (mode == DriftMode.Disabled) return;
        var report = await DetectAsync(ct);
        if (!report.HasErrors) return;

        if (mode == DriftMode.Throw)
            throw new SchemaDriftException(report.ToString());
    }

    private async Task<Dictionary<string, DbColumnInfo>?> GetDbColumnsAsync(
        IDbConnection conn, string tableName, CancellationToken ct)
    {
        string sql;
        if (_dialect.Provider == DbProvider.SqlServer)
        {
            sql = $@"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = '{tableName}'";
        }
        else
        {
            sql = $"PRAGMA table_info({tableName})";
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        IDataReader reader;
        if (cmd is DbCommand dbCmd) reader = await dbCmd.ExecuteReaderAsync(ct);
        else reader = cmd.ExecuteReader();

        var result = new Dictionary<string, DbColumnInfo>(StringComparer.OrdinalIgnoreCase);
        using (reader)
        {
            if (!reader.Read()) return null; // Table doesn't exist
            do
            {
                string name, isNullable;
                if (_dialect.Provider == DbProvider.SqlServer)
                {
                    name = reader["COLUMN_NAME"].ToString()!;
                    isNullable = reader["IS_NULLABLE"].ToString()!;
                    result[name.ToLower()] = new DbColumnInfo
                    {
                        Name = name,
                        IsNotNull = isNullable?.Equals("NO", StringComparison.OrdinalIgnoreCase) ?? true
                    };
                }
                else
                {
                    name = reader["name"].ToString()!;
                    var notNull = Convert.ToInt32(reader["notnull"]) == 1;
                    result[name.ToLower()] = new DbColumnInfo { Name = name, IsNotNull = notNull };
                }
            } while (reader.Read());
        }
        return result;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1]
                    : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
        return dp[a.Length, b.Length];
    }

    private sealed class DbColumnInfo
    {
        public string Name { get; init; } = string.Empty;
        public bool IsNotNull { get; init; }
    }
}

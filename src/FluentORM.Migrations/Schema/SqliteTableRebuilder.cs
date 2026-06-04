using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;

namespace FluentORM.Migrations.Schema;

/// <summary>
/// Implements the SQLite table rebuild pattern required for DropColumn and AlterColumn.
/// The sequence is atomic within a transaction:
/// 1. CREATE TABLE new_name (desired schema)
/// 2. INSERT INTO new_name SELECT ... FROM old_table
/// 3. DROP TABLE old_table
/// 4. ALTER TABLE new_name RENAME TO old_table
/// 5. Recreate all indexes
/// 6. Run PRAGMA foreign_key_check
/// </summary>
public sealed class SqliteTableRebuilder
{
    public static async Task RebuildAsync(
        IDbConnection conn,
        IDbTransaction txn,
        string tableName,
        IReadOnlyList<ColumnDefinition> newColumns,
        IReadOnlyList<string> columnsToKeep,
        IReadOnlyList<IndexDefinition> indexes,
        CancellationToken ct = default)
    {
        var tempName = $"{tableName}_rebuild_{Guid.NewGuid():N}";

        // Step 1: Disable FK enforcement during rebuild
        await Exec(conn, txn, "PRAGMA foreign_keys = OFF;", ct);

        // Step 2: Create new table with desired schema
        var createSql = BuildCreateTable(tempName, newColumns);
        await Exec(conn, txn, createSql, ct);

        // Step 3: Copy data from old table (only columns that survive)
        var cols = string.Join(", ", columnsToKeep);
        await Exec(conn, txn, $"INSERT INTO {tempName} ({cols}) SELECT {cols} FROM {tableName};", ct);

        // Step 4: Drop old table
        await Exec(conn, txn, $"DROP TABLE {tableName};", ct);

        // Step 5: Rename new table to original name
        await Exec(conn, txn, $"ALTER TABLE {tempName} RENAME TO {tableName};", ct);

        // Step 6: Recreate indexes
        foreach (var idx in indexes)
        {
            var unique = idx.IsUnique ? "UNIQUE " : "";
            var cols2 = string.Join(", ", idx.Columns);
            await Exec(conn, txn, $"CREATE {unique}INDEX IF NOT EXISTS {idx.Name} ON {tableName} ({cols2});", ct);
        }

        // Step 7: Re-enable FK enforcement
        await Exec(conn, txn, "PRAGMA foreign_keys = ON;", ct);

        // Step 8: Validate foreign key integrity
        var violations = await CheckForeignKeys(conn, txn, ct);
        if (violations.Count > 0)
        {
            throw new ForeignKeyViolationException(
                $"Foreign key check failed after table rebuild on '{tableName}': " +
                string.Join(", ", violations.Take(3)));
        }
    }

    private static string BuildCreateTable(string tableName, IReadOnlyList<ColumnDefinition> columns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {tableName} (");
        var colDefs = columns.Select(c => "    " + RenderCol(c)).ToList();
        sb.AppendLine(string.Join(",\n", colDefs));
        sb.Append(");");
        return sb.ToString();
    }

    private static string RenderCol(ColumnDefinition col)
    {
        var sb = new StringBuilder();
        sb.Append($"{col.Name} ");
        if (col.IsAutoIncrement) sb.Append("INTEGER PRIMARY KEY AUTOINCREMENT");
        else if (col.IsPrimaryKey) sb.Append("INTEGER PRIMARY KEY");
        else if (col.IsRowVersion) sb.Append("INTEGER NOT NULL DEFAULT 1");
        else
        {
            sb.Append(col.SqlType);
            if (!col.IsNullable) sb.Append(" NOT NULL");
            if (col.HasDefault && col.Default != null)
                sb.Append($" DEFAULT {FormatDefault(col.Default)}");
        }
        return sb.ToString();
    }

    private static string FormatDefault(object value) => value switch
    {
        string s => $"'{s}'",
        bool b => b ? "1" : "0",
        _ => value.ToString()!
    };

    private static async Task<IReadOnlyList<string>> CheckForeignKeys(
        IDbConnection conn, IDbTransaction txn, CancellationToken ct)
    {
        var violations = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        cmd.Transaction = txn;
        IDataReader reader;
        if (cmd is DbCommand dbCmd) reader = await dbCmd.ExecuteReaderAsync(ct);
        else reader = cmd.ExecuteReader();
        using (reader)
        {
            while (reader.Read())
                violations.Add($"{reader[0]}.{reader[3]}");
        }
        return violations;
    }

    private static async Task Exec(IDbConnection conn, IDbTransaction txn, string sql, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = txn;
        if (cmd is DbCommand dbCmd) await dbCmd.ExecuteNonQueryAsync(ct);
        else cmd.ExecuteNonQuery();
    }
}

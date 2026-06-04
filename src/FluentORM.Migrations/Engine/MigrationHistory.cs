using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Migrations.Engine;

public sealed class MigrationHistoryEntry
{
    public long Version { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public string AppliedBy { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

public sealed class MigrationHistory
{
    private readonly IConnectionFactory _factory;
    private readonly ISqlDialect _dialect;

    private const string TableName = "__FluentMigrations";

    public MigrationHistory(IConnectionFactory factory, ISqlDialect dialect)
    {
        _factory = factory;
        _dialect = dialect;
    }

    public async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var sql = _dialect.Provider == DbProvider.SqlServer
            ? $@"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{TableName}')
BEGIN
    CREATE TABLE [{TableName}] (
        Version      BIGINT        NOT NULL PRIMARY KEY,
        Description  NVARCHAR(500) NOT NULL,
        AppliedAt    DATETIME2     NOT NULL,
        AppliedBy    NVARCHAR(200) NOT NULL,
        DurationMs   INT           NOT NULL,
        Checksum     NVARCHAR(64)  NOT NULL
    );
END"
            : $@"CREATE TABLE IF NOT EXISTS {TableName} (
    Version     INTEGER NOT NULL PRIMARY KEY,
    Description TEXT    NOT NULL,
    AppliedAt   TEXT    NOT NULL,
    AppliedBy   TEXT    NOT NULL,
    DurationMs  INTEGER NOT NULL,
    Checksum    TEXT    NOT NULL
);";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (cmd is DbCommand dbCmd) await dbCmd.ExecuteNonQueryAsync(ct);
        else cmd.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<MigrationHistoryEntry>> GetAppliedAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var sql = _dialect.Provider == DbProvider.SqlServer
            ? $"SELECT Version, Description, AppliedAt, AppliedBy, DurationMs, Checksum FROM [{TableName}] ORDER BY Version"
            : $"SELECT Version, Description, AppliedAt, AppliedBy, DurationMs, Checksum FROM {TableName} ORDER BY Version";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        IDataReader reader;
        if (cmd is DbCommand dbCmd) reader = await dbCmd.ExecuteReaderAsync(ct);
        else reader = cmd.ExecuteReader();

        using (reader)
        {
            var results = new List<MigrationHistoryEntry>();
            while (reader.Read())
            {
                results.Add(new MigrationHistoryEntry
                {
                    Version = Convert.ToInt64(reader["Version"]),
                    Description = reader["Description"].ToString()!,
                    AppliedAt = DateTime.Parse(reader["AppliedAt"].ToString()!),
                    AppliedBy = reader["AppliedBy"].ToString()!,
                    DurationMs = Convert.ToInt32(reader["DurationMs"]),
                    Checksum = reader["Checksum"].ToString()!
                });
            }
            return results;
        }
    }

    public async Task RecordAsync(MigrationHistoryEntry entry, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var sql = _dialect.Provider == DbProvider.SqlServer
            ? $"INSERT INTO [{TableName}] (Version, Description, AppliedAt, AppliedBy, DurationMs, Checksum) VALUES (@v, @d, @a, @by, @ms, @cs)"
            : $"INSERT INTO {TableName} (Version, Description, AppliedAt, AppliedBy, DurationMs, Checksum) VALUES (@v, @d, @a, @by, @ms, @cs)";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParam(cmd, "@v", entry.Version);
        AddParam(cmd, "@d", entry.Description);
        AddParam(cmd, "@a", entry.AppliedAt.ToString("O"));
        AddParam(cmd, "@by", entry.AppliedBy);
        AddParam(cmd, "@ms", entry.DurationMs);
        AddParam(cmd, "@cs", entry.Checksum);

        if (cmd is DbCommand dbCmd) await dbCmd.ExecuteNonQueryAsync(ct);
        else cmd.ExecuteNonQuery();
    }

    public async Task RemoveAsync(long version, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var sql = _dialect.Provider == DbProvider.SqlServer
            ? $"DELETE FROM [{TableName}] WHERE Version = @v"
            : $"DELETE FROM {TableName} WHERE Version = @v";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParam(cmd, "@v", version);
        if (cmd is DbCommand dbCmd) await dbCmd.ExecuteNonQueryAsync(ct);
        else cmd.ExecuteNonQuery();
    }

    public static string ComputeChecksum(string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLower();
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

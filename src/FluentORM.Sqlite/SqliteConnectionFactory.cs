using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace FluentORM.Sqlite;

public sealed class SqliteConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
        => _connectionString = connectionString;

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnableForeignKeys(conn, ct);
        return conn;
    }

    private static async Task EnableForeignKeys(SqliteConnection conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

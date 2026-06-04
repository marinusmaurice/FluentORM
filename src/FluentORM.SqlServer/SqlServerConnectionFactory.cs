using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using Microsoft.Data.SqlClient;

namespace FluentORM.SqlServer;

public sealed class SqlServerConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(string connectionString)
        => _connectionString = connectionString;

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace FluentORM.Core.Abstractions;

public interface IConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken ct = default);
}

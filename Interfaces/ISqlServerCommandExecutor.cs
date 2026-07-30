using System.Data.Common;

namespace raft_backend.Interfaces;

public interface ISqlServerCommandExecutor
{
    Task<int> ExecuteNonQueryAsync(
        string commandText,
        Action<DbCommand>? configureCommand,
        CancellationToken cancellationToken = default);

    Task<List<T>> QueryAsync<T>(
        string commandText,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);
}

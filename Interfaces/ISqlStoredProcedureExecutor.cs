using System.Data.Common;

namespace raft_backend.Interfaces;

public interface ISqlStoredProcedureExecutor
{
    Task<List<T>> QueryAsync<T>(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        CancellationToken cancellationToken = default);
}

using System.Data.Common;

namespace raft_backend.Interfaces;

// Thin ADO.NET executor against the MySQL provisioning server — mirrors
// ISqlStoredProcedureExecutor's connection-lifecycle shape, but MySQL provisioning is plain
// parameterized DDL/DCL against infrastructure we manage, not a stored-procedure catalog.
public interface IMySqlCommandExecutor
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

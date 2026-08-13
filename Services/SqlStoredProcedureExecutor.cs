using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using raft_backend.Database;

namespace raft_backend.Services;

public class SqlStoredProcedureExecutor : ISqlStoredProcedureExecutor
{
    private readonly RaftDbContext _context;
    private readonly ILogger<SqlStoredProcedureExecutor> _logger;

    public SqlStoredProcedureExecutor(RaftDbContext context, ILogger<SqlStoredProcedureExecutor> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<T>> QueryAsync<T>(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithReaderAsync(storedProcedureName, configureCommand, async reader =>
        {
            var items = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(map(reader));
            }

            return items;
        }, cancellationToken);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithReaderAsync(storedProcedureName, configureCommand, async reader =>
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return default;
            }

            var item = map(reader);
            if (await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"Stored procedure '{storedProcedureName}' returned more than one row.");
            }

            return item;
        }, cancellationToken);
    }

    public async Task<int> ExecuteAsync(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            using var command = connection.CreateCommand();
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;
            configureCommand?.Invoke(command);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing stored procedure.");
            throw;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<TResult> ExecuteWithReaderAsync<TResult>(
        string storedProcedureName,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            using var command = connection.CreateCommand();
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;
            configureCommand?.Invoke(command);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await action(reader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying stored procedure.");
            throw;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}

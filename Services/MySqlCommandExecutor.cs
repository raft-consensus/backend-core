using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using raft_backend.Database;

namespace raft_backend.Services;

public class MySqlCommandExecutor : IMySqlCommandExecutor
{
    private readonly MySqlDbContext _context;
    private readonly ILogger<MySqlCommandExecutor> _logger;

    public MySqlCommandExecutor(MySqlDbContext context, ILogger<MySqlCommandExecutor> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> ExecuteNonQueryAsync(
        string commandText,
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
            command.CommandText = commandText;
            configureCommand?.Invoke(command);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MySQL command: {CommandText}", commandText);
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

    public async Task<List<T>> QueryAsync<T>(
        string commandText,
        Action<DbCommand>? configureCommand,
        Func<DbDataReader, T> map,
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
            command.CommandText = commandText;
            configureCommand?.Invoke(command);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(map(reader));
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying MySQL command: {CommandText}", commandText);
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

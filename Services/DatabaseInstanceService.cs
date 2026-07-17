using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class DatabaseInstanceService : IDatabaseInstanceService
{
    private readonly ISqlStoredProcedureExecutor _executor;

    public DatabaseInstanceService(ISqlStoredProcedureExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<DatabaseInstanceReadDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _executor.QueryAsync(
            StoredProcedureNames.DatabaseInstances_GetAll,
            null,
            Map,
            cancellationToken);

        return items;
    }

    public Task<DatabaseInstanceReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DatabaseInstances_GetById,
            command => command.AddParameter("@Id", id),
            Map,
            cancellationToken);
    }

    public Task<DatabaseInstanceReadDto?> CreateAsync(DatabaseInstanceCreateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DatabaseInstances_Create,
            command =>
            {
                command.AddParameter("@UserId", dto.UserId);
                command.AddParameter("@Host", dto.Host);
                command.AddParameter("@Port", dto.Port);
                command.AddParameter("@DatabaseName", dto.DatabaseName);
                command.AddParameter("@DatabaseUser", dto.DatabaseUser);
                command.AddParameter("@Engine", dto.Engine);
                command.AddParameter("@Status", dto.Status);
                command.AddParameter("@UsedSpaceBytes", dto.UsedSpaceBytes);
                command.AddParameter("@MaxSpaceBytes", dto.MaxSpaceBytes);
                command.AddParameter("@LastActivity", dto.LastActivity);
            },
            Map,
            cancellationToken);
    }

    public Task<DatabaseInstanceReadDto?> UpdateAsync(int id, DatabaseInstanceUpdateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DatabaseInstances_Update,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", dto.UserId);
                command.AddParameter("@Host", dto.Host);
                command.AddParameter("@Port", dto.Port);
                command.AddParameter("@DatabaseName", dto.DatabaseName);
                command.AddParameter("@DatabaseUser", dto.DatabaseUser);
                command.AddParameter("@Engine", dto.Engine);
                command.AddParameter("@Status", dto.Status);
                command.AddParameter("@UsedSpaceBytes", dto.UsedSpaceBytes);
                command.AddParameter("@MaxSpaceBytes", dto.MaxSpaceBytes);
                command.AddParameter("@LastActivity", dto.LastActivity);
            },
            Map,
            cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.DatabaseInstances_SoftDelete,
            command => command.AddParameter("@Id", id),
            cancellationToken);

        return rows > 0;
    }

    private static DatabaseInstanceReadDto Map(DbDataReader reader)
    {
        return new DatabaseInstanceReadDto
        {
            Id = reader.GetInt32Value("Id"),
            UserId = reader.GetInt32Value("UserId"),
            Host = reader.GetStringOrEmpty("Host"),
            Port = reader.GetInt32Value("Port"),
            DatabaseName = reader.GetStringOrEmpty("DatabaseName"),
            DatabaseUser = reader.GetStringOrEmpty("DatabaseUser"),
            Engine = reader.GetStringOrEmpty("Engine"),
            Status = reader.GetStringOrEmpty("Status"),
            UsedSpaceBytes = reader.GetInt64Value("UsedSpaceBytes"),
            MaxSpaceBytes = reader.GetInt64Value("MaxSpaceBytes"),
            LastActivity = reader.GetNullableDateTime("LastActivity"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            DeletedAt = reader.GetNullableDateTime("DeletedAt")
        };
    }
}

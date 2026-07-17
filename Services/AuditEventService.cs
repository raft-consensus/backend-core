using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class AuditEventService : IAuditEventService
{
    private readonly ISqlStoredProcedureExecutor _executor;

    public AuditEventService(ISqlStoredProcedureExecutor executor)
    {
        _executor = executor;
    }

    public Task<IReadOnlyList<AuditEventReadDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.AuditEvents_GetAll,
            null,
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<AuditEventReadDto>)task.Result, cancellationToken);
    }

    public Task<AuditEventReadDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AuditEvents_GetById,
            command => command.AddParameter("@Id", id),
            Map,
            cancellationToken);
    }

    public Task<AuditEventReadDto?> CreateAsync(AuditEventCreateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AuditEvents_Create,
            command =>
            {
                command.AddParameter("@UserId", dto.UserId);
                command.AddParameter("@EventType", dto.EventType);
                command.AddParameter("@Description", dto.Description);
                command.AddParameter("@IpAddress", dto.IpAddress);
                command.AddParameter("@AdditionalData", dto.AdditionalData);
            },
            Map,
            cancellationToken);
    }

    public Task<AuditEventReadDto?> UpdateAsync(long id, AuditEventUpdateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AuditEvents_Update,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", dto.UserId);
                command.AddParameter("@EventType", dto.EventType);
                command.AddParameter("@Description", dto.Description);
                command.AddParameter("@IpAddress", dto.IpAddress);
                command.AddParameter("@AdditionalData", dto.AdditionalData);
            },
            Map,
            cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.AuditEvents_SoftDelete,
            command => command.AddParameter("@Id", id),
            cancellationToken);

        return rows > 0;
    }

    private static AuditEventReadDto Map(DbDataReader reader)
    {
        return new AuditEventReadDto
        {
            Id = reader.GetInt64Value("Id"),
            UserId = reader.GetNullableInt32("UserId"),
            EventType = reader.GetStringOrEmpty("EventType"),
            Description = reader.GetStringOrEmpty("Description"),
            IpAddress = reader.GetNullableString("IpAddress"),
            AdditionalData = reader.GetNullableString("AdditionalData"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            DeletedAt = reader.GetNullableDateTime("DeletedAt")
        };
    }
}

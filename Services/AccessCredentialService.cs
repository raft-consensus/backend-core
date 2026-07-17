using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class AccessCredentialService : IAccessCredentialService
{
    private readonly ISqlStoredProcedureExecutor _executor;

    public AccessCredentialService(ISqlStoredProcedureExecutor executor)
    {
        _executor = executor;
    }

    public Task<IReadOnlyList<AccessCredentialReadDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.AccessCredentials_GetAll,
            null,
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<AccessCredentialReadDto>)task.Result, cancellationToken);
    }

    public Task<AccessCredentialReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AccessCredentials_GetById,
            command => command.AddParameter("@Id", id),
            Map,
            cancellationToken);
    }

    public Task<AccessCredentialReadDto?> GetByDatabaseInstanceIdAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AccessCredentials_GetByDatabaseInstanceId,
            command => command.AddParameter("@DatabaseInstanceId", databaseInstanceId),
            Map,
            cancellationToken);
    }

    public Task<AccessCredentialReadDto?> CreateAsync(AccessCredentialCreateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AccessCredentials_Create,
            command =>
            {
                command.AddParameter("@DatabaseInstanceId", dto.DatabaseInstanceId);
                command.AddParameter("@EncryptedPassword", dto.EncryptedPassword);
            },
            Map,
            cancellationToken);
    }

    public Task<AccessCredentialReadDto?> UpdateAsync(int id, AccessCredentialUpdateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AccessCredentials_Update,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@EncryptedPassword", dto.EncryptedPassword);
            },
            Map,
            cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.AccessCredentials_SoftDelete,
            command => command.AddParameter("@Id", id),
            cancellationToken);

        return rows > 0;
    }

    private static AccessCredentialReadDto Map(DbDataReader reader)
    {
        return new AccessCredentialReadDto
        {
            Id = reader.GetInt32Value("Id"),
            DatabaseInstanceId = reader.GetInt32Value("DatabaseInstanceId"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            DeletedAt = reader.GetNullableDateTime("DeletedAt")
        };
    }
}

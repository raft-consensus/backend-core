using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Models;

namespace raft_backend.Services;

public class UserService : IUserService
{
    private readonly ISqlStoredProcedureExecutor _executor;

    public UserService(ISqlStoredProcedureExecutor executor)
    {
        _executor = executor;
    }

    public Task<IReadOnlyList<UserReadDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.Users_GetAll,
            null,
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<UserReadDto>)task.Result, cancellationToken);
    }

    public Task<UserReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_GetById,
            command => command.AddParameter("@Id", id),
            Map,
            cancellationToken);
    }

    public Task<UserReadDto?> CreateAsync(UserCreateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_Create,
            command =>
            {
                command.AddParameter("@Name", dto.Name);
                command.AddParameter("@Email", dto.Email);
                command.AddParameter("@Organization", dto.Organization);
                command.AddParameter("@AvatarUrl", dto.AvatarUrl);
                command.AddParameter("@Provider", dto.Provider);
                command.AddParameter("@ProviderUserId", dto.ProviderUserId);
                command.AddParameter("@LastLogin", dto.LastLogin);
            },
            Map,
            cancellationToken);
    }

    public Task<UserReadDto?> UpdateAsync(int id, UserUpdateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_Update,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@Name", dto.Name);
                command.AddParameter("@Email", dto.Email);
                command.AddParameter("@Organization", dto.Organization);
                command.AddParameter("@AvatarUrl", dto.AvatarUrl);
                command.AddParameter("@Provider", dto.Provider);
                command.AddParameter("@ProviderUserId", dto.ProviderUserId);
                command.AddParameter("@LastLogin", dto.LastLogin);
            },
            Map,
            cancellationToken);
    }

    public Task<UserReadDto?> UpdateSelfAsync(int userId, UserProfileUpdateDto dto, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_UpdateSelf,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@Name", dto.Name);
                command.AddParameter("@Organization", dto.Organization);
                command.AddParameter("@Phone", dto.Phone);
                command.AddParameter("@Gender", dto.Gender);
                command.AddParameter("@BirthDate", dto.BirthDate);
                command.AddParameter("@Country", dto.Country);
            },
            Map,
            cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.Users_SoftDelete,
            command => command.AddParameter("@Id", id),
            cancellationToken);

        return rows > 0;
    }

    private static UserReadDto Map(DbDataReader reader)
    {
        return new UserReadDto
        {
            Id = reader.GetInt32Value("Id"),
            Name = reader.GetStringOrEmpty("Name"),
            Email = reader.GetStringOrEmpty("Email"),
            Organization = reader.GetNullableString("Organization"),
            Phone = reader.GetNullableString("Phone"),
            Gender = reader.GetNullableString("Gender"),
            BirthDate = reader.GetNullableDateTime("BirthDate"),
            Country = reader.GetNullableString("Country"),
            AvatarUrl = reader.GetNullableString("AvatarUrl"),
            Provider = reader.GetStringOrEmpty("Provider"),
            ProviderUserId = reader.GetStringOrEmpty("ProviderUserId"),
            Role = reader.GetStringOrEmpty("Role"),
            HasLocalPassword = reader.GetBooleanValue("HasLocalPassword"),
            PasswordChangeRequired = reader.GetBooleanValue("PasswordChangeRequired"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            DeletedAt = reader.GetNullableDateTime("DeletedAt"),
            LastLogin = reader.GetNullableDateTime("LastLogin")
        };
    }
}

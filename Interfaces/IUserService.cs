using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserReadDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<UserReadDto?> CreateAsync(UserCreateDto dto, CancellationToken cancellationToken = default);
    Task<UserReadDto?> UpdateAsync(int id, UserUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}

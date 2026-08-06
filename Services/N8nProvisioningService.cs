using System.Data.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class N8nProvisioningService : IN8nProvisioningService
{
    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly IUserService _userService;
    private readonly IAuditEventService _auditEventService;
    private readonly N8nProvisioningOptions _options;
    private readonly ILogger<N8nProvisioningService> _logger;

    public N8nProvisioningService(
        ISqlStoredProcedureExecutor executor,
        IUserService userService,
        IAuditEventService auditEventService,
        IOptions<N8nProvisioningOptions> options,
        ILogger<N8nProvisioningService> logger)
    {
        _executor = executor;
        _userService = userService;
        _auditEventService = auditEventService;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<N8nAccountReadDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.N8nAccounts_GetAll,
            null,
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<N8nAccountReadDto>)task.Result, cancellationToken);
    }

    public Task<IReadOnlyList<N8nAccountReadDto>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.N8nAccounts_GetAllByUserId,
            command => command.AddParameter("@UserId", userId),
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<N8nAccountReadDto>)task.Result, cancellationToken);
    }

    public Task<N8nAccountReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.N8nAccounts_GetById,
            command => command.AddParameter("@Id", id),
            Map,
            cancellationToken);
    }

    public Task<N8nAccountReadDto?> GetActiveByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.N8nAccounts_GetActiveByUserId,
            command => command.AddParameter("@UserId", userId),
            Map,
            cancellationToken);
    }

    public async Task<N8nProvisioningResultDto?> ProvisionAsync(int userId, CancellationToken cancellationToken = default)
    {
        var existing = await GetActiveByUserIdAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return new N8nProvisioningResultDto
            {
                Created = false,
                Account = existing
            };
        }

        var user = await _userService.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} was not found.");

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException($"User {userId} does not have a valid email address.");
        }

        var externalUserRef = user.Id.ToString(CultureInfo.InvariantCulture);

        var pending = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.N8nAccounts_Create,
            command =>
            {
                command.AddParameter("@UserId", user.Id);
                command.AddParameter("@ExternalUserRef", externalUserRef);
                command.AddParameter("@Email", user.Email);
                command.AddParameter("@AccountId", DBNull.Value);
            },
            Map,
            cancellationToken);

        if (pending is null)
        {
            var current = await GetActiveByUserIdAsync(userId, cancellationToken);
            if (current is not null)
        {
            return new N8nProvisioningResultDto
            {
                Created = false,
                Account = current
            };
        }

            throw new InvalidOperationException("The N8N provisioning record could not be created.");
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "N8nProvisioningStarted",
            Description = $"N8N provisioning started for user {userId}.",
            AdditionalData = $"{{\"externalUserRef\":\"{externalUserRef}\"}}"
        }, cancellationToken);

        N8nExternalProvisionResponseDto remoteResponse;
        try
        {
            remoteResponse = await ProvisionRemoteAccountAsync(externalUserRef, user.Email, cancellationToken);
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(userId, pending.Id, ex.Message, cancellationToken);
            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = userId,
                EventType = "N8nProvisioningFailed",
                Description = $"N8N provisioning failed for user {userId}.",
                AdditionalData = SafeJson(new { error = ex.Message, externalUserRef })
            }, cancellationToken);

            _logger.LogWarning(ex, "N8N provisioning failed for user {UserId}", userId);
            return null;
        }

        var provisioned = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.N8nAccounts_MarkProvisioned,
            command =>
            {
                command.AddParameter("@Id", pending.Id);
                command.AddParameter("@UserId", userId);
                command.AddParameter("@AccountId", remoteResponse.AccountId);
            },
            Map,
            cancellationToken);

        if (provisioned is null)
        {
            var error = "The N8N account was created remotely but the local record could not be updated.";
            await MarkFailedAsync(userId, pending.Id, error, cancellationToken);
            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = userId,
                EventType = "N8nProvisioningFailed",
                Description = $"N8N provisioning completed remotely but failed locally for user {userId}.",
                AdditionalData = SafeJson(new { accountId = remoteResponse.AccountId, externalUserRef })
            }, cancellationToken);

            _logger.LogError("N8N remote provisioning succeeded but local update failed for user {UserId}", userId);
            return null;
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "N8nProvisioned",
            Description = $"N8N account provisioned for user {userId}.",
            AdditionalData = SafeJson(new { accountId = remoteResponse.AccountId, status = remoteResponse.Status, externalUserRef })
        }, cancellationToken);

        return new N8nProvisioningResultDto
        {
            Created = true,
            Account = provisioned,
            AccessType = remoteResponse.AccessType,
            Credential = remoteResponse.Credential
        };
    }

    public async Task<bool> RevokeAsync(int id, CancellationToken cancellationToken = default)
    {
        var current = await GetByIdAsync(id, cancellationToken);
        if (current is null || string.Equals(current.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.N8nAccounts_Revoke,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", current.UserId);
            },
            cancellationToken);

        if (rows <= 0)
        {
            return false;
        }

        return true;
    }

    private async Task<N8nExternalProvisionResponseDto> ProvisionRemoteAccountAsync(string externalUserRef, string email, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)
        };

        client.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey);

        var payload = new N8nExternalProvisionRequestDto
        {
            ExternalUserRef = externalUserRef,
            Email = email
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildProvisionUrl())
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = string.IsNullOrWhiteSpace(responseBody)
                ? $"N8N provisioning returned HTTP {(int)response.StatusCode}."
                : $"N8N provisioning returned HTTP {(int)response.StatusCode}: {responseBody}";

            throw new InvalidOperationException(Sanitize(message));
        }

        var remote = JsonSerializer.Deserialize<N8nExternalProvisionResponseDto>(
            responseBody,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (remote is null || string.IsNullOrWhiteSpace(remote.AccountId))
        {
            throw new InvalidOperationException("N8N provisioning returned an invalid response.");
        }

        return remote;
    }

    private async Task MarkFailedAsync(int userId, int id, string errorMessage, CancellationToken cancellationToken)
    {
        await _executor.ExecuteAsync(
            StoredProcedureNames.N8nAccounts_MarkFailed,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", userId);
                command.AddParameter("@LastErrorMessage", Truncate(errorMessage, 4000));
            },
            cancellationToken);
    }

    private static N8nAccountReadDto Map(DbDataReader reader)
    {
        return new N8nAccountReadDto
        {
            Id = reader.GetInt32Value("Id"),
            UserId = reader.GetInt32Value("UserId"),
            ExternalUserRef = reader.GetStringOrEmpty("ExternalUserRef"),
            Email = reader.GetStringOrEmpty("Email"),
            AccountId = reader.GetNullableString("AccountId"),
            Status = reader.GetStringOrEmpty("Status"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            ProvisionedAt = reader.GetNullableDateTime("ProvisionedAt"),
            RevokedAt = reader.GetNullableDateTime("RevokedAt"),
            LastSyncedAt = reader.GetNullableDateTime("LastSyncedAt"),
            LastErrorMessage = reader.GetNullableString("LastErrorMessage")
        };
    }

    private string BuildProvisionUrl()
    {
        var baseUrl = _options.BaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(new Uri(baseUrl), "n8n/external/provision").ToString();
    }

    private static string Sanitize(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        var sanitized = Sanitize(value);
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static string SafeJson(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    private sealed class N8nExternalProvisionRequestDto
    {
        [JsonPropertyName("external_user_ref")]
        public string ExternalUserRef { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    private sealed class N8nExternalProvisionResponseDto
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("access_type")]
        public string? AccessType { get; set; }

        [JsonPropertyName("credential")]
        public string? Credential { get; set; }
    }
}

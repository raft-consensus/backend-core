using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public partial class DnsProvisioningService : IDnsProvisioningService
{
    private const string CloudflareBaseUrl = "https://api.cloudflare.com/client/v4/";

    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly IAuditEventService _auditEventService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DnsProvisioningOptions _options;
    private readonly ILogger<DnsProvisioningService> _logger;

    public DnsProvisioningService(
        ISqlStoredProcedureExecutor executor,
        IAuditEventService auditEventService,
        IHttpClientFactory httpClientFactory,
        IOptions<DnsProvisioningOptions> options,
        ILogger<DnsProvisioningService> logger)
    {
        _executor = executor;
        _auditEventService = auditEventService;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_options.ZoneId) &&
        !string.IsNullOrWhiteSpace(_options.ZoneName) &&
        !string.IsNullOrWhiteSpace(_options.CellSubdomain) &&
        !string.IsNullOrWhiteSpace(_options.ApiToken);

    public int MaxRecordsPerUser => _options.MaxRecordsPerUser;

    public Task<IReadOnlyList<DnsRecordReadDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.DnsRecords_GetAll,
            null,
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<DnsRecordReadDto>)task.Result, cancellationToken);
    }

    public Task<IReadOnlyList<DnsRecordReadDto>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.DnsRecords_GetAllByUserId,
            command => command.AddParameter("@UserId", userId),
            Map,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<DnsRecordReadDto>)task.Result, cancellationToken);
    }

    public Task<DnsRecordReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DnsRecords_GetById,
            command => command.AddParameter("@Id", id),
            Map,
            cancellationToken);
    }

    public Task<DnsRecordReadDto?> GetByIdForUserAsync(int userId, int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DnsRecords_GetByIdAndUserId,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", userId);
            },
            Map,
            cancellationToken);
    }

    public Task<DnsRecordReadDto?> GetActiveByUserIdAndFqdnAsync(int userId, string fqdn, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DnsRecords_GetActiveByUserIdAndFqdn,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@Fqdn", fqdn);
            },
            Map,
            cancellationToken);
    }

    public async Task<DnsProvisioningResultDto?> ProvisionAsync(int userId, DnsRecordCreateDto dto, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("DNS provisioning is not available.");
        }

        var label = NormalizeLabel(dto.Label);
        var recordName = BuildRecordName(label);
        var fqdn = BuildFqdn(recordName);
        var content = string.IsNullOrWhiteSpace(dto.Content)
            ? _options.DefaultContent.Trim()
            : dto.Content.Trim();
        var ttl = dto.RecordTtl.GetValueOrDefault(_options.RecordTtl);
        var proxied = dto.Proxied.GetValueOrDefault(_options.Proxied);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("DNS content is required.");
        }

        if (!IsValidContent(content))
        {
            throw new InvalidOperationException("DNS content must be a valid IP address for A records.");
        }

        var existing = await GetActiveByUserIdAndFqdnAsync(userId, fqdn, cancellationToken);
        if (existing is not null)
        {
            return new DnsProvisioningResultDto
            {
                Created = false,
                Record = existing
            };
        }

        var maxRecords = await GetNonRevokedCountAsync(userId, cancellationToken);
        if (maxRecords >= MaxRecordsPerUser)
        {
            throw new InvalidOperationException($"You already have {maxRecords} DNS record(s). The maximum allowed per account is {MaxRecordsPerUser}.");
        }

        var pending = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DnsRecords_Create,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@Label", label);
                command.AddParameter("@RecordName", recordName);
                command.AddParameter("@Fqdn", fqdn);
                command.AddParameter("@RecordType", "A");
                command.AddParameter("@Content", content);
                command.AddParameter("@RecordTtl", ttl);
                command.AddParameter("@Proxied", proxied);
                command.AddParameter("@CloudflareZoneId", _options.ZoneId);
            },
            Map,
            cancellationToken);

        if (pending is null)
        {
            var current = await GetActiveByUserIdAndFqdnAsync(userId, fqdn, cancellationToken);
            if (current is not null)
            {
                return new DnsProvisioningResultDto
                {
                    Created = false,
                    Record = current
                };
            }

            throw new InvalidOperationException("The DNS provisioning record could not be created.");
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "DnsProvisioningStarted",
            Description = $"DNS provisioning started for {fqdn}.",
            AdditionalData = SafeJson(new { fqdn, label, content, ttl, proxied })
        }, cancellationToken);

        CloudflareDnsRecord? remoteRecord;
        try
        {
            remoteRecord = await CreateRemoteRecordAsync(recordName, content, ttl, proxied, cancellationToken);
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(pending.Id, ex.Message, cancellationToken);
            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = userId,
                EventType = "DnsProvisioningFailed",
                Description = $"DNS provisioning failed for {fqdn}.",
                AdditionalData = SafeJson(new { fqdn, error = ex.Message })
            }, cancellationToken);

            _logger.LogWarning(ex, "DNS provisioning failed for {Fqdn}", fqdn);
            return null;
        }

        var provisioned = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DnsRecords_MarkProvisioned,
            command =>
            {
                command.AddParameter("@Id", pending.Id);
                command.AddParameter("@CloudflareRecordId", remoteRecord.Id);
                command.AddParameter("@CloudflareZoneId", _options.ZoneId);
            },
            Map,
            cancellationToken);

        if (provisioned is null)
        {
            var cleanupError = "The DNS record was created remotely but the local record could not be updated.";
            try
            {
                await DeleteRemoteRecordAsync(remoteRecord.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                cleanupError = $"{cleanupError} Cleanup failed: {ex.Message}";
                _logger.LogError(ex, "Remote DNS cleanup failed for {RemoteRecordId}", remoteRecord.Id);
            }

            await MarkFailedAsync(pending.Id, cleanupError, cancellationToken);
            throw new InvalidOperationException(cleanupError);
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "DnsProvisioned",
            Description = $"DNS record {fqdn} was provisioned successfully.",
            AdditionalData = SafeJson(new { remoteRecordId = remoteRecord.Id, fqdn })
        }, cancellationToken);

        return new DnsProvisioningResultDto
        {
            Created = true,
            Record = provisioned
        };
    }

    public Task<bool> RevokeAsync(int id, CancellationToken cancellationToken = default)
    {
        return RevokeInternalAsync(null, id, cancellationToken);
    }

    public Task<bool> RevokeAsync(int userId, int id, CancellationToken cancellationToken = default)
    {
        return RevokeInternalAsync(userId, id, cancellationToken);
    }

    private async Task<bool> RevokeInternalAsync(int? userId, int id, CancellationToken cancellationToken)
    {
        var record = userId is null
            ? await GetByIdAsync(id, cancellationToken)
            : await GetByIdForUserAsync(userId.Value, id, cancellationToken);

        if (record is null || string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(record.CloudflareRecordId))
        {
            try
            {
                await DeleteRemoteRecordAsync(record.CloudflareRecordId, cancellationToken);
            }
            catch (Exception ex)
            {
                await MarkFailedAsync(record.Id, ex.Message, cancellationToken);
                _logger.LogWarning(ex, "Failed to revoke remote DNS record {DnsRecordId}", record.Id);
                return false;
            }
        }

        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.DnsRecords_Revoke,
            command => command.AddParameter("@Id", record.Id),
            cancellationToken);

        return rows > 0;
    }

    private async Task<long> GetNonRevokedCountAsync(int userId, CancellationToken cancellationToken)
    {
        var items = await GetAllByUserIdAsync(userId, cancellationToken);
        return items.Count(record => !string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CloudflareDnsRecord> CreateRemoteRecordAsync(string recordName, string content, int ttl, bool proxied, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{CloudflareBaseUrl}zones/{_options.ZoneId}/dns_records");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiToken);
        request.Content = JsonContent.Create(new
        {
            type = "A",
            name = recordName,
            content,
            ttl,
            proxied
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildCloudflareError(body, response.StatusCode));
        }

        var parsed = JsonSerializer.Deserialize<CloudflareResponse<CloudflareDnsRecord>>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (parsed is null || !parsed.Success || parsed.Result is null || string.IsNullOrWhiteSpace(parsed.Result.Id))
        {
            throw new InvalidOperationException(BuildCloudflareError(body, response.StatusCode));
        }

        return parsed.Result;
    }

    private async Task DeleteRemoteRecordAsync(string recordId, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{CloudflareBaseUrl}zones/{_options.ZoneId}/dns_records/{recordId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiToken);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildCloudflareError(body, response.StatusCode));
        }
    }

    private async Task MarkFailedAsync(int id, string error, CancellationToken cancellationToken)
    {
        await _executor.ExecuteAsync(
            StoredProcedureNames.DnsRecords_MarkFailed,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@LastError", error);
            },
            cancellationToken);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("CloudflareDns");
        client.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
        return client;
    }

    private string BuildRecordName(string label)
    {
        return $"{label}.{_options.CellSubdomain.Trim().TrimEnd('.')}".Trim('.');
    }

    private string BuildFqdn(string recordName)
    {
        return $"{recordName}.{_options.ZoneName.Trim().TrimEnd('.')}".Trim('.');
    }

    private static string NormalizeLabel(string label)
    {
        var normalized = label.Trim().ToLowerInvariant();
        if (!LabelRegex().IsMatch(normalized))
        {
            throw new InvalidOperationException("DNS label must contain only lowercase letters, numbers, and hyphens, and cannot start or end with a hyphen.");
        }

        return normalized;
    }

    private static bool IsValidContent(string content)
    {
        return IPAddress.TryParse(content, out _);
    }

    private static string SafeJson(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static string BuildCloudflareError(string body, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<CloudflareResponse<object>>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed?.Errors is { Count: > 0 })
            {
                var details = string.Join("; ", parsed.Errors.Select(error => error.Message));
                return $"Cloudflare returned HTTP {(int)statusCode}: {details}";
            }
        }
        catch
        {
            // ignore parsing failures and fall through to raw body
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"Cloudflare returned HTTP {(int)statusCode}."
            : $"Cloudflare returned HTTP {(int)statusCode}: {body}";
    }

    private static DnsRecordReadDto Map(DbDataReader reader)
    {
        return new DnsRecordReadDto
        {
            Id = reader.GetInt32Value("Id"),
            UserId = reader.GetInt32Value("UserId"),
            Label = reader.GetStringOrEmpty("Label"),
            RecordName = reader.GetStringOrEmpty("RecordName"),
            Fqdn = reader.GetStringOrEmpty("Fqdn"),
            RecordType = reader.GetStringOrEmpty("RecordType"),
            Content = reader.GetStringOrEmpty("Content"),
            RecordTtl = reader.GetInt32Value("RecordTtl"),
            Proxied = reader.GetBooleanValue("Proxied"),
            CloudflareZoneId = reader.GetNullableString("CloudflareZoneId"),
            CloudflareRecordId = reader.GetNullableString("CloudflareRecordId"),
            Status = reader.GetStringOrEmpty("Status"),
            LastError = reader.GetNullableString("LastError"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            RevokedAt = reader.GetNullableDateTime("RevokedAt")
        };
    }

    private sealed record CloudflareResponse<T>(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("result")] T? Result,
        [property: JsonPropertyName("errors")] List<CloudflareError> Errors);

    private sealed record CloudflareDnsRecord(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("proxied")] bool Proxied);

    private sealed record CloudflareError(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message);

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex LabelRegex();
}

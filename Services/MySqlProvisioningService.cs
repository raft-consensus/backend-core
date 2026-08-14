using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public partial class MySqlProvisioningService : IDatabaseProvisioningService
{
    private readonly IUserDashboardService _dashboardService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MySqlProvisioningOptions _options;
    private readonly ILogger<MySqlProvisioningService> _logger;

    public MySqlProvisioningService(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDatabaseInstanceService databaseInstanceService,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<MySqlProvisioningOptions> options,
        ILogger<MySqlProvisioningService> logger)
    {
        _dashboardService = dashboardService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _databaseInstanceService = databaseInstanceService;
        _dataProtectionProvider = dataProtectionProvider;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Engine => "MySQL";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public int MaxDatabasesPerUser => _options.MaxDatabasesPerUser;

    public async Task<DatabaseProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("MySQL Provisioning via ABA partner cell is not configured or disabled.");
        }

        using var client = CreateHttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "partners/databases");
        var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("ABA MySQL provisioning returned HTTP {StatusCode}: {Body}", (int)response.StatusCode, responseBody);
            throw new InvalidOperationException($"ABA MySQL provisioning failed: {responseBody}");
        }

        var abaResponse = JsonSerializer.Deserialize<AbaCreateDatabaseResponseDto>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("ABA MySQL provisioning returned an empty or invalid payload.");

        var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
        var encryptedPassword = protector.Protect(abaResponse.PasswordTemporal);

        var instance = await _databaseInstanceService.CreateAsync(new DatabaseInstanceCreateDto
        {
            UserId = userId,
            Host = !string.IsNullOrWhiteSpace(abaResponse.Host) ? abaResponse.Host : _options.PublicHost,
            Port = abaResponse.Puerto > 0 ? abaResponse.Puerto : _options.PublicPort,
            DatabaseName = abaResponse.NombreBD,
            DatabaseUser = abaResponse.UsuarioBD,
            Engine = Engine,
            Status = "Active",
            UsedSpaceBytes = 0,
            MaxSpaceBytes = _options.DefaultMaxSpaceBytes,
            LastActivity = DateTime.UtcNow
        }, cancellationToken) ?? throw new InvalidOperationException("Failed to persist the provisioned MySQL database instance.");

        await _accessCredentialService.CreateAsync(new AccessCredentialCreateDto
        {
            DatabaseInstanceId = instance.Id,
            EncryptedPassword = encryptedPassword
        }, cancellationToken);

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "Provisioning",
            Description = $"MySQL database '{abaResponse.NombreBD}' provisioned through ABA partner cell."
        }, cancellationToken);

        _cacheExpiresAt = DateTime.MinValue;

        return new MySqlProvisioningResultDto
        {
            DatabaseInstanceId = instance.Id,
            Host = instance.Host,
            Port = instance.Port,
            DatabaseName = instance.DatabaseName,
            DatabaseUser = instance.DatabaseUser,
            Password = abaResponse.PasswordTemporal,
            Engine = Engine
        };
    }

    public async Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await UpdateStatusAsync(databaseInstanceId, instance, "Suspended", cancellationToken);
    }

    public async Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await UpdateStatusAsync(databaseInstanceId, instance, "Active", cancellationToken);
    }

    public async Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (IsAvailable)
        {
            try
            {
                var abaId = await GetAbaDatabaseIdByNameAsync(instance.DatabaseName, cancellationToken);
                if (abaId.HasValue)
                {
                    using var client = CreateHttpClient();
                    var resetResponse = await client.PostAsync($"partners/databases/{abaId.Value}/credenciales/reset", null, cancellationToken);
                    if (resetResponse.IsSuccessStatusCode)
                    {
                        var resetBody = await resetResponse.Content.ReadAsStringAsync(cancellationToken);
                        var resetData = JsonSerializer.Deserialize<AbaResetPasswordResponseDto>(resetBody, JsonOptions);
                        if (resetData is not null && !string.IsNullOrWhiteSpace(resetData.PasswordNueva))
                        {
                            var cred = await _accessCredentialService.GetByDatabaseInstanceIdAsync(databaseInstanceId, cancellationToken);
                            if (cred is not null)
                            {
                                var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
                                var encryptedPassword = protector.Protect(resetData.PasswordNueva);
                                await _accessCredentialService.UpdateAsync(cred.Id, new AccessCredentialUpdateDto
                                {
                                    EncryptedPassword = encryptedPassword
                                }, cancellationToken);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rotate credentials on ABA MySQL for {DatabaseName} during soft-delete", instance.DatabaseName);
            }
            finally
            {
                _cacheExpiresAt = DateTime.MinValue;
            }
        }

        await _databaseInstanceService.SoftDeleteAsync(databaseInstanceId, cancellationToken);
    }

    public async Task PurgeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (IsAvailable)
        {
            try
            {
                var abaId = await GetAbaDatabaseIdByNameAsync(instance.DatabaseName, cancellationToken);
                if (abaId.HasValue)
                {
                    using var client = CreateHttpClient();
                    var deleteResponse = await client.DeleteAsync($"partners/databases/{abaId.Value}", cancellationToken);
                    if (!deleteResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("ABA MySQL delete returned status {StatusCode} for DB {DatabaseName}", deleteResponse.StatusCode, instance.DatabaseName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to purge database {DatabaseName} on ABA MySQL", instance.DatabaseName);
            }
            finally
            {
                _cacheExpiresAt = DateTime.MinValue;
            }
        }

        await UpdateStatusAsync(databaseInstanceId, instance, "Deleted", cancellationToken);
    }

    public async Task<long> GetUsedSpaceBytesAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return 0L;
        }

        try
        {
            var item = await GetAbaDatabaseByNameAsync(databaseName, cancellationToken);
            if (item is not null)
            {
                return (long)(item.EspacioUtilizadoMB * 1024 * 1024);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query storage usage from ABA MySQL for {DatabaseName}", databaseName);
        }

        return 0L;
    }

    public Task<int> GetActiveConnectionCountAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("AbaMySql");
        var baseUrl = _options.BaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        client.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
        return client;
    }

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private static List<AbaDatabaseItemDto>? _cachedList;
    private static DateTime _cacheExpiresAt = DateTime.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    private async Task<int?> GetAbaDatabaseIdByNameAsync(string databaseName, CancellationToken cancellationToken)
    {
        var item = await GetAbaDatabaseByNameAsync(databaseName, cancellationToken);
        return item?.Id;
    }

    private async Task<AbaDatabaseItemDto?> GetAbaDatabaseByNameAsync(string databaseName, CancellationToken cancellationToken)
    {
        var list = await GetAllAbaDatabasesAsync(cancellationToken);
        return list.FirstOrDefault(d => string.Equals(d.NombreBD, databaseName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<AbaDatabaseItemDto>> GetAllAbaDatabasesAsync(CancellationToken cancellationToken)
    {
        if (_cachedList is not null && DateTime.UtcNow < _cacheExpiresAt)
        {
            return _cachedList;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedList is not null && DateTime.UtcNow < _cacheExpiresAt)
            {
                return _cachedList;
            }

            using var client = CreateHttpClient();
            var response = await client.GetAsync("partners/databases", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _cachedList = JsonSerializer.Deserialize<List<AbaDatabaseItemDto>>(responseBody, JsonOptions) ?? new();
                _cacheExpiresAt = DateTime.UtcNow.Add(CacheTtl);
                return _cachedList;
            }
            else
            {
                _logger.LogWarning("ABA MySQL GET /partners/databases returned HTTP {StatusCode}", (int)response.StatusCode);
                return _cachedList ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch databases from ABA MySQL API");
            return _cachedList ?? new();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task UpdateStatusAsync(
        int databaseInstanceId,
        DatabaseInstanceReadDto instance,
        string status,
        CancellationToken cancellationToken)
    {
        await _databaseInstanceService.UpdateAsync(
            databaseInstanceId,
            new DatabaseInstanceUpdateDto
            {
                UserId = instance.UserId,
                Host = instance.Host,
                Port = instance.Port,
                DatabaseName = instance.DatabaseName,
                DatabaseUser = instance.DatabaseUser,
                Engine = instance.Engine,
                Status = status,
                UsedSpaceBytes = instance.UsedSpaceBytes,
                MaxSpaceBytes = instance.MaxSpaceBytes,
                LastActivity = instance.LastActivity
            },
            cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class AbaCreateDatabaseResponseDto
    {
        [JsonPropertyName("baseDeDatosId")]
        public int BaseDeDatosId { get; set; }

        [JsonPropertyName("nombreBD")]
        public string NombreBD { get; set; } = string.Empty;

        [JsonPropertyName("usuarioBD")]
        public string UsuarioBD { get; set; } = string.Empty;

        [JsonPropertyName("passwordTemporal")]
        public string PasswordTemporal { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("puerto")]
        public int Puerto { get; set; }

        [JsonPropertyName("motor")]
        public string Motor { get; set; } = string.Empty;
    }

    private sealed class AbaDatabaseItemDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("nombreBD")]
        public string NombreBD { get; set; } = string.Empty;

        [JsonPropertyName("usuarioBD")]
        public string UsuarioBD { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("puerto")]
        public int Puerto { get; set; }

        [JsonPropertyName("estado")]
        public string Estado { get; set; } = string.Empty;

        [JsonPropertyName("espacioMaximoMB")]
        public double EspacioMaximoMB { get; set; }

        [JsonPropertyName("espacioUtilizadoMB")]
        public double EspacioUtilizadoMB { get; set; }

        [JsonPropertyName("porcentajeUsado")]
        public double PorcentajeUsado { get; set; }
    }

    private sealed class AbaResetPasswordResponseDto
    {
        [JsonPropertyName("baseDeDatosId")]
        public int BaseDeDatosId { get; set; }

        [JsonPropertyName("nombreBD")]
        public string NombreBD { get; set; } = string.Empty;

        [JsonPropertyName("usuarioBD")]
        public string UsuarioBD { get; set; } = string.Empty;

        [JsonPropertyName("passwordNueva")]
        public string PasswordNueva { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("puerto")]
        public int Puerto { get; set; }
    }
}

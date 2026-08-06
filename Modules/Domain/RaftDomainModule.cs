using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using raft_backend.Services;

namespace raft_backend.Modules.Domain;

public static class RaftDomainModule
{
    public static void AddRaftDomainModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IDatabaseInstanceService, DatabaseInstanceService>();
        builder.Services.AddScoped<IAccessCredentialService, AccessCredentialService>();
        builder.Services.AddScoped<IAuditEventService, AuditEventService>();
        builder.Services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
        builder.Services.AddScoped<IUserDashboardService, UserDashboardService>();
        builder.Services.AddScoped<IAiApiKeyService, AiApiKeyService>();
        builder.Services.AddScoped<IAiService, AiService>();
        builder.Services.AddScoped<IN8nProvisioningService, N8nProvisioningService>();
        builder.Services.AddSingleton<ISecurePasswordGenerator, SecurePasswordGenerator>();
        builder.Services.AddSingleton<IApiAvailabilityTracker, ApiAvailabilityTracker>();
        builder.Services.AddHostedService<DatabaseLifecycleBackgroundService>();
    }
}

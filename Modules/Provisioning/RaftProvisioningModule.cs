using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using raft_backend.Interfaces;
using raft_backend.Services;

namespace raft_backend.Modules.Provisioning;

public static class RaftProvisioningModule
{
    public static void AddRaftProvisioningModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ISqlServerProvisioningService, SqlServerProvisioningService>();
        builder.Services.AddScoped<IDatabaseProvisioningService, SqlServerProvisioningService>();
        builder.Services.AddScoped<IDatabaseProvisioningService, MySqlProvisioningService>();
        builder.Services.AddScoped<IDatabaseProvisioningService, PostgresProvisioningService>();
        builder.Services.AddScoped<IDatabaseProvisioningService, MongoProvisioningService>();
        builder.Services.AddScoped<IDatabaseProvisioningServiceResolver, DatabaseProvisioningServiceResolver>();
    }
}

using Microsoft.AspNetCore.Builder;
using raft_backend.Modules.Data;
using raft_backend.Modules.Domain;
using raft_backend.Modules.Hosting;
using raft_backend.Modules.Platform;
using raft_backend.Modules.Provisioning;

namespace raft_backend.Modules;

public static class RaftModuleBootstrapper
{
    public static void AddRaftModules(this WebApplicationBuilder builder)
    {
        builder.AddRaftPlatformModule();
        builder.AddRaftDataModule();
        builder.AddRaftDomainModule();
        builder.AddRaftProvisioningModule();
    }

    public static void UseRaftModules(this WebApplication app)
    {
        app.UseRaftHostingModule();
    }
}

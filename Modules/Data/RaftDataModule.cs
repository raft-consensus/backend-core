using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.Services;

namespace raft_backend.Modules.Data;

public static class RaftDataModule
{
    public static void AddRaftDataModule(this WebApplicationBuilder builder)
    {
        var raftConnectionString = builder.Configuration.GetConnectionString("RaftDb")
            ?? throw new InvalidOperationException("Missing connection string: ConnectionStrings:RaftDb");

        var sqlServerProvisioningConnectionString = builder.Configuration.GetConnectionString("SqlServerProvisioning")
            ?? BuildProvisioningConnectionString(raftConnectionString);

        var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"];
        var dataProtectionKeysPath = string.IsNullOrWhiteSpace(configuredKeysPath)
            ? Path.Combine(builder.Environment.ContentRootPath, "keys")
            : configuredKeysPath;

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
            .SetApplicationName("raft-backend");

        builder.Services.AddDbContext<RaftDbContext>(options =>
        {
            options.UseSqlServer(raftConnectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure();
            });
        });

        builder.Services.AddDbContext<SqlServerProvisioningDbContext>(options =>
        {
            options.UseSqlServer(sqlServerProvisioningConnectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure();
            });
        });

        builder.Services.AddScoped<ISqlStoredProcedureExecutor, SqlStoredProcedureExecutor>();
        builder.Services.AddScoped<ISqlServerCommandExecutor, SqlServerCommandExecutor>();
        builder.Services.Configure<ExternalCellConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));
    }

    private static string BuildProvisioningConnectionString(string raftConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(raftConnectionString)
        {
            InitialCatalog = "master"
        };

        return builder.ConnectionString;
    }
}

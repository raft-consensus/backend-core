using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using raft_backend.Configuration;
using raft_backend.Interfaces;
using raft_backend.Services;

namespace raft_backend.Modules.Dns;

public static class RaftDnsModule
{
    public static void AddRaftDnsModule(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("DnsProvisioning").Get<DnsProvisioningOptions>()
            ?? throw new InvalidOperationException("Missing configuration section: DnsProvisioning");

        ValidateDnsRuntimeConfiguration(builder.Environment, options);

        builder.Services.AddOptions<DnsProvisioningOptions>()
            .Bind(builder.Configuration.GetSection("DnsProvisioning"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddHttpClient("CloudflareDns", client =>
        {
            client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        });

        builder.Services.AddScoped<IDnsProvisioningService, DnsProvisioningService>();
    }

    private static void ValidateDnsRuntimeConfiguration(IWebHostEnvironment environment, DnsProvisioningOptions options)
    {
        if (!environment.IsDevelopment())
        {
            EnsureNotPlaceholder(options.ZoneId, "DnsProvisioning:ZoneId");
            EnsureNotPlaceholder(options.ZoneName, "DnsProvisioning:ZoneName");
            if (!string.IsNullOrWhiteSpace(options.CellSubdomain))
            {
                EnsureNotPlaceholder(options.CellSubdomain, "DnsProvisioning:CellSubdomain");
            }
            EnsureNotPlaceholder(options.ApiToken, "DnsProvisioning:ApiToken");
            EnsureNotPlaceholder(options.DefaultContent, "DnsProvisioning:DefaultContent");
        }

        if (string.IsNullOrWhiteSpace(options.ZoneId))
        {
            throw new InvalidOperationException("DnsProvisioning:ZoneId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ZoneName))
        {
            throw new InvalidOperationException("DnsProvisioning:ZoneName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            throw new InvalidOperationException("DnsProvisioning:ApiToken is required.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultContent))
        {
            throw new InvalidOperationException("DnsProvisioning:DefaultContent is required.");
        }
    }

    private static void EnsureNotPlaceholder(string value, string configurationKey)
    {
        var normalized = value.Trim();
        if (normalized.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Configuration value {configurationKey} still looks like a placeholder.");
        }
    }
}

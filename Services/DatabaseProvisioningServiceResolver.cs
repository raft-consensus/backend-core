using raft_backend.Interfaces;

namespace raft_backend.Services;

public class DatabaseProvisioningServiceResolver : IDatabaseProvisioningServiceResolver
{
    private readonly IReadOnlyDictionary<string, IDatabaseProvisioningService> _services;

    public DatabaseProvisioningServiceResolver(IEnumerable<IDatabaseProvisioningService> services)
    {
        _services = services.ToDictionary(service => Normalize(service.Engine), StringComparer.OrdinalIgnoreCase);
    }

    public IDatabaseProvisioningService Resolve(string engine)
    {
        var key = Normalize(engine);
        if (_services.TryGetValue(key, out var service))
        {
            return service;
        }

        throw new KeyNotFoundException($"No database provisioning service is registered for engine '{engine}'.");
    }

    public IReadOnlyCollection<IDatabaseProvisioningService> GetAll()
    {
        return _services.Values.ToArray();
    }

    private static string Normalize(string engine)
    {
        var normalized = engine.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sqlserver" => "sql server",
            "mysql" => "mysql",
            "postgres" => "postgresql",
            _ => normalized
        };
    }
}

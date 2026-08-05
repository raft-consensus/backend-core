namespace raft_backend.Interfaces;

public interface IDatabaseProvisioningServiceResolver
{
    IDatabaseProvisioningService Resolve(string engine);

    IReadOnlyCollection<IDatabaseProvisioningService> GetAll();
}

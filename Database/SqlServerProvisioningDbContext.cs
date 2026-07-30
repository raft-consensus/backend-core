using Microsoft.EntityFrameworkCore;

namespace raft_backend.Database;

public class SqlServerProvisioningDbContext : DbContext
{
    public SqlServerProvisioningDbContext(DbContextOptions<SqlServerProvisioningDbContext> options)
        : base(options)
    {
    }
}

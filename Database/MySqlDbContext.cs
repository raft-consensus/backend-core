using Microsoft.EntityFrameworkCore;

namespace raft_backend.Database;

public class MySqlDbContext : DbContext
{
    public MySqlDbContext(DbContextOptions<MySqlDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // This context is used for provisioned MySQL databases.
        // When the user schema is defined, the corresponding entities are added here.
    }
}

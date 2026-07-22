using Microsoft.EntityFrameworkCore;
using raft_backend.Models;

namespace raft_backend.Database;

public class RaftDbContext : DbContext
{
    public RaftDbContext(DbContextOptions<RaftDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<DatabaseInstance> DatabaseInstances => Set<DatabaseInstance>();
    public DbSet<AccessCredential> AccessCredentials => Set<AccessCredential>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.AvatarUrl).HasMaxLength(2048);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ProviderUserId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(20).IsRequired().HasDefaultValue("User");
            entity.Property(x => x.Created_at)
                .HasColumnName("Created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.Updated_at).HasColumnName("Updated_at");
            entity.Property(x => x.Deleted_at).HasColumnName("Deleted_at");
            entity.Property(x => x.LastLogin).HasColumnName("LastLogin");
            entity.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
            entity.HasIndex(x => x.Email);
            entity.HasQueryFilter(x => x.Deleted_at == null);
            entity.HasMany(x => x.DatabaseInstances)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.AuditEvents)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DatabaseInstance>(entity =>
        {
            entity.ToTable("DatabaseInstances");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Host).HasMaxLength(255).IsRequired();
            entity.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DatabaseUser).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Engine).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status)
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("Active");
            entity.Property(x => x.UsedSpaceBytes).HasDefaultValue(0);
            entity.Property(x => x.MaxSpaceBytes).HasDefaultValue(20 * 1024 * 1024);
            entity.Property(x => x.Created_at)
                .HasColumnName("Created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.Updated_at).HasColumnName("Updated_at");
            entity.Property(x => x.Deleted_at).HasColumnName("Deleted_at");
            entity.HasIndex(x => x.UserId);
            entity.HasQueryFilter(x => x.Deleted_at == null);
            entity.HasOne(x => x.AccessCredential)
                .WithOne(x => x.DatabaseInstance)
                .HasForeignKey<AccessCredential>(x => x.DatabaseInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccessCredential>(entity =>
        {
            entity.ToTable("AccessCredentials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EncryptedPassword)
                .HasMaxLength(1024)
                .IsRequired();
            entity.Property(x => x.Created_at)
                .HasColumnName("Created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.Updated_at).HasColumnName("Updated_at");
            entity.Property(x => x.Deleted_at).HasColumnName("Deleted_at");
            entity.HasIndex(x => x.DatabaseInstanceId).IsUnique();
            entity.HasQueryFilter(x => x.Deleted_at == null);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(45);
            entity.Property(x => x.AdditionalData).HasColumnType("nvarchar(max)");
            entity.Property(x => x.Created_at)
                .HasColumnName("Created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.UserId);
            entity.HasQueryFilter(x => x.Deleted_at == null);
            entity.HasOne(x => x.User)
                .WithMany(x => x.AuditEvents)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

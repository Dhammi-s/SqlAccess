using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Models;
using SqlAccess.Api.Vault.Models;

namespace SqlAccess.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Agency> Agencies => Set<Agency>();

    // CI/CD portal
    public DbSet<Website> Websites => Set<Website>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentLog> DeploymentLogs => Set<DeploymentLog>();

    // Secret Vault
    public DbSet<VaultApplication> VaultApplications => Set<VaultApplication>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<SecretVersion> SecretVersions => Set<SecretVersion>();
    public DbSet<ApplicationSecret> ApplicationSecrets => Set<ApplicationSecret>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agency>(e =>
        {
            e.ToTable("agencies");
            e.HasKey(a => a.AgencyId);
            e.Property(a => a.AgencyId).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Website>().Property(w => w.WebsiteId).ValueGeneratedOnAdd();
        modelBuilder.Entity<Deployment>().Property(d => d.DeploymentId).ValueGeneratedOnAdd();
        modelBuilder.Entity<DeploymentLog>().Property(l => l.LogId).ValueGeneratedOnAdd();

        modelBuilder.Entity<VaultApplication>().Property(a => a.ApplicationId).ValueGeneratedOnAdd();
        modelBuilder.Entity<Secret>().Property(s => s.SecretId).ValueGeneratedOnAdd();
        modelBuilder.Entity<SecretVersion>().Property(v => v.SecretVersionId).ValueGeneratedOnAdd();
        modelBuilder.Entity<ApplicationSecret>().Property(a => a.ApplicationSecretId).ValueGeneratedOnAdd();
        modelBuilder.Entity<AuditLog>().Property(a => a.AuditLogId).ValueGeneratedOnAdd();
    }
}

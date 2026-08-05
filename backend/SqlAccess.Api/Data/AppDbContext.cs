using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Models;

namespace SqlAccess.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Agency> Agencies => Set<Agency>();

    // CI/CD portal
    public DbSet<Website> Websites => Set<Website>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentLog> DeploymentLogs => Set<DeploymentLog>();

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
    }
}

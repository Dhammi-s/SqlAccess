using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Models;

namespace SqlAccess.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Agency> Agencies => Set<Agency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agency>(e =>
        {
            e.ToTable("agencies");
            e.HasKey(a => a.AgencyId);
            e.Property(a => a.AgencyId).ValueGeneratedOnAdd();
        });
    }
}

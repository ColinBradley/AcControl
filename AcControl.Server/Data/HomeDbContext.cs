namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

public class HomeDbContext : DbContext
{
    public HomeDbContext(DbContextOptions<HomeDbContext> options) : base(options)
    {
    }

    public DbSet<InverterDaySummaryEntry> InverterDaySummaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        new InverterDaySummaryEntryConfiguration().Configure(modelBuilder.Entity<InverterDaySummaryEntry>());
    }
}

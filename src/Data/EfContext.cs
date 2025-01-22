using Conductor.Model;
using Conductor.Shared.Config;
using LinqToDB;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Data;

public class EfContext(string? dbType = null) : DbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Destination> Destinations { get; set; } = null!;
    public DbSet<Origin> Origins { get; set; } = null!;
    public DbSet<Extraction> Extractions { get; set; } = null!;
    public DbSet<Record> Records { get; set; } = null!;
    public DbSet<Schedule> Schedules { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string db = dbType ?? Settings.DbType;
        if (!optionsBuilder.IsConfigured)
        {
            _ = db switch
            {
                ProviderName.SQLite => optionsBuilder.UseSqlite(Settings.ConnectionString),
                ProviderName.PostgreSQL => optionsBuilder.UseNpgsql(Settings.ConnectionString),
                _ => throw new InvalidOperationException("Unsupported database type")
            };
        }
    }
}

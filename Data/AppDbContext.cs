using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public DbSet<License> Licenses { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
}
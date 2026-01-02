
using Microsoft.EntityFrameworkCore;

namespace CentauriSeo.Infrastructure.Data;

public class LlmCacheDbContext : DbContext
{
    public LlmCacheDbContext(DbContextOptions<LlmCacheDbContext> options) : base(options) { }

    public DbSet<LlmCacheEntry> LlmCacheEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LlmCacheEntry>()
            .HasIndex(e => e.RequestKey)
            .IsUnique();

        base.OnModelCreating(modelBuilder);
    }
}
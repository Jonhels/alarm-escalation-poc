using Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AlarmEscalationDbContext : DbContext
{
    public AlarmEscalationDbContext(DbContextOptions<AlarmEscalationDbContext> options)
        : base(options)
    {
    }

    public DbSet<CallAttemptEntity> CallAttempts => Set<CallAttemptEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CallAttemptConfiguration());
    }
}

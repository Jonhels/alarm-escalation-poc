using Common.Options;
using Domain.CallAttempts.Contracts;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Startup;

public static class ServiceExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, ExternalServicesOptions options)
    {
        var connectionString = options.Database?.ConnectionString
            ?? throw new InvalidOperationException("Database connection string is not configured");

        services.AddDbContext<AlarmEscalationDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddTransient<ICallAttemptRepository, CallAttemptRepository>();
    }
}

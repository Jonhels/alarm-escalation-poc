using Domain.CallAttempts.Contracts;
using Domain.CallAttempts.Models;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class CallAttemptRepository : ICallAttemptRepository
{
    private readonly AlarmEscalationDbContext _dbContext;

    public CallAttemptRepository(AlarmEscalationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CallAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CallAttempts
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<IReadOnlyList<CallAttempt>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CallAttempts
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<CallAttempt?> GetByTwilioCallSidAsync(string callSid, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CallAttempts
            .FirstOrDefaultAsync(e => e.TwilioCallSid == callSid, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<CallAttempt> CreateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(callAttempt);
        _dbContext.CallAttempts.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDomain(entity);
    }

    public async Task UpdateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CallAttempts
            .FirstOrDefaultAsync(e => e.Id == callAttempt.Id, cancellationToken);

        if (entity is null)
        {
            throw new CallAttemptNotFoundException(callAttempt.Id);
        }

        entity.Status = callAttempt.Status.ToString();
        entity.AcknowledgedAt = callAttempt.AcknowledgedAt;
        entity.TwilioCallSid = callAttempt.TwilioCallSid;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CallAttempt MapToDomain(CallAttemptEntity entity)
    {
        return new CallAttempt
        {
            Id = entity.Id,
            PhoneNumber = entity.PhoneNumber,
            AlarmMessage = entity.AlarmMessage,
            Status = Enum.Parse<CallAttemptStatus>(entity.Status),
            TwilioCallSid = entity.TwilioCallSid,
            CreatedAt = entity.CreatedAt,
            AcknowledgedAt = entity.AcknowledgedAt
        };
    }

    private static CallAttemptEntity MapToEntity(CallAttempt domain)
    {
        return new CallAttemptEntity
        {
            Id = domain.Id,
            PhoneNumber = domain.PhoneNumber,
            AlarmMessage = domain.AlarmMessage,
            Status = domain.Status.ToString(),
            TwilioCallSid = domain.TwilioCallSid,
            CreatedAt = domain.CreatedAt,
            AcknowledgedAt = domain.AcknowledgedAt
        };
    }
}

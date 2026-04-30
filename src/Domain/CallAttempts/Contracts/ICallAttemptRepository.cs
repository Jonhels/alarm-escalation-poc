using Domain.CallAttempts.Models;

namespace Domain.CallAttempts.Contracts;

public interface ICallAttemptRepository
{
    Task<CallAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CallAttempt?> GetByTwilioCallSidAsync(string callSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CallAttempt>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<CallAttempt> CreateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default);
    Task UpdateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default);
}

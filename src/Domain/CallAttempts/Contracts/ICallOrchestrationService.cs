using Domain.CallAttempts.Models;

namespace Domain.CallAttempts.Contracts;

public interface ICallOrchestrationService
{
    Task<CallAttempt> TriggerAlarmCallAsync(
        string phoneNumber,
        string alarmMessage,
        CancellationToken cancellationToken = default);

    Task HandleGatherAsync(
        string callSid,
        string digits,
        CancellationToken cancellationToken = default);

    Task HandleStatusCallbackAsync(
        string callSid,
        string callStatus,
        CancellationToken cancellationToken = default);
}

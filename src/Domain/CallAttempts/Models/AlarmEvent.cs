namespace Domain.CallAttempts.Models;

public record AlarmEvent
{
    public Guid Id { get; init; }
    public required string AlarmSource { get; init; }
    public required string AlarmType { get; init; }
    public required string Message { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid? CallAttemptId { get; init; }
}

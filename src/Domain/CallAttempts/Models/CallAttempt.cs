namespace Domain.CallAttempts.Models;

public record CallAttempt
{
    public Guid Id { get; init; }
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
    public CallAttemptStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public string? TwilioCallSid { get; init; }
}

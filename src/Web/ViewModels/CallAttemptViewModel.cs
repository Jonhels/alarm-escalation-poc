using Domain.CallAttempts.Models;

namespace Web.ViewModels;

public record CallAttemptViewModel
{
    public Guid Id { get; init; }
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public string? TwilioCallSid { get; init; }
}

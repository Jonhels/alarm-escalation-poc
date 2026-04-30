namespace Infrastructure.Persistence;

public class CallAttemptEntity
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string AlarmMessage { get; set; } = "";
    public string Status { get; set; } = "";
    public string? TwilioCallSid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

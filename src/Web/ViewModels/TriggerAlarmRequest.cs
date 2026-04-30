namespace Web.ViewModels;

public record TriggerAlarmRequest
{
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
}

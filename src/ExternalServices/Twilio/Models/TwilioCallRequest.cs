namespace ExternalServices.Twilio.Models;

public class TwilioCallRequest
{
    public required string ToPhoneNumber { get; init; }
    public required string FromPhoneNumber { get; init; }
    public required string WebhookBaseUrl { get; init; }
}

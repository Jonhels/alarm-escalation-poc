namespace Common.Options;

public class TwilioOptions
{
    public string AccountSid { get; init; } = "";
    public string AuthToken { get; init; } = "";
    public string FromPhoneNumber { get; init; } = "";
    public bool? UseDummyClient { get; init; }
    public string? BaseWebhookUrl { get; init; }
}

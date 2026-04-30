namespace Common.Options;

public class ExternalServicesOptions
{
    public TwilioOptions? Twilio { get; init; }
    public DatabaseOptions? Database { get; init; }
}

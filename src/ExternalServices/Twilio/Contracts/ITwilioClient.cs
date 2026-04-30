namespace ExternalServices.Twilio.Contracts;

public interface ITwilioClient
{
    Task<string> InitiateCallAsync(string toPhoneNumber, string fromPhoneNumber, string webhookBaseUrl, CancellationToken cancellationToken = default);
}

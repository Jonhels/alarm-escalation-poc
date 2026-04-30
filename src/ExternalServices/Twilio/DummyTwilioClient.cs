using ExternalServices.Twilio.Contracts;
using Microsoft.Extensions.Logging;

namespace ExternalServices.Twilio;

public class DummyTwilioClient : ITwilioClient
{
    private readonly ILogger<DummyTwilioClient> _logger;

    public DummyTwilioClient(ILogger<DummyTwilioClient> logger)
    {
        _logger = logger;
    }

    public Task<string> InitiateCallAsync(
        string toPhoneNumber,
        string fromPhoneNumber,
        string webhookBaseUrl,
        CancellationToken cancellationToken = default)
    {
        var dummyCallSid = $"DUMMY_{Guid.NewGuid():N}";
        _logger.LogInformation(
            "Dummy Twilio call initiated: To={ToPhoneNumber}, From={FromPhoneNumber}, Sid={CallSid}",
            toPhoneNumber, fromPhoneNumber, dummyCallSid);
        return Task.FromResult(dummyCallSid);
    }
}

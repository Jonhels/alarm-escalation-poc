using System.Text;
using Common.Options;
using ExternalServices.Twilio.Contracts;
using ExternalServices.Twilio.Models;
using Flurl.Http;
using Microsoft.Extensions.Options;

namespace ExternalServices.Twilio;

public class TwilioClient : ITwilioClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> InitiateCallAsync(
        string toPhoneNumber,
        string fromPhoneNumber,
        string webhookBaseUrl,
        CancellationToken cancellationToken = default)
    {
        var accountSid = _options.AccountSid;
        var authToken = _options.AuthToken;

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Calls.json";

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));

        var flurlClient = new FlurlClient(_httpClient);

        var response = await flurlClient.Request(url)
            .WithHeader("Authorization", $"Basic {basicAuth}")
            .PostUrlEncodedAsync(new
            {
                To = toPhoneNumber,
                From = fromPhoneNumber,
                Url = $"{webhookBaseUrl}/api/twilio/voice"
            }, cancellationToken: cancellationToken)
            .ReceiveJson<TwilioCallResponse>();

        return response.Sid;
    }
}

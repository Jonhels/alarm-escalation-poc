using Common.Options;
using Domain.CallAttempts.Contracts;
using ExternalServices.Services;
using ExternalServices.Twilio;
using ExternalServices.Twilio.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExternalServices.Startup;

public static class ServiceExtensions
{
    public static void AddExternalServices(
        this IServiceCollection services,
        ExternalServicesOptions options,
        IHostEnvironment env)
    {
        services.AddTransient<ICallOrchestrationService, CallOrchestrationService>();

        if (options.Twilio?.UseDummyClient == true)
        {
            services.AddTransient<ITwilioClient, DummyTwilioClient>();
        }
        else
        {
            services.AddHttpClient<ITwilioClient, TwilioClient>();
        }
    }
}

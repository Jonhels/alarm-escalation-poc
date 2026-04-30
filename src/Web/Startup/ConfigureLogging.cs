using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Web.Startup;

public static class ConfigureLogging
{
    public static void ConfigureSerilog(this ConfigureHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((hostContext, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(hostContext.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .MinimumLevel.Information();
                if (hostContext.HostingEnvironment.IsDevelopment())
                {
                    configuration
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("System", LogEventLevel.Information)
                        .WriteTo.Console();
                }
                else
                {
                    configuration
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                        .MinimumLevel.Override("System", LogEventLevel.Warning)
                        .WriteTo.Console(new JsonFormatter(renderMessage: true));
                }
            },
            preserveStaticLogger: true);
    }
}

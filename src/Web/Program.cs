using System.Text.Json.Serialization;
using Common.Options;
using ExternalServices.Startup;
using Infrastructure.Startup;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Web.Filters;
using Web.Startup;

// Configure initial bootstrap logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter(renderMessage: true))
    .CreateBootstrapLogger();

Log.Information("Starting Alarm Escalation web application");

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.ConfigureSerilog();

// Add services to the container.
var externalServicesOptions = builder.Configuration.GetSection("ExternalServices").Get<ExternalServicesOptions>();
builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection("ExternalServices:Twilio"));

builder.Services.AddExternalServices(externalServicesOptions!, builder.Environment);
builder.Services.AddInfrastructure(externalServicesOptions!);

// Make enums return as strings
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    var enumConverter = new JsonStringEnumConverter();
    opts.JsonSerializerOptions.Converters.Add(enumConverter);
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionsFilter>();
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.ToString());
});

builder.Services.AddHealthChecks();
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors();
}

app.UseHealthChecks("/health");

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

public partial class Program { }

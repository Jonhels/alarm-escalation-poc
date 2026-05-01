# Architecture Guide: Learning C# Through the Alarm Escalation POC

This guide teaches C# by walking through the **actual code** in this project. Instead of starting with abstract language concepts and then showing examples, we present real project files first and explain what they do, why they were written that way, and how specific C# language features make the architecture work.

---

## Table of Contents

1. [How This Project Uses C# Records](#1-how-this-project-uses-c-records)
2. [How This Project Uses Interfaces](#2-how-this-project-uses-interfaces)
3. [How This Project Uses Dependency Injection](#3-how-this-project-uses-dependency-injection)
4. [How This Project Uses EF Core](#4-how-this-project-uses-ef-core)
5. [How This Project Uses Async/Await](#5-how-this-project-uses-asyncawait)
6. [How This Project Handles Configuration](#6-how-this-project-handles-configuration)
7. [How This Project Structures API Responses](#7-how-this-project-structures-api-responses)
8. [End-to-End Flow: Trace a Request](#8-end-to-end-flow-trace-a-request)

---

## 1. How This Project Uses C# Records

### 1.1 The Domain Model: `CallAttempt.cs`

Here is the exact file `src/Domain/CallAttempts/Models/CallAttempt.cs`:

```csharp
namespace Domain.CallAttempts.Models;

public record CallAttempt
{
    public Guid Id { get; init; }
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
    public CallAttemptStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public string? TwilioCallSid { get; init; }
}
```

And the status enum from `src/Domain/CallAttempts/Models/CallAttemptStatus.cs`:

```csharp
namespace Domain.CallAttempts.Models;

public enum CallAttemptStatus
{
    Pending,
    InProgress,
    Acknowledged,
    Failed,
    NoAnswer
}
```

And the alarm event model `src/Domain/CallAttempts/Models/AlarmEvent.cs`:

```csharp
namespace Domain.CallAttempts.Models;

public record AlarmEvent
{
    public Guid Id { get; init; }
    public required string AlarmSource { get; init; }
    public required string AlarmType { get; init; }
    public required string Message { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid? CallAttemptId { get; init; }
}
```

### What's happening here and why

**`record` instead of `class`**: A `record` declared without `struct` is a reference type that provides built-in value-based equality and an immutable-by-default design. When you declare `public record CallAttempt`, the compiler automatically generates:

- A constructor that accepts all properties
- `Equals()` and `GetHashCode()` based on property values (not reference identity)
- A `ToString()` that prints all properties
- A `Deconstruct()` method for pattern matching
- The special `with` expression support

This project uses records for domain models because a `CallAttempt` represents a **value** — two call attempts with the same values (especially the same Id) are considered equal. Value equality matters more than reference equality for domain objects.

**`{ get; init; }` — init-only properties**: The `init` accessor means the property can only be set during object construction (either in the constructor or with an object initializer), but never after. This makes the record *shallowly immutable* after creation. Once a `CallAttempt` exists, you cannot accidentally mutate `callAttempt.Status = something` — the compiler prevents it.

**`required` modifier**: The `required` keyword (C# 11+) means that any code constructing a `CallAttempt` *must* provide a value for `PhoneNumber` and `AlarmMessage`. The compiler enforces this at build time. Without it, someone could create `new CallAttempt { Id = Guid.NewGuid() }` and forget the phone number — which would break the entire flow. With `required`, the compiler says "you forgot to set PhoneNumber" before the code even runs.

```csharp
// This COMPILES — `required` properties are set:
var attempt = new CallAttempt
{
    Id = Guid.NewGuid(),
    PhoneNumber = "+1234567890",       // required
    AlarmMessage = "Fire detected",     // required
    Status = CallAttemptStatus.Pending,
    CreatedAt = DateTime.UtcNow
};

// This DOES NOT COMPILE — missing required properties:
var broken = new CallAttempt { Id = Guid.NewGuid() };
// Compiler error: Required member 'CallAttempt.PhoneNumber' must be set
```

### 1.2 The `with` Expression in Action

Now look at how `with` is used throughout `src/ExternalServices/Services/CallOrchestrationService.cs`. Here are the key moments where a call attempt's status changes:

**Moment 1 — Creating the initial call attempt (lines 34-41):**

```csharp
var callAttempt = new CallAttempt
{
    Id = Guid.NewGuid(),
    PhoneNumber = phoneNumber,
    AlarmMessage = alarmMessage,
    Status = CallAttemptStatus.Pending,
    CreatedAt = DateTime.UtcNow
};

callAttempt = await _repository.CreateAsync(callAttempt, cancellationToken);
```

A new immutable record is created with `Status = Pending`. It's saved, and the repository returns the persisted version (which might have database-generated values, though in this project the ID is client-generated).

**Moment 2 — After Twilio successfully initiates the call (lines 60-64):**

```csharp
callAttempt = callAttempt with
{
    Status = CallAttemptStatus.InProgress,
    TwilioCallSid = callSid
};
```

**Moment 3 — If Twilio fails (lines 78):**

```csharp
callAttempt = callAttempt with { Status = CallAttemptStatus.Failed };
```

**Moment 4 — When the user presses 1 to acknowledge (lines 98-102):**

```csharp
callAttempt = callAttempt with
{
    Status = CallAttemptStatus.Acknowledged,
    AcknowledgedAt = DateTime.UtcNow
};
```

**Moment 5 — When Twilio reports the call status (lines 134):**

```csharp
callAttempt = callAttempt with { Status = newStatus };
```

### What `with` actually does

The `with` expression creates a **new copy** of the record with zero or more properties changed. The original record is untouched. Consider:

```csharp
var pending = new CallAttempt { Id = Guid.NewGuid(), PhoneNumber = "+123", AlarmMessage = "Test", Status = CallAttemptStatus.Pending, CreatedAt = DateTime.UtcNow };

// with creates a brand-new record:
var inProgress = pending with { Status = CallAttemptStatus.InProgress };

// pending.Status is STILL Pending — the original is unchanged
// inProgress.Status is InProgress
// Every other property (Id, PhoneNumber, etc.) is copied verbatim
```

This is critical for this project's architecture because:

1. **Thread safety**: No other code can observe a half-mutated `CallAttempt`. Status transitions are atomic — you get a whole new object or nothing.
2. **Audit trail clarity**: Each variable binding (`callAttempt = callAttempt with { ... }`) represents a distinct state in the lifecycle. You can trace exactly where and why the status changed.
3. **No defensive copying**: The repository's `UpdateAsync` receives the full new state. There's no risk of the caller holding a reference to the same object that later mutates inside the repository.

### Why `record` with `init` + `with` beats mutable classes

If `CallAttempt` were a mutable class:

```csharp
// Mutable class — what we deliberately avoid:
public class MutableCallAttempt
{
    public Guid Id { get; set; }
    public CallAttemptStatus Status { get; set; }  // Anyone can change this anywhere
}
```

Then this bug becomes possible:

```csharp
var attempt = await _repository.GetByIdAsync(id);
// ... some other code accidentally mutates:
attempt.Status = CallAttemptStatus.Failed;  // Oops, the in-memory object is now wrong
await _repository.UpdateAsync(attempt);     // Persists the accidentally-changed status
```

With records and `init`, the compiler catches this at build time. You *must* use `with` to create a new version, making the intent explicit.

---

## 2. How This Project Uses Interfaces

### 2.1 The Repository Contract

`src/Domain/CallAttempts/Contracts/ICallAttemptRepository.cs`:

```csharp
using Domain.CallAttempts.Models;

namespace Domain.CallAttempts.Contracts;

public interface ICallAttemptRepository
{
    Task<CallAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CallAttempt?> GetByTwilioCallSidAsync(string callSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CallAttempt>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<CallAttempt> CreateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default);
    Task UpdateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default);
}
```

### 2.2 The Orchestration Contract

`src/Domain/CallAttempts/Contracts/ICallOrchestrationService.cs`:

```csharp
using Domain.CallAttempts.Models;

namespace Domain.CallAttempts.Contracts;

public interface ICallOrchestrationService
{
    Task<CallAttempt> TriggerAlarmCallAsync(
        string phoneNumber,
        string alarmMessage,
        CancellationToken cancellationToken = default);

    Task HandleGatherAsync(
        string callSid,
        string digits,
        CancellationToken cancellationToken = default);

    Task HandleStatusCallbackAsync(
        string callSid,
        string callStatus,
        CancellationToken cancellationToken = default);
}
```

### 2.3 The Twilio Client Contract — With Two Implementations

`src/ExternalServices/Twilio/Contracts/ITwilioClient.cs`:

```csharp
namespace ExternalServices.Twilio.Contracts;

public interface ITwilioClient
{
    Task<string> InitiateCallAsync(string toPhoneNumber, string fromPhoneNumber, string webhookBaseUrl, CancellationToken cancellationToken = default);
}
```

**Implementation 1 — Real Twilio** (`src/ExternalServices/Twilio/TwilioClient.cs`):

```csharp
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
```

**Implementation 2 — Dummy (no-op)** (`src/ExternalServices/Twilio/DummyTwilioClient.cs`):

```csharp
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
```

### 2.4 The Environment-Aware Switch

`src/ExternalServices/Startup/ServiceExtensions.cs`:

```csharp
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
```

### Why interfaces matter here

This is the single most important architectural pattern in the project. Every collaborator depends on the **contract** (interface), never on a concrete implementation:

| Who depends on what | The dependency |
|---|---|
| `TestAlarmController` | `ICallOrchestrationService` |
| `TwilioWebhookController` | `ICallOrchestrationService` |
| `CallOrchestrationService` | `ICallAttemptRepository`, `ITwilioClient` |
| `CallAttemptsController` | `ICallAttemptRepository` |

Notice that **no class** directly references `CallOrchestrationService`, `CallAttemptRepository`, `TwilioClient`, or `DummyTwilioClient` by their concrete types. Every dependency is on an interface.

This enables three critical things:

1. **Testing**: You can unit test `CallOrchestrationService` by injecting a mock `ICallAttemptRepository` and a mock `ITwilioClient`. No database, no network calls.

2. **Implementation switching at runtime**: The `if (options.Twilio?.UseDummyClient == true)` check in `ServiceExtensions.cs` decides which `ITwilioClient` implementation gets registered. The rest of the codebase doesn't know or care — it just calls `ITwilioClient.InitiateCallAsync()`. In development, you get a dummy that generates fake call SIDs. In production, you get the real Twilio HTTP client. Zero code changes outside this one file.

3. **Implementation switching at registration level**: Notice the difference between `AddTransient<ITwilioClient, DummyTwilioClient>()` and `AddHttpClient<ITwilioClient, TwilioClient>()`. The real `TwilioClient` needs an `HttpClient` (which ASP.NET manages via `IHttpClientFactory` for connection pooling and resilience), so it uses `AddHttpClient`. The dummy doesn't need HTTP at all, so it uses plain `AddTransient`. Again, the consumers don't care.

---

## 3. How This Project Uses Dependency Injection

### 3.1 The Entry Point: `Program.cs`

`src/Web/Program.cs` is where everything gets wired together:

```csharp
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
```

### 3.2 Step-by-Step DI Walkthrough

#### Step 1: Configuration Binding (lines 26-27)

```csharp
var externalServicesOptions = builder.Configuration.GetSection("ExternalServices").Get<ExternalServicesOptions>();
builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection("ExternalServices:Twilio"));
```

Two different patterns are used here:

- **Direct binding**: `Get<ExternalServicesOptions>()` reads the entire `ExternalServices` JSON section and deserializes it into a POCO immediately. This is used when you need the values *right now* to make registration decisions (like checking `UseDummyClient`).
- **Options pattern**: `Configure<TwilioOptions>(...)` registers the `ExternalServices:Twilio` section so that any class can later inject `IOptions<TwilioOptions>` and get the current values. This is used for runtime configuration that doesn't affect DI wiring.

#### Step 2: Layer Registration (lines 29-30)

```csharp
builder.Services.AddExternalServices(externalServicesOptions!, builder.Environment);
builder.Services.AddInfrastructure(externalServicesOptions!);
```

Each layer exposes a `static class ServiceExtensions` with extension methods on `IServiceCollection`. This keeps `Program.cs` clean — it delegates to each layer to register its own dependencies. The infrastructure layer receives `externalServicesOptions` because it needs the database connection string to configure EF Core.

#### Step 3: JSON Serialization Configuration (lines 33-37)

```csharp
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    var enumConverter = new JsonStringEnumConverter();
    opts.JsonSerializerOptions.Converters.Add(enumConverter);
});
```

Without this, the `CallAttemptStatus` enum would serialize as integers (`"status": 2`). With `JsonStringEnumConverter`, it serializes as human-readable strings (`"status": "Acknowledged"`). This matters for API consumers who shouldn't need to know the numeric enum mapping.

#### Step 4: Global Exception Filter (lines 39-42)

```csharp
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionsFilter>();
});
```

Instead of try-catch blocks in every controller action, a single `ApiExceptionsFilter` intercepts unhandled exceptions and converts them to proper HTTP responses. (We'll examine this filter in Section 7.)

#### Step 5: Cross-Cutting Concerns (lines 44-62)

Swagger, health checks, CORS, and Problem Details (RFC 7807) are registered. These are standard ASP.NET middleware and services.

#### Step 6: Middleware Pipeline (lines 64-84)

After `builder.Build()`, the middleware pipeline is configured in order:

```
ExceptionHandler → Swagger (dev only) → Health Checks → Request Logging → HTTPS Redirect → MVC Controllers
```

Order matters: the exception handler must be first to catch errors from everything downstream. HTTPS redirection and CORS come before controller routing.

### 3.3 Constructor Injection in Services

Every class receives its dependencies through its constructor. Example from `src/ExternalServices/Services/CallOrchestrationService.cs`:

```csharp
public class CallOrchestrationService : ICallOrchestrationService
{
    private readonly ICallAttemptRepository _repository;
    private readonly ITwilioClient _twilioClient;
    private readonly TwilioOptions _twilioOptions;
    private readonly ILogger<CallOrchestrationService> _logger;

    public CallOrchestrationService(
        ICallAttemptRepository repository,
        ITwilioClient twilioClient,
        IOptions<TwilioOptions> twilioOptions,
        ILogger<CallOrchestrationService> logger)
    {
        _repository = repository;
        _twilioClient = twilioClient;
        _twilioOptions = twilioOptions.Value;  // Unwrap IOptions<T> to get the actual options object
        _logger = logger;
    }
    // ...
}
```

The ASP.NET DI container automatically resolves each parameter:

1. It sees `ICallAttemptRepository` → finds the registration `AddTransient<ICallAttemptRepository, CallAttemptRepository>()` → creates a `CallAttemptRepository`.
2. It sees `ITwilioClient` → finds `AddTransient<ITwilioClient, DummyTwilioClient>()` (in dev) → creates a `DummyTwilioClient`. It never needs to know which implementation; the container handles it.
3. It sees `IOptions<TwilioOptions>` → provides the configuration values bound from `appsettings.json`.
4. It sees `ILogger<CallOrchestrationService>` → provides a Serilog logger automatically scoped to this class.

### 3.4 The `ServiceExtensions` Pattern

Each layer defines its own registration logic in a static extension method class:

**Infrastructure layer** (`src/Infrastructure/Startup/ServiceExtensions.cs`):

```csharp
using Common.Options;
using Domain.CallAttempts.Contracts;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Startup;

public static class ServiceExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, ExternalServicesOptions options)
    {
        var connectionString = options.Database?.ConnectionString
            ?? throw new InvalidOperationException("Database connection string is not configured");

        services.AddDbContext<AlarmEscalationDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddTransient<ICallAttemptRepository, CallAttemptRepository>();
    }
}
```

**External services layer** (`src/ExternalServices/Startup/ServiceExtensions.cs`):

```csharp
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
```

This pattern keeps registration logic close to the implementations it registers, and `Program.cs` stays readable.

---

## 4. How This Project Uses EF Core

### 4.1 The Dual-Model Pattern

This project has **two separate models** for the same concept. Here they are side by side:

**Domain record** (`src/Domain/CallAttempts/Models/CallAttempt.cs`):

```csharp
public record CallAttempt           // ← record — immutable domain model
{
    public Guid Id { get; init; }
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
    public CallAttemptStatus Status { get; init; }   // ← typed enum
    public DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public string? TwilioCallSid { get; init; }
}
```

**EF entity** (`src/Infrastructure/Persistence/CallAttemptEntity.cs`):

```csharp
public class CallAttemptEntity      // ← class — mutable persistence model
{
    public Guid Id { get; set; }                    // ← set (mutable)
    public string PhoneNumber { get; set; } = "";
    public string AlarmMessage { get; set; } = "";
    public string Status { get; set; } = "";        // ← string (not enum!)
    public string? TwilioCallSid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
```

### Why two models?

EF Core needs **mutable** entities with `{ get; set; }` properties because it uses property setters to materialize objects from database rows and to track changes for `SaveChangesAsync()`. Records with `{ get; init; }` cannot be mutated after construction, so EF Core can't use them directly as tracked entities.

Additionally, the database stores `Status` as a `string` (e.g., `"Acknowledged"`), but the domain uses a typed `CallAttemptStatus` enum. The mapping layer handles this conversion.

This separation means:
- The **domain** stays pure — immutable records, typed enums, no EF concerns.
- The **infrastructure** handles persistence details — mutable classes, string statuses, table mappings.
- Neither layer leaks into the other.

### 4.2 The Mapping Methods

`src/Infrastructure/Persistence/Repositories/CallAttemptRepository.cs` contains two private static mapping methods:

**Entity → Domain:**

```csharp
private static CallAttempt MapToDomain(CallAttemptEntity entity)
{
    return new CallAttempt
    {
        Id = entity.Id,
        PhoneNumber = entity.PhoneNumber,
        AlarmMessage = entity.AlarmMessage,
        Status = Enum.Parse<CallAttemptStatus>(entity.Status),  // string → enum
        TwilioCallSid = entity.TwilioCallSid,
        CreatedAt = entity.CreatedAt,
        AcknowledgedAt = entity.AcknowledgedAt
    };
}
```

**Domain → Entity:**

```csharp
private static CallAttemptEntity MapToEntity(CallAttempt domain)
{
    return new CallAttemptEntity
    {
        Id = domain.Id,
        PhoneNumber = domain.PhoneNumber,
        AlarmMessage = domain.AlarmMessage,
        Status = domain.Status.ToString(),  // enum → string
        TwilioCallSid = domain.TwilioCallSid,
        CreatedAt = domain.CreatedAt,
        AcknowledgedAt = domain.AcknowledgedAt
    };
}
```

These are called at every boundary between the repository and the rest of the application. For example, in `CreateAsync`:

```csharp
public async Task<CallAttempt> CreateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default)
{
    var entity = MapToEntity(callAttempt);        // Domain → Entity (for EF)
    _dbContext.CallAttempts.Add(entity);
    await _dbContext.SaveChangesAsync(cancellationToken);
    return MapToDomain(entity);                    // Entity → Domain (for caller)
}
```

And in `UpdateAsync`, the entity is fetched from EF, mutated field-by-field, and saved:

```csharp
public async Task UpdateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default)
{
    var entity = await _dbContext.CallAttempts
        .FirstOrDefaultAsync(e => e.Id == callAttempt.Id, cancellationToken);

    if (entity is null)
    {
        throw new CallAttemptNotFoundException(callAttempt.Id);
    }

    entity.Status = callAttempt.Status.ToString();
    entity.AcknowledgedAt = callAttempt.AcknowledgedAt;
    entity.TwilioCallSid = callAttempt.TwilioCallSid;

    await _dbContext.SaveChangesAsync(cancellationToken);
}
```

### 4.3 Database Schema Configuration

`src/Infrastructure/Persistence/Configurations/CallAttemptConfiguration.cs` uses the Fluent API to define the table schema:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class CallAttemptConfiguration : IEntityTypeConfiguration<CallAttemptEntity>
{
    public void Configure(EntityTypeBuilder<CallAttemptEntity> builder)
    {
        builder.ToTable("call_attempts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.AlarmMessage)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.TwilioCallSid)
            .HasMaxLength(100);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.TwilioCallSid)
            .IsUnique()
            .HasFilter(null);
    }
}
```

Key points:

- **`ValueGeneratedNever()` on Id**: Since the domain generates its own GUIDs, the database must not auto-generate primary keys.
- **`.IsUnique().HasFilter(null)` on `TwilioCallSid`**: A unique index ensures no two call attempts share the same Twilio SID. The `HasFilter(null)` is needed because `TwilioCallSid` is nullable — without it, PostgreSQL would allow duplicate NULLs (which is fine), but EF Core's unique index on a nullable column needs an explicit filter to work correctly across providers.
- **Status stored as string (max 20 chars)**: The enum values (`"Pending"`, `"InProgress"`, etc.) are stored as text, not integers. This makes the database human-readable without joining to a lookup table.
- **Indexes on `Status` and `CreatedAt`**: Because the application queries by status and sorts by creation time.

### 4.4 The DbContext

`src/Infrastructure/Persistence/AlarmEscalationDbContext.cs`:

```csharp
using Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AlarmEscalationDbContext : DbContext
{
    public AlarmEscalationDbContext(DbContextOptions<AlarmEscalationDbContext> options)
        : base(options)
    {
    }

    public DbSet<CallAttemptEntity> CallAttempts => Set<CallAttemptEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CallAttemptConfiguration());
    }
}
```

The `ApplyConfiguration` call registers the `CallAttemptConfiguration` class. EF Core calls `Configure()` during model creation to build the table schema. This keeps the entity class clean — no data annotations, no `OnModelCreating` bloat.

### 4.5 EF Core Registration

In `src/Infrastructure/Startup/ServiceExtensions.cs`:

```csharp
services.AddDbContext<AlarmEscalationDbContext>(opts =>
    opts.UseNpgsql(connectionString));
```

`UseNpgsql` is the PostgreSQL provider for EF Core. The connection string comes from `appsettings.Development.json` → `ExternalServicesOptions` → this method. The DbContext is registered with a scoped lifetime (default for `AddDbContext`), meaning a new instance is created per HTTP request and disposed when the request ends.

---

## 5. How This Project Uses Async/Await

### 5.1 The Async Contract

Every method in the repository interface returns `Task` or `Task<T>`:

```csharp
public interface ICallAttemptRepository
{
    Task<CallAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CallAttempt?> GetByTwilioCallSidAsync(string callSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CallAttempt>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<CallAttempt> CreateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default);
    Task UpdateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default);
}
```

Every method in the orchestration service returns `Task` or `Task<T>`:

```csharp
public interface ICallOrchestrationService
{
    Task<CallAttempt> TriggerAlarmCallAsync(...);
    Task HandleGatherAsync(...);
    Task HandleStatusCallbackAsync(...);
}
```

### 5.2 The `CancellationToken` Pattern

Every async method accepts an optional `CancellationToken` parameter with `= default`. This is the standard .NET pattern for cooperative cancellation. When an HTTP request is aborted (client disconnects, timeout), ASP.NET signals the token, and all downstream operations can stop.

The token flows through the entire call chain:

```
Controller (gets token from ASP.NET)
  → OrchestrationService (passes token)
    → Repository (passes token to EF Core)
    → TwilioClient (passes token to HTTP call)
```

If the client cancels the `POST /api/test-alarm` request, the token propagates and EF Core's `SaveChangesAsync(cancellationToken)` will throw `OperationCanceledException`, which ASP.NET handles gracefully.

### 5.3 Async in the Repository

`src/Infrastructure/Persistence/Repositories/CallAttemptRepository.cs` — the async pattern in practice:

```csharp
public async Task<CallAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
{
    var entity = await _dbContext.CallAttempts                          // ← await suspends until DB returns
        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);       // ← token passed to EF

    return entity is null ? null : MapToDomain(entity);
}

public async Task<IReadOnlyList<CallAttempt>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
{
    var entities = await _dbContext.CallAttempts
        .OrderByDescending(e => e.CreatedAt)
        .Take(count)
        .ToListAsync(cancellationToken);                                // ← EF async method

    return entities.Select(MapToDomain).ToList();
}

public async Task<CallAttempt> CreateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default)
{
    var entity = MapToEntity(callAttempt);
    _dbContext.CallAttempts.Add(entity);                                 // ← synchronous (in-memory)
    await _dbContext.SaveChangesAsync(cancellationToken);                // ← async (database I/O)
    return MapToDomain(entity);
}

public async Task UpdateAsync(CallAttempt callAttempt, CancellationToken cancellationToken = default)
{
    var entity = await _dbContext.CallAttempts
        .FirstOrDefaultAsync(e => e.Id == callAttempt.Id, cancellationToken);

    if (entity is null)
    {
        throw new CallAttemptNotFoundException(callAttempt.Id);
    }

    entity.Status = callAttempt.Status.ToString();                       // ← synchronous (in-memory)
    entity.AcknowledgedAt = callAttempt.AcknowledgedAt;
    entity.TwilioCallSid = callAttempt.TwilioCallSid;

    await _dbContext.SaveChangesAsync(cancellationToken);
}
```

Notice the pattern: **in-memory operations are synchronous, I/O operations are async**. `_dbContext.CallAttempts.Add(entity)` modifies the in-memory change tracker — it doesn't touch the database. `SaveChangesAsync` does the actual SQL `INSERT`. This distinction matters because synchronous code on hot paths is faster (no state machine overhead), while async code prevents thread blocking during I/O.

### 5.4 Async in the Twilio Client

`src/ExternalServices/Twilio/TwilioClient.cs` — the HTTP call:

```csharp
var response = await flurlClient.Request(url)
    .WithHeader("Authorization", $"Basic {basicAuth}")
    .PostUrlEncodedAsync(new
    {
        To = toPhoneNumber,
        From = fromPhoneNumber,
        Url = $"{webhookBaseUrl}/api/twilio/voice"
    }, cancellationToken: cancellationToken)
    .ReceiveJson<TwilioCallResponse>();
```

This single `await` does all of the following:
1. Opens an HTTP connection to Twilio's API (network I/O)
2. Sends the POST body as URL-encoded form data
3. Waits for the response (potentially hundreds of milliseconds)
4. Deserializes the JSON response into a `TwilioCallResponse` object
5. Returns the `Sid` string

During the wait, the thread is released back to the ASP.NET thread pool to handle other requests.

### 5.5 The Dummy Implementation — Sync-to-Async

`src/ExternalServices/Twilio/DummyTwilioClient.cs` implements the async interface but does no I/O:

```csharp
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
```

This method is **not** marked `async`. It doesn't need to be — there's no I/O to await. It synchronously generates a GUID, logs it, and wraps the result in a completed `Task<string>` using `Task.FromResult()`. This is more efficient than marking it `async` and using `return dummyCallSid;` (which would generate an unnecessary state machine).

However, it still accepts `CancellationToken` — even though it doesn't use it, it satisfies the interface contract. If a future version of this dummy wanted to simulate delay (`await Task.Delay(1000, cancellationToken)`), the token would already be wired in.

### 5.6 The Full Async Chain in the Orchestration Service

`src/ExternalServices/Services/CallOrchestrationService.cs` — `TriggerAlarmCallAsync`:

```csharp
public async Task<CallAttempt> TriggerAlarmCallAsync(
    string phoneNumber,
    string alarmMessage,
    CancellationToken cancellationToken = default)
{
    // 1. Create domain object (sync, in-memory)
    var callAttempt = new CallAttempt { ... };

    // 2. Save to database (async I/O)
    callAttempt = await _repository.CreateAsync(callAttempt, cancellationToken);

    // 3. Log (sync, but logger is thread-safe)
    _logger.LogInformation(...);

    try
    {
        // 4. Call Twilio (async I/O — HTTP request)
        var callSid = await _twilioClient.InitiateCallAsync(...);

        // 5. Update status with `with` expression (sync, in-memory)
        callAttempt = callAttempt with { Status = CallAttemptStatus.InProgress, TwilioCallSid = callSid };

        // 6. Save to database (async I/O)
        await _repository.UpdateAsync(callAttempt, cancellationToken);
    }
    catch (Exception ex)
    {
        // 7. On failure: update status to Failed, save again
        _logger.LogError(ex, ...);
        callAttempt = callAttempt with { Status = CallAttemptStatus.Failed };
        await _repository.UpdateAsync(callAttempt, cancellationToken);
    }

    return callAttempt;
}
```

The flow: **create → save → await HTTP → update state → save → return**. Each `await` gives control back to the runtime so the thread can handle other requests.

---

## 6. How This Project Handles Configuration

### 6.1 The Options Classes

`src/Common/Options/ExternalServicesOptions.cs`:

```csharp
namespace Common.Options;

public class ExternalServicesOptions
{
    public TwilioOptions? Twilio { get; init; }
    public DatabaseOptions? Database { get; init; }
}
```

`src/Common/Options/TwilioOptions.cs`:

```csharp
namespace Common.Options;

public class TwilioOptions
{
    public string AccountSid { get; init; } = "";
    public string AuthToken { get; init; } = "";
    public string FromPhoneNumber { get; init; } = "";
    public bool? UseDummyClient { get; init; }
    public string? BaseWebhookUrl { get; init; }
}
```

`src/Common/Options/DatabaseOptions.cs`:

```csharp
namespace Common.Options;

public class DatabaseOptions
{
    public string ConnectionString { get; init; } = "";
}
```

### 6.2 The JSON Configuration

`src/Web/appsettings.Development.json`:

```json
{
  "ExternalServices": {
    "Twilio": {
      "AccountSid": "",
      "AuthToken": "",
      "FromPhoneNumber": "",
      "UseDummyClient": true,
      "BaseWebhookUrl": "https://localhost:5001"
    },
    "Database": {
      "ConnectionString": "Host=localhost;Port=5432;Database=alarmescalation;Username=alarmuser;Password=alarmpassword"
    }
  }
}
```

The JSON key names match the C# property names exactly. ASP.NET's configuration system binds by convention: `ExternalServices:Twilio:AccountSid` maps to `ExternalServicesOptions.Twilio.AccountSid`.

### 6.3 How Options Are Bound in `Program.cs`

Two different binding approaches are used:

**Approach 1 — Direct deserialization** (for values needed during DI setup):

```csharp
var externalServicesOptions = builder.Configuration
    .GetSection("ExternalServices")
    .Get<ExternalServicesOptions>();
```

This immediately creates an `ExternalServicesOptions` object with all values populated. It's needed because `AddExternalServices` and `AddInfrastructure` need the values *right now* to decide which implementations to register. For example, `options.Twilio?.UseDummyClient == true` determines whether `DummyTwilioClient` or `TwilioClient` is registered.

**Approach 2 — Options pattern** (for values needed at runtime):

```csharp
builder.Services.Configure<TwilioOptions>(
    builder.Configuration.GetSection("ExternalServices:Twilio"));
```

This registers `TwilioOptions` in the DI container. Any class can then inject `IOptions<TwilioOptions>` and get the values. This is used for `CallOrchestrationService`, which needs `AccountSid`, `AuthToken`, `FromPhoneNumber`, and `BaseWebhookUrl` at runtime.

### 6.4 Consuming Options via `IOptions<T>`

In `CallOrchestrationService.cs`:

```csharp
public CallOrchestrationService(
    ICallAttemptRepository repository,
    ITwilioClient twilioClient,
    IOptions<TwilioOptions> twilioOptions,    // ← inject the wrapper
    ILogger<CallOrchestrationService> logger)
{
    _repository = repository;
    _twilioClient = twilioClient;
    _twilioOptions = twilioOptions.Value;       // ← unwrap to get the actual options
    _logger = logger;
}
```

Then used at runtime:

```csharp
var webhookBaseUrl = _twilioOptions.BaseWebhookUrl ?? "";
var fromPhoneNumber = _twilioOptions.FromPhoneNumber;
```

`IOptions<T>.Value` always returns the current configuration values. For scenarios where configuration can change at runtime (not this project), `IOptionsSnapshot<T>` or `IOptionsMonitor<T>` would be used instead.

### 6.5 Environment-Specific Configuration

ASP.NET automatically loads `appsettings.json` first, then overlays `appsettings.{Environment}.json`. In development, `appsettings.Development.json` overrides values from `appsettings.json`. Environment variables override both.

This means:
- `appsettings.json` has structural defaults (empty strings, `UseDummyClient: true` as safe default)
- `appsettings.Development.json` has local development values (localhost URLs, local database)
- A hypothetical `appsettings.Production.json` would have real Twilio credentials and `UseDummyClient: false`
- Secrets like `AccountSid` and `AuthToken` can be set via environment variables without modifying files

---

## 7. How This Project Structures API Responses

### 7.1 Request Models (What Comes In)

`src/Web/ViewModels/TriggerAlarmRequest.cs`:

```csharp
namespace Web.ViewModels;

public record TriggerAlarmRequest
{
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
}
```

This is what the client sends to `POST /api/test-alarm`:

```json
{
    "phoneNumber": "+1234567890",
    "alarmMessage": "Fire detected in Building A"
}
```

ASP.NET automatically deserializes the JSON body into a `TriggerAlarmRequest` object because of the `[FromBody]` attribute on the controller action. The `required` modifier makes missing values visible in C# code. For incoming HTTP requests, ASP.NET model binding and validation handle missing fields.

### 7.2 View Models (What Goes Out)

`src/Web/ViewModels/CallAttemptViewModel.cs`:

```csharp
using Domain.CallAttempts.Models;

namespace Web.ViewModels;

public record CallAttemptViewModel
{
    public Guid Id { get; init; }
    public required string PhoneNumber { get; init; }
    public required string AlarmMessage { get; init; }
    public required string Status { get; init; }    // ← String, not CallAttemptStatus enum
    public DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public string? TwilioCallSid { get; init; }
}
```

Notice the `Status` property is `string`, not `CallAttemptStatus`. The domain uses the typed enum internally, but the API returns a string representation (e.g., `"Acknowledged"`) for readability. The `JsonStringEnumConverter` registered in `Program.cs` would also serialize enums as strings, but using a separate view model with a string property is more explicit and decouples the API contract from the domain's internal representation.

### 7.3 The Extension Method Mapper

`src/Web/Mappers/CallAttemptMapper.cs`:

```csharp
using Domain.CallAttempts.Models;
using Web.ViewModels;

namespace Web.Mappers;

public static class CallAttemptMapper
{
    public static CallAttemptViewModel ToViewModel(this CallAttempt callAttempt)
    {
        return new CallAttemptViewModel
        {
            Id = callAttempt.Id,
            PhoneNumber = callAttempt.PhoneNumber,
            AlarmMessage = callAttempt.AlarmMessage,
            Status = callAttempt.Status.ToString(),     // Enum → String
            CreatedAt = callAttempt.CreatedAt,
            AcknowledgedAt = callAttempt.AcknowledgedAt,
            TwilioCallSid = callAttempt.TwilioCallSid
        };
    }
}
```

This is an **extension method**. The `this CallAttempt callAttempt` parameter means you can call it as if it were an instance method on `CallAttempt`:

```csharp
CallAttempt domainObject = ...;
CallAttemptViewModel vm = domainObject.ToViewModel();  // Looks like an instance method
```

Without the extension method pattern, you'd write:

```csharp
CallAttemptViewModel vm = CallAttemptMapper.ToViewModel(domainObject);  // Less fluent
```

Extension methods are used here (rather than putting `ToViewModel()` directly on `CallAttempt`) because the domain layer shouldn't know about view models — that would create a dependency from `Domain` → `Web`, which is backwards. The extension method lives in the `Web` project, which already depends on `Domain`.

### 7.4 Controllers in Action

`src/Web/Controllers/TestAlarmController.cs`:

```csharp
[ApiController]
public class TestAlarmController : ControllerBase
{
    private readonly ICallOrchestrationService _orchestrationService;

    public TestAlarmController(ICallOrchestrationService orchestrationService)
    {
        _orchestrationService = orchestrationService;
    }

    [HttpPost]
    [Route("api/test-alarm")]
    public async Task<ActionResult<CallAttemptViewModel>> TriggerAlarm(
        [FromBody] TriggerAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var callAttempt = await _orchestrationService.TriggerAlarmCallAsync(
            request.PhoneNumber,
            request.AlarmMessage,
            cancellationToken);

        return Ok(callAttempt.ToViewModel());
    }
}
```

The `[ApiController]` attribute enables automatic model validation, binding source inference, and problem details responses. `ActionResult<CallAttemptViewModel>` tells Swagger what the response type is while still allowing the method to return different HTTP status codes.

`src/Web/Controllers/CallAttemptsController.cs`:

```csharp
[HttpGet]
[Route("api/call-attempts")]
public async Task<ActionResult<IReadOnlyList<CallAttemptViewModel>>> GetRecent(
    [FromQuery] int count = 20,
    CancellationToken cancellationToken = default)
{
    var attempts = await _repository.GetRecentAsync(count, cancellationToken);
    return Ok(attempts.Select(a => a.ToViewModel()).ToList());
}

[HttpGet]
[Route("api/call-attempts/{id:guid}")]
public async Task<ActionResult<CallAttemptViewModel>> GetById(
    Guid id,
    CancellationToken cancellationToken)
{
    var attempt = await _repository.GetByIdAsync(id, cancellationToken);

    if (attempt is null)
    {
        return NotFound();
    }

    return Ok(attempt.ToViewModel());
}
```

The route constraint `{id:guid}` ensures only valid GUIDs reach the action — ASP.NET returns 404 for non-GUID strings without even invoking the method.

### 7.5 The Exception Filter — Domain Errors → HTTP Responses

`src/Web/Filters/ApiExceptionsFilter.cs`:

```csharp
using System.Net;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Web.Filters;

public class ApiExceptionsFilter : IActionFilter, IOrderedFilter
{
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public ApiExceptionsFilter(ProblemDetailsFactory problemDetailsFactory)
    {
        _problemDetailsFactory = problemDetailsFactory;
    }

    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        switch (context.Exception)
        {
            case CallAttemptNotFoundException:
                MakeProblemDetailsResponse(context, (int)HttpStatusCode.NotFound, "The call attempt was not found");
                context.ExceptionHandled = true;
                return;

            case InvalidOperationException:
                MakeProblemDetailsResponse(context, (int)HttpStatusCode.InternalServerError, "An unexpected error occurred");
                context.ExceptionHandled = true;
                return;
        }
    }

    private void MakeProblemDetailsResponse(ActionExecutedContext context, int? statusCode = null, string? title = null)
    {
        var problemDetails = _problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            statusCode: statusCode,
            title: title
        );
        context.Result = new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };
    }

    public int Order => int.MaxValue - 10;
}
```

This filter runs **after** every controller action. If an unhandled exception was thrown, it maps the exception type to an HTTP status code and creates an RFC 7807 Problem Details response:

- `CallAttemptNotFoundException` → **404 Not Found** with title "The call attempt was not found"
- `InvalidOperationException` → **500 Internal Server Error** with title "An unexpected error occurred"

Without this filter, an unhandled `CallAttemptNotFoundException` would bubble up to ASP.NET's default exception handler and return a generic 500 error — losing the semantic meaning that the resource wasn't found.

The `ProblemDetailsFactory` creates a standardized error response body like:

```json
{
    "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
    "title": "The call attempt was not found",
    "status": 404
}
```

---

## 8. End-to-End Flow: Trace a Request

Let's follow a single `POST /api/test-alarm` request through every layer of the application.

### Step 1: HTTP Request Arrives

```
POST /api/test-alarm HTTP/1.1
Content-Type: application/json

{
    "phoneNumber": "+1234567890",
    "alarmMessage": "Fire detected in Building A"
}
```

### Step 2: ASP.NET Routing

The request matches `[HttpPost]` `[Route("api/test-alarm")]` on `TestAlarmController.TriggerAlarm`. ASP.NET:
- Deserializes the JSON body into a `TriggerAlarmRequest` record
- Validates that `PhoneNumber` and `AlarmMessage` are present (model binding + validation)
- Extracts a `CancellationToken` from the HTTP context
- Creates an instance of `TestAlarmController` via DI

### Step 3: Controller → Orchestration Service

```csharp
// TestAlarmController.cs, line 24
var callAttempt = await _orchestrationService.TriggerAlarmCallAsync(
    request.PhoneNumber,      // "+1234567890"
    request.AlarmMessage,     // "Fire detected in Building A"
    cancellationToken);
```

DI resolves `_orchestrationService` as `CallOrchestrationService` (registered as `ICallOrchestrationService`).

### Step 4: Orchestration Service — Create Domain Object

```csharp
// CallOrchestrationService.cs, lines 34-41
var callAttempt = new CallAttempt
{
    Id = Guid.NewGuid(),                              // e.g., "a1b2c3d4-..."
    PhoneNumber = phoneNumber,                        // "+1234567890"
    AlarmMessage = alarmMessage,                      // "Fire detected in Building A"
    Status = CallAttemptStatus.Pending,
    CreatedAt = DateTime.UtcNow
};
```

A new immutable record is created in memory. No I/O yet.

### Step 5: Orchestration Service → Repository → Database

```csharp
// CallOrchestrationService.cs, line 43
callAttempt = await _repository.CreateAsync(callAttempt, cancellationToken);
```

Inside `CallAttemptRepository.CreateAsync`:
1. `MapToEntity(callAttempt)` converts the domain record to a mutable `CallAttemptEntity` (converting `Status` enum to `"Pending"` string)
2. `_dbContext.CallAttempts.Add(entity)` adds it to EF's change tracker
3. `await _dbContext.SaveChangesAsync(cancellationToken)` executes `INSERT INTO call_attempts (...)`
4. `MapToDomain(entity)` converts back to a domain record and returns it

### Step 6: Orchestration Service → Twilio Client → Twilio API

```csharp
// CallOrchestrationService.cs, lines 54-58
var callSid = await _twilioClient.InitiateCallAsync(
    phoneNumber,            // "+1234567890"
    fromPhoneNumber,        // from config: e.g., "+15551234567"
    webhookBaseUrl,         // from config: e.g., "https://localhost:5001"
    cancellationToken);
```

If `UseDummyClient` is `true` (development), `DummyTwilioClient.InitiateCallAsync` runs synchronously and returns `"DUMMY_a1b2c3d4e5f6..."`.

If `UseDummyClient` is `false` (production), `TwilioClient.InitiateCallAsync` makes a real HTTP POST to `https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Calls.json` with Twilio credentials and call parameters. Twilio responds with JSON containing the call SID.

### Step 7: Orchestration Service — Update State with `with`

```csharp
// CallOrchestrationService.cs, lines 60-64
callAttempt = callAttempt with
{
    Status = CallAttemptStatus.InProgress,
    TwilioCallSid = callSid     // "DUMMY_a1b2c3d4e5f6..." or real Twilio SID
};
```

A new `CallAttempt` record is created with `Status = InProgress` and the Twilio SID populated. The original `Pending` record is discarded.

### Step 8: Orchestration Service → Repository → Database (Update)

```csharp
// CallOrchestrationService.cs, line 66
await _repository.UpdateAsync(callAttempt, cancellationToken);
```

Inside `CallAttemptRepository.UpdateAsync`:
1. EF fetches the existing entity by `Id`
2. The entity's `Status`, `AcknowledgedAt`, and `TwilioCallSid` fields are updated in-place
3. `SaveChangesAsync` executes `UPDATE call_attempts SET status = 'InProgress', twilio_call_sid = '...' WHERE id = '...'`

### Step 9: Back to Controller → HTTP Response

```csharp
// TestAlarmController.cs, line 29
return Ok(callAttempt.ToViewModel());
```

`ToViewModel()` converts the domain record to a `CallAttemptViewModel` (enum `Status` → string `"InProgress"`). ASP.NET serializes it as JSON:

```json
HTTP/1.1 200 OK
Content-Type: application/json

{
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "phoneNumber": "+1234567890",
    "alarmMessage": "Fire detected in Building A",
    "status": "InProgress",
    "createdAt": "2026-05-01T10:30:00Z",
    "acknowledgedAt": null,
    "twilioCallSid": "DUMMY_a1b2c3d4e5f67890abcdef12345678"
}
```

### The Rest of the Flow: Twilio Callbacks

After the initial request returns, Twilio processes the call asynchronously and hits the webhook endpoints:

**1. Twilio calls `POST /api/twilio/voice`** when the recipient answers:
```
TwilioWebhookController.HandleVoice(CallSid)
  → Returns TwiML XML telling Twilio to <Say> the alarm message and <Gather> digits
```

**2. Twilio calls `POST /api/twilio/gather`** when the recipient presses a key:
```
TwilioWebhookController.HandleGather(CallSid, Digits)
  → _orchestrationService.HandleGatherAsync(CallSid, Digits)
    → If Digits == "1":
      → Look up CallAttempt by TwilioCallSid
      → callAttempt with { Status = Acknowledged, AcknowledgedAt = UtcNow }
      → _repository.UpdateAsync(callAttempt)
  → Returns TwiML XML saying "The alarm has been acknowledged"
```

**3. Twilio calls `POST /api/twilio/status`** with the final call status:
```
TwilioWebhookController.HandleStatusCallback(CallSid, CallStatus)
  → _orchestrationService.HandleStatusCallbackAsync(CallSid, CallStatus)
    → Look up CallAttempt by TwilioCallSid
    → Map CallStatus string to CallAttemptStatus via switch expression
    → If status changed: callAttempt with { Status = newStatus }, then update
```

### Error Path: When Twilio Fails

If `_twilioClient.InitiateCallAsync` throws (network error, invalid credentials, etc.), the catch block in `TriggerAlarmCallAsync` executes:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to initiate Twilio call for CallAttemptId={CallAttemptId}", callAttempt.Id);

    callAttempt = callAttempt with { Status = CallAttemptStatus.Failed };
    await _repository.UpdateAsync(callAttempt, cancellationToken);
}
```

The call attempt is marked `Failed` and persisted. The controller still returns a 200 OK with the failed call attempt — the API consumer can check `status` to see it failed.

### Error Path: When a Call Attempt Is Not Found

If `GetByTwilioCallSidAsync` returns null (e.g., Twilio sends a status callback for an unknown SID), the private `FindByCallSidAsync` helper throws:

```csharp
throw new Domain.Exceptions.CallAttemptNotFoundException(Guid.Empty);
```

This propagates up through `HandleStatusCallbackAsync` → `TwilioWebhookController.HandleStatusCallback` → `ApiExceptionsFilter.OnActionExecuted`, which catches it and returns:

```json
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{
    "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
    "title": "The call attempt was not found",
    "status": 404
}
```

---

## Summary: The Architecture Patterns at a Glance

| Pattern | What it looks like in this project | Why it was chosen |
|---|---|---|
| **Records with `init`** | `public record CallAttempt { ... get; init; }` | Immutable domain models prevent accidental mutation |
| **`with` expressions** | `callAttempt with { Status = InProgress }` | Explicit state transitions with compiler enforcement |
| **`required` modifier** | `public required string PhoneNumber` | Compile-time guarantee that critical fields are set |
| **Interfaces** | `ICallAttemptRepository`, `ITwilioClient` | Decouple consumers from implementations; enable testing |
| **Multi-implementation interface** | `ITwilioClient` → `TwilioClient` / `DummyTwilioClient` | Switch between real and fake without changing consuming code |
| **Constructor injection** | `CallOrchestrationService(ICallAttemptRepository, ITwilioClient, ...)` | Dependencies are explicit and resolved by the container |
| **`ServiceExtensions` pattern** | `public static void AddInfrastructure(this IServiceCollection, ...)` | Keep `Program.cs` clean; co-locate registration with implementations |
| **Dual model (Domain + Entity)** | `CallAttempt` (record) + `CallAttemptEntity` (class) | Domain purity vs. EF Core mutability requirements |
| **Async all the way** | `Task<T>` on every interface method | Non-blocking I/O for database and HTTP calls |
| **`CancellationToken`** | Every async method accepts `= default` | Cooperative cancellation from HTTP request through to I/O |
| **Options pattern** | `IOptions<TwilioOptions>` + `Configure<T>` | Type-safe, validated configuration from JSON/environment |
| **Environment-aware registration** | `if (options.Twilio?.UseDummyClient == true)` | Same codebase runs differently in dev vs. prod |
| **Extension method mappers** | `callAttempt.ToViewModel()` | Fluent mapping without coupling domain to web layer |
| **Exception filter** | `ApiExceptionsFilter : IActionFilter` | Centralized domain-to-HTTP error mapping |
| **Enum-as-string serialization** | `JsonStringEnumConverter` | Human-readable API responses |
| **Fluent EF configuration** | `CallAttemptConfiguration : IEntityTypeConfiguration<T>` | Schema definition outside entity classes |

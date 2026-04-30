# Alarm Escalation POC

## Overview

This repository contains a proof of concept (POC) for an **alarm escalation system** using outbound voice calls.

The goal is to validate that a backend system can:

- Trigger phone calls when an alarm occurs
- Deliver a voice message to the receiver
- Allow the receiver to acknowledge the alarm via keypad input
- Persist and track the result of the call

This POC focuses on the **core interaction flow**, not full production behavior.

---

## What This POC Does

This service implements a minimal end-to-end alarm call flow:

```text
Trigger alarm
→ Create call attempt
→ Call phone via Twilio
→ Play message
→ Receiver presses 1
→ Mark alarm as acknowledged
```

### Features

- Trigger a simulated alarm via API
- Outbound phone call using Twilio
- Voice message playback
- Keypad interaction (press 1 to acknowledge)
- Persistence of call attempts in PostgreSQL
- Tracking of call status and acknowledgement
- Basic logging of call lifecycle

---

## Tech Stack

- .NET 9.0 / ASP.NET Core
- PostgreSQL
- Entity Framework Core
- Twilio Voice API
- Docker Compose (for local database)
- Serilog (structured logging)
- Swashbuckle (Swagger)

---

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Docker Desktop

### 1. Start PostgreSQL

```bash
docker compose up -d
```

### 2. Configure settings

Edit `src/Web/appsettings.Development.json` with your Twilio credentials and database connection string.

### 3. Run the API

```bash
dotnet run --project src/Web/Web.csproj
```

### 4. Expose local server (required for Twilio webhooks)

```bash
ngrok http 5000
```

Copy the HTTPS URL and configure it as your webhook base URL.

---

## API Endpoints

### Trigger test alarm

```http
POST /api/test-alarm
```

Example body:

```json
{
  "phoneNumber": "+47xxxxxxxx",
  "alarmMessage": "Freezer alarm in Store X"
}
```

### Twilio webhooks

```
POST /api/twilio/voice     → returns voice message + gather input
POST /api/twilio/gather    → handles keypad input
POST /api/twilio/status    → receives call status updates
```

### Retrieve call attempts

```http
GET /api/call-attempts
GET /api/call-attempts/{id}
```

---

## Architecture

This project uses a layered .NET service architecture:

```
alarm-escalation-poc/
├── AlarmEscalation.sln
├── docker-compose.yml
├── src/
│   ├── Web/              → ASP.NET Core API (controllers, filters, startup)
│   ├── Domain/           → Domain models, interfaces, exceptions
│   ├── Infrastructure/   → EF Core DbContext, repositories
│   ├── ExternalServices/ → Twilio integration, call orchestration
│   └── Common/           → Shared options/configuration classes
└── tests/                → Test projects (future)
```

**Key patterns:**

- Strongly-typed Options classes in Common/Options
- Domain models as C# records with init-only properties
- Repository pattern with EF Core + PostgreSQL
- Environment-aware service registration (real vs dummy clients)
- Scrutor for DI decoration
- Serilog with JSON formatter for non-dev environments
- Custom exception filter for consistent error handling

---

## Data Model

### CallAttempt

```
Id                  GUID
PhoneNumber         string
AlarmMessage        string
Status              enum: Pending, InProgress, Acknowledged, Failed, NoAnswer
TwilioCallSid       string (nullable)
CreatedAt           DateTime
AcknowledgedAt      DateTime (nullable)
```

### AlarmEvent

```
Id                  GUID
AlarmSource         string
AlarmType           string
Message             string
OccurredAt          DateTime
CallAttemptId       GUID (nullable)
```

---

## Call Flow

```
POST /api/test-alarm
  → store CallAttempt
  → trigger Twilio call

Twilio → /api/twilio/voice
  → play message
  → ask for input

User presses 1

Twilio → /api/twilio/gather
  → mark CallAttempt as acknowledged

Twilio → /api/twilio/status
  → update call status
```

---

## Limitations (Important)

This is a **POC**, not a production-ready system.

It does NOT include:

- Escalation chains (multiple contacts)
- Retry logic
- SMS fallback
- On-call schedules
- Multi-region routing
- Alarm deduplication
- Monitoring and alerting
- Access control / security hardening

---

## Next Steps

To move toward a production system:

- Support multiple contacts per alarm
- Add escalation and retry logic
- Add SMS fallback
- Introduce scheduling (on-call / time-based routing)
- Add monitoring and alerting
- Improve robustness and error handling

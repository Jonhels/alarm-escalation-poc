# Alarm Escalation POC

## Overview

This repository contains a proof of concept (POC) for an **alarm escalation system** using outbound voice calls.

The goal is to validate that a backend system can:

* Trigger phone calls when an alarm occurs
* Deliver a voice message to the receiver
* Allow the receiver to acknowledge the alarm via keypad input
* Persist and track the result of the call

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

* Trigger a simulated alarm via API
* Outbound phone call using Twilio
* Voice message playback
* Keypad interaction (press 1 to acknowledge)
* Persistence of call attempts in PostgreSQL
* Tracking of call status and acknowledgement
* Basic logging of call lifecycle

---

## Tech Stack

* .NET 8 / ASP.NET Core (Minimal API)
* PostgreSQL
* Entity Framework Core
* Twilio Voice API
* Docker Compose (for local database)
* ngrok (for exposing webhooks locally)

---

## Getting Started

### 1. Start PostgreSQL

```bash
docker compose up -d
```

---

### 2. Run the API

```bash
dotnet run
```

---

### 3. Expose local server (required for Twilio)

```bash
ngrok http 5000
```

Copy the HTTPS URL and configure it as your webhook base URL.

---

### 4. Configure environment variables

```bash
TWILIO_ACCOUNT_SID=
TWILIO_AUTH_TOKEN=
TWILIO_FROM_NUMBER=
BASE_WEBHOOK_URL=https://your-ngrok-url
```

---

## API Endpoints

### Trigger test alarm

```http
POST /test-alarm
```

Example body:

```json
{
  "phoneNumber": "+47xxxxxxxx",
  "message": "Freezer alarm in store X"
}
```

---

### Twilio webhooks

```text
POST /twilio/voice     → returns voice message + gather input
POST /twilio/gather    → handles keypad input
POST /twilio/status    → receives call status updates
```

---

### Retrieve call attempts

```http
GET /call-attempts
GET /call-attempts/{id}
```

---

## Data Model

### CallAttempt

```text
Id
AlarmReference
PhoneNumber
Status
AttemptNumber
TwilioCallSid
CreatedAt
UpdatedAt
AcknowledgedAt
```

---

## Call Flow

```text
POST /test-alarm
  → store CallAttempt
  → trigger Twilio call

Twilio → /twilio/voice
  → play message
  → ask for input

User presses 1

Twilio → /twilio/gather
  → mark CallAttempt as acknowledged

Twilio → /twilio/status
  → update call status
```

---

## What This POC Proves

* Outbound alarm calls are technically feasible
* Twilio integration works with a backend-driven flow
* Acknowledgement via keypad input is reliable
* Call state can be persisted and tracked

---

## Limitations (Important)

This is a **POC**, not a production-ready system.

It does NOT include:

* Escalation chains (multiple contacts)
* Retry logic
* SMS fallback
* On-call schedules
* Multi-region routing
* Alarm deduplication
* Monitoring and alerting
* Access control / security hardening

---

## Next Steps

To move toward a production system:

* Support multiple contacts per alarm
* Add escalation and retry logic
* Add SMS fallback
* Introduce scheduling (on-call / time-based routing)
* Add monitoring and alerting
* Improve robustness and error handling

---

## Architecture Note

This POC follows the principle:

> The backend owns the logic. External services (Twilio) are delivery mechanisms.

Future versions could evolve toward:

```text
Alarm → Escalation Engine → Call Orchestrator → Twilio → Webhooks → State updates
```

---

## Purpose

This POC was created to:

* Validate feasibility of automated alarm calling
* Understand integration complexity
* Identify the main challenges for a production-ready system

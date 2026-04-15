# FanPad Service Health Monitor

Agentic service health monitoring system for Fanpad's messaging infrastructure.
Monitors Twilio (SMS), Mailgun (email), and AWS SES (email fallback) using an
AI agent powered by Claude. Ensures campaigns launch only through healthy channels
with human-in-the-loop failover approval.

**Stack:** .NET 8 (C#) · Angular 17 · PostgreSQL · SignalR · Anthropic SDK

---

## Architecture at a Glance

```
Angular Dashboard  ←──SignalR──→  .NET API  →  Claude Agent  →  Health Probes
                                      ↕                               ↕
                                  PostgreSQL              External Status Pages
```

See [docs/architecture.md](docs/architecture.md) for full design documentation.

---

## Quick Start

### Prerequisites
- Docker + Docker Compose
- .NET 8 SDK (for local development without Docker)
- Node.js 20+ and Angular CLI 17 (for frontend without Docker)
- Anthropic API key

### 1. Configure API Key

```bash
# Copy and edit environment file
cp .env.example .env
# Set ANTHROPIC_API_KEY=your_key_here
```

Or edit [backend/src/FanPad.ServiceMonitor.Api/appsettings.json](backend/src/FanPad.ServiceMonitor.Api/appsettings.json):
```json
"Anthropic": { "ApiKey": "your_key_here" }
```

### 2. Start with Docker Compose

```bash
docker-compose up -d
```

Services start at:
- **Frontend:** http://localhost:4200
- **Backend API:** http://localhost:5000
- **Swagger UI:** http://localhost:5000/swagger
- **PostgreSQL:** localhost:5432

### 3. Manual Setup (without Docker)

**Database:**
```bash
docker run -d -e POSTGRES_USER=fanpad -e POSTGRES_PASSWORD=fanpad_dev \
  -e POSTGRES_DB=fanpad_health -p 5432:5432 postgres:16-alpine

psql -U fanpad -d fanpad_health -f db/migrations/001_initial_schema.sql
psql -U fanpad -d fanpad_health -f db/migrations/002_seed_data.sql
```

**Backend:**
```bash
cd backend/src/FanPad.ServiceMonitor.Api
dotnet run
```

**Frontend:**
```bash
cd frontend/fanpad-dashboard
npm install
npm start
```

---

## Project Structure

```
fanpad/
├── docs/
│   ├── architecture.md      # Full system design
│   ├── stories.md           # Agile user stories (5 epics)
│   ├── test-plan.md         # Comprehensive test plan
│   └── runbook.md           # Operator runbook
├── db/
│   └── migrations/
│       ├── 001_initial_schema.sql
│       └── 002_seed_data.sql
├── backend/
│   ├── src/
│   │   ├── FanPad.ServiceMonitor.Core/          # Models, interfaces, enums
│   │   ├── FanPad.ServiceMonitor.Infrastructure/ # Probes, agent, routing, data
│   │   └── FanPad.ServiceMonitor.Api/           # Controllers, SignalR, background service
│   └── tests/
│       └── FanPad.ServiceMonitor.Tests/         # xUnit test suite
├── frontend/
│   └── fanpad-dashboard/                        # Angular 17 standalone components
├── docker-compose.yml
└── README.md
```

---

## Key Features

### Agent-Powered Health Evaluation
- Claude Sonnet 4.6 with 11 specialized tools
- Multi-signal reasoning: external status pages + internal synthetic probes + history
- Adaptive polling: 2-minute normal, 30-second during degradation
- Full reasoning log stored to PostgreSQL

### Failover with Human Approval
- Agent recommends, human approves — never autonomous
- 30-minute approval window with expiry
- Per-channel failover (email and SMS fail independently)
- Full audit trail of all failover events

### Campaign Gate
- Pre-launch gate check via agent evaluation
- Per-channel gating (email held doesn't block SMS)
- Agent-generated work plan explains hold reason

### Comprehensive Failure Simulation (15 Scenarios)
For demo and testing — no real external calls required:

| Category | Scenarios |
|---|---|
| Email | Mailgun Complete Outage, Partial Degradation, High Latency, High Error Rate |
| Email | SES Complete Outage, SES Partial Degradation, Both Email Providers Down |
| SMS | Twilio Complete Outage, Partial Degradation, High Latency |
| Complex | Mailgun→SES Cascade Failure, Intermittent Errors, All Services Down |
| Recovery | Mailgun Recovering, Twilio Recovering |

---

## API Reference

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/health/summary` | Current health status all services |
| POST | `/api/health/evaluate` | Trigger manual agent evaluation |
| GET | `/api/health/history` | Health check results (configurable window) |
| GET | `/api/health/decisions` | Agent decision log |
| GET | `/api/incidents` | List incidents (filter by status) |
| GET | `/api/routing` | Current routing state |
| GET | `/api/routing/approvals` | Pending failover approvals |
| POST | `/api/routing/approvals/{id}/approve` | Approve a failover |
| POST | `/api/routing/approvals/{id}/reject` | Reject a failover |
| POST | `/api/routing/revert/{serviceType}` | Revert to primary provider |
| GET | `/api/campaigns` | List campaigns with gate status |
| POST | `/api/campaigns/{id}/gate-check` | Run agent gate check |
| POST | `/api/campaigns/{id}/release` | Manually release held campaign |
| GET | `/api/simulation/scenarios` | List all failure scenarios |
| POST | `/api/simulation/activate` | Activate a failure scenario |
| POST | `/api/simulation/clear` | Clear active simulation |

Full OpenAPI spec: http://localhost:5000/swagger

---

## Running Tests

```bash
cd backend
dotnet test --logger trx --results-directory TestResults
```

Test coverage targets:
- Core + Infrastructure: **90%+** (failure simulator, routing, probes)
- API Controllers: **80%+**
- Angular Components: **70%+**

Key test files:
- [FailureSimulatorTests.cs](backend/tests/FanPad.ServiceMonitor.Tests/Probes/FailureSimulatorTests.cs) — all 15 scenarios
- [FailoverScenarioTests.cs](backend/tests/FanPad.ServiceMonitor.Tests/Scenarios/FailoverScenarioTests.cs) — 9 end-to-end scenarios
- [AgentToolsTests.cs](backend/tests/FanPad.ServiceMonitor.Tests/Agent/AgentToolsTests.cs) — tool definition validation
- [ProbeServiceIntegrationTests.cs](backend/tests/FanPad.ServiceMonitor.Tests/Integration/ProbeServiceIntegrationTests.cs) — probe + simulator integration

---

## Demo Walkthrough Guide

See [docs/runbook.md](docs/runbook.md) Section 4 for the step-by-step demo script.

**Core demo flow (5 minutes):**
1. Show baseline — all services green, campaigns GO
2. Activate "Mailgun Complete Outage" → watch agent detect and generate work plan
3. Approve failover in Routing tab → email routes to SES instantly
4. Show Campaign Gate — email campaigns held, SMS campaigns unaffected
5. Activate "Mailgun Recovering" → agent detects improvement
6. Clear simulation → system returns to nominal

---

## Design Decisions

**Agent trigger cadence:** Hybrid scheduled + on-demand. 2-minute polling for steady-state.
30-second elevated polling when degradation detected. On-demand for campaign gates and manual checks.

**Failover authority:** Human-in-the-loop. Agent recommends with a work plan.
Operator approves. Approval expires in 30 minutes; agent re-evaluates and resubmits if needed.

**Probe depth:** Synthetic test accounts — actual API calls to test endpoints
(Twilio magic numbers, Mailgun sandbox, SES verified test address).
Production: real test sends. For the assignment: simulated via `FailureSimulatorService`.

**Multi-channel campaigns:** Per-channel failover. A Mailgun failure holds the email
channel of a campaign but the SMS channel continues if Twilio is healthy.

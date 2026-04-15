# FanPad Service Health Monitor — Architecture

## Overview

FanPad runs outbound campaigns through Twilio (SMS), Mailgun (email primary), and AWS SES
(email fallback). This system monitors the health of those services in real time using an
AI agent (Claude API), ensures campaigns only launch through healthy channels, and provides
an auditable failover mechanism with human-in-the-loop approval.

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Angular 17 Dashboard                     │
│   Service Status Grid | Agent Log | Routing | Campaigns     │
│   Simulation Control Panel                                  │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTP REST + SignalR (WebSocket)
┌──────────────────────────▼──────────────────────────────────┐
│                   .NET 8 API Layer (C#)                      │
│                                                              │
│  HealthController  |  CampaignGateController                │
│  RoutingController | IncidentController                     │
│  SimulationController (demo/testing)                        │
│                                                              │
│  ServiceStatusHub (SignalR real-time push)                   │
│  HealthMonitorBackgroundService (scheduled polling)         │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              Agent Orchestration Service                     │
│         (Claude API via Anthropic.SDK — claude-sonnet-4-6)  │
│                                                              │
│  11 Tools:                                                   │
│  check_external_status   run_internal_probe                 │
│  get_health_history      get_open_incidents                 │
│  get_routing_state       get_pending_campaigns              │
│  submit_failover_recommendation                              │
│  open_incident           resolve_incident                   │
│  hold_campaign           release_campaign                   │
│                                                              │
│  Agentic loop: runs until stop_reason = end_turn            │
└──────────────┬───────────────────────────────────────────────┘
               │
┌──────────────▼──────────────────┐  ┌───────────────────────┐
│   Health Probe Services          │  │   Routing Service     │
│                                  │  │                       │
│  MailgunProbeService             │  │  ExecuteFailover()    │
│  SesProbeService                 │  │  RevertToPrimary()    │
│  TwilioProbeService              │  │  GetActiveRoute()     │
│                                  │  └───────────────────────┘
│  FailureSimulatorService         │
│  (singleton, injectable)         │
└──────────────┬───────────────────┘
               │
    ┌──────────┴──────────────┐
    │         PostgreSQL       │
    │                          │
    │  service_configs         │
    │  health_check_results    │
    │  routing_states          │
    │  incidents               │
    │  incident_updates        │
    │  agent_decisions         │
    │  failover_events         │
    │  failover_approvals      │
    │  campaigns               │
    └──────────────────────────┘
```

---

## Agent Architecture

### Why an Agent vs. a Direct API?

| Decision Factor | Direct API Polling | Claude Agent |
|---|---|---|
| Binary health check | ✅ Better (fast, deterministic) | Overkill |
| Multi-signal reasoning | ❌ Brittle rule-based | ✅ Natural |
| Distinguishing transient vs. sustained degradation | ❌ Hard to encode | ✅ Understands context |
| Work plan generation for operators | ❌ Template strings | ✅ Coherent prose |
| Cascade failure handling | ❌ Requires many edge case rules | ✅ Reasons through novel situations |
| Threshold ambiguity (e.g., 15% error intermittent) | ❌ Hardcoded thresholds miss nuance | ✅ Considers trend, severity, campaign context |

**Decision:** Hybrid. The probe layer (fast, deterministic HTTP calls) feeds raw data.
The agent layer reasons over that data to make decisions.

### Agent Tool Philosophy

Each tool is designed to be atomic and idempotent:
- **Read tools** (check_external_status, run_internal_probe, get_health_history, get_open_incidents, get_routing_state, get_pending_campaigns) — safe to call multiple times
- **Write tools** (open_incident, resolve_incident, hold_campaign, release_campaign, submit_failover_recommendation) — have side effects, agent uses judgment to call sparingly
- **Failover** is always submitted as a recommendation, never executed autonomously

### Agentic Loop

```
User message → Claude API (with tools) →
  if stop_reason == "tool_use":
    → Execute tools locally
    → Append tool results to messages
    → Call Claude API again
  if stop_reason == "end_turn":
    → Extract decision + work plan
    → Persist AgentDecision to DB
    → Push SignalR event
```

Max 10 iterations per evaluation to prevent runaway loops.

---

## Trigger Cadence

| Mode | Interval | Trigger |
|---|---|---|
| Normal polling | 2 minutes | `HealthMonitorBackgroundService` timer |
| Elevated (degradation detected) | 30 seconds | Automatically when agent returns non-`no_action` |
| Campaign gate | On-demand | `POST /api/campaigns/{id}/gate-check` |
| Manual | On-demand | `POST /api/health/evaluate` |
| Simulation activation | Immediate | `POST /api/simulation/activate` triggers agent |

---

## Failover Flow (Human-in-the-Loop)

```
Agent detects degradation
  ↓
Agent calls: submit_failover_recommendation(service_type, to_provider, recommendation, work_plan)
  ↓
FailoverApproval record created (status=pending, expires in 30 min)
  ↓
Dashboard shows approval card with work plan
  ↓
Operator clicks "Approve" or "Reject"
  ↓
  Approve: RoutingService.ExecuteFailoverAsync() called
           RoutingState updated, FailoverEvent recorded
           SignalR "FailoverExecuted" pushed to all dashboard clients
  Reject:  Approval marked rejected, no routing change
```

### Severity-Based Auto-Escalation

| Error Rate | External Incident | Agent Action |
|---|---|---|
| < 5% | No | Monitor only |
| 5–15% | No | Open incident, monitor |
| > 15% sustained | Any | Recommend failover |
| 0% / Unreachable | Any | Immediate failover recommendation + hold campaigns |
| Primary AND fallback both down | — | Hold all campaigns, critical alert |

---

## Per-Channel Campaign Gating

Multi-channel campaigns (e.g., email + SMS) gate independently:

```
Campaign: Summer Tour (email + sms)

Gate check:
  Email (Mailgun) → HOLD (Mailgun is down)
  SMS (Twilio)    → GO   (Twilio healthy)

Result: Email channel held. SMS channel allowed to proceed.
Campaign overall: PARTIAL (rerouted/hold depending on config)
```

---

## Data Model Summary

| Table | Purpose |
|---|---|
| `service_configs` | Provider metadata, credentials config, status page URLs |
| `health_check_results` | Time-series log of every probe result |
| `routing_states` | Current active provider per service type (email, sms) |
| `incidents` | Open/resolved service incidents with work plans |
| `agent_decisions` | Full agent reasoning log, decisions, token usage |
| `failover_events` | Audit trail of every executed failover |
| `failover_approvals` | Pending human approval requests with expiry |
| `campaigns` | Campaign definitions with gate status |

---

## Failure Simulation System

`FailureSimulatorService` is a singleton that intercepts probe calls before real HTTP is made.
When a scenario is active, probes return deterministic simulated data for the affected providers.

15 scenarios cover:
- Provider-specific outages (complete, partial, high-latency, high-error)
- Multi-provider failures (both email providers down, all services down)
- Cascade failures (Mailgun fails → failover to SES → SES also fails)
- Recovery scenarios (improving metrics trending up)
- Intermittent/flapping failures (every 3rd probe fails)

This enables comprehensive demo and testing without any real external service calls.

---

## Technology Choices

| Component | Choice | Rationale |
|---|---|---|
| AI Agent | Claude Sonnet 4.6 (Anthropic.SDK) | Tool use, extended context, reasoning quality |
| Real-time | SignalR | Native .NET, WebSocket with fallback, Angular client |
| DB | PostgreSQL + EF Core | JSONB for flexible probe details, strong time-series indexing |
| Scheduling | IHostedService (built-in) | No extra dependencies; adaptive interval via flag |
| HTTP | Named HttpClient per probe | Proper lifecycle management, testable |
| Auth | None (scoped for demo) | Would add JWT/role-based for production |

# FanPad Service Health Monitor — User Stories

## Epic 1: Service Health Monitoring

---

### Story 1.1 — Scheduled Health Checks

**As a** platform operator,
**I want** the system to automatically check all messaging service health every 2 minutes (escalating to 30 seconds when degradation is detected),
**So that** I have continuous visibility into service health without manual intervention.

**Acceptance Criteria:**
- Background service polls all providers on a 2-minute interval under normal conditions
- Polling escalates to 30-second intervals when an agent decision returns anything other than `no_action`
- Polling returns to 2-minute intervals once all services are back to `operational`
- Each cycle stores a `HealthCheckResult` record to PostgreSQL
- Service restarts resume the polling schedule without manual intervention

**Story Points:** 3 | **Priority:** P0

---

### Story 1.2 — Internal Synthetic Probe

**As a** platform operator,
**I want** the system to run a synthetic test send against each messaging provider using test/sandbox credentials,
**So that** I can confirm actual message delivery capability, not just API reachability.

**Acceptance Criteria:**
- Each probe performs a real (or simulated) test send to a designated test account
- Probe captures latency (ms), success rate (%), error code, and error message
- Results are tagged with `is_simulated: true` when `FailureSimulatorService` is active
- Probe failures are caught and returned as `Unknown` status (not exceptions)
- Twilio uses magic test numbers (`+15005550006`), Mailgun uses sandbox domain, SES uses verified test address

**Story Points:** 5 | **Priority:** P0

---

### Story 1.3 — External Status Page Check

**As a** platform operator,
**I want** the system to check each provider's official status page,
**So that** the agent can corroborate internal probe results with the provider's own acknowledgment.

**Acceptance Criteria:**
- Mailgun: parses `status.mailgun.com/api/v2/summary.json` indicator field
- Twilio: parses `status.twilio.com/api/v2/components.json` for Programmable Messaging component
- AWS SES: checks `health.aws.amazon.com` for availability
- External status is stored as a separate field (`external_status`) alongside `internal_status`
- HTTP errors from status pages return `Unknown` status (not crash)

**Story Points:** 3 | **Priority:** P1

---

### Story 1.4 — Health Check Result Persistence

**As a** platform engineer,
**I want** every health check result stored to PostgreSQL with full detail,
**So that** the agent can analyze historical trends and the team can audit past events.

**Acceptance Criteria:**
- Every probe result (both internal and external) is persisted to `health_check_results`
- Records include: provider, status, latency, success/error rate, error code, timestamp
- Records include `is_simulated` and `simulation_scenario` flags for test hygiene
- Database index on `(service_config_id, checked_at DESC)` for efficient time-range queries
- `v_service_health_summary` view provides latest status per service for dashboard

**Story Points:** 2 | **Priority:** P0

---

### Story 1.5 — Real-Time Dashboard Updates via SignalR

**As a** platform operator,
**I want** the dashboard to update automatically without page refreshes,
**So that** I see service health changes the moment the agent detects them.

**Acceptance Criteria:**
- SignalR hub (`/hubs/status`) pushes events for: agent decisions, incidents, failovers, campaign gate changes, simulation activations
- Angular dashboard components subscribe to relevant events and auto-refresh data
- Connection state (connected / reconnecting / disconnected) displayed in top navigation bar
- Automatic reconnection with exponential backoff (0s, 2s, 5s, 10s, 30s)
- Dashboard functions correctly even if SignalR is temporarily disconnected (falls back to manual refresh)

**Story Points:** 5 | **Priority:** P0

---

## Epic 2: Agentic Decision Making

---

### Story 2.1 — Multi-Signal Health Evaluation

**As a** platform operator,
**I want** the AI agent to evaluate service health using external status pages, internal probes, AND historical trends together,
**So that** decisions are based on comprehensive evidence rather than a single data point.

**Acceptance Criteria:**
- Agent calls `check_external_status`, `run_internal_probe`, AND `get_health_history` before deciding
- Agent considers trend direction: improving vs. worsening over the last 15 minutes
- Transient errors (< 5%, no external incident) result in `no_action` — not failover
- Agent output includes explicit citation of which signals influenced the decision
- Full tool call trace stored in `agent_decisions.actions_taken` (JSONB)

**Story Points:** 8 | **Priority:** P0

---

### Story 2.2 — Plain-English Work Plan Generation

**As a** campaign manager (non-technical),
**I want** the agent to generate a plain-English work plan whenever it detects a problem,
**So that** I understand what happened and what to do without reading technical logs.

**Acceptance Criteria:**
- Work plan is generated for every non-`no_action` decision
- Work plan covers: what happened, what will change, impact on campaigns, what to watch for, how to revert
- Work plan is stored in `agent_decisions.work_plan` and surfaced in the dashboard Agent Log tab
- Work plan is included in failover approval cards for operator review
- Language is plain English — no error codes, stack traces, or jargon

**Story Points:** 3 | **Priority:** P0

---

### Story 2.3 — Incident Management

**As a** platform operator,
**I want** the agent to automatically open and resolve incident records when service health degrades or recovers,
**So that** there is a structured timeline of all service events.

**Acceptance Criteria:**
- Agent opens an incident when error rate exceeds 5% sustained OR external incident is declared
- Incident severity maps to: Low (<5%), Medium (5–15%), High (>15%), Critical (complete outage)
- Agent resolves an incident when 3 consecutive clean probes are recorded after an outage
- Incident updates are appended (not overwritten) to preserve the event timeline
- Open incidents are surfaced on the service health card in the dashboard

**Story Points:** 5 | **Priority:** P1

---

### Story 2.4 — Failover Recommendation Submission

**As a** platform operator,
**I want** the agent to submit failover recommendations for human review rather than executing them autonomously,
**So that** no routing change happens without explicit human approval.

**Acceptance Criteria:**
- Agent NEVER calls a routing change directly — always uses `submit_failover_recommendation`
- Recommendation includes: from/to provider, evidence summary, work plan
- A `FailoverApproval` record is created with `status=pending` and 30-minute expiry
- Pending approvals appear on the Routing tab dashboard for operator action
- Expired approvals are not executable; agent resubmits on next cycle if still degraded

**Story Points:** 5 | **Priority:** P0

---

### Story 2.5 — Agent Decision Log

**As a** platform engineer,
**I want** the full agent reasoning, tool calls, decisions, and token usage stored to PostgreSQL,
**So that** every decision is fully auditable and the team can understand agent behavior.

**Acceptance Criteria:**
- Every agent run persists an `AgentDecision` record with: trigger type, input summary, full reasoning text, decision, work plan, model used, token counts, duration
- All tool calls and their results stored in `actions_taken` JSONB field
- Decision log is viewable in the Agent Log tab, filterable by trigger type
- Agent reasoning can be expanded inline (collapsed by default for readability)
- `GET /api/health/decisions` returns paginated decision history

**Story Points:** 3 | **Priority:** P1

---

### Story 2.6 — Manual Agent Evaluation Trigger

**As a** platform operator,
**I want** to manually trigger an agent evaluation at any time,
**So that** I can get an immediate assessment without waiting for the scheduled cycle.

**Acceptance Criteria:**
- `POST /api/health/evaluate?provider={optional}` triggers an on-demand agent run
- Optional `provider` filter scopes the evaluation to a single service
- "Trigger Evaluation" button in the Agent Log tab dashboard
- Response includes full decision, work plan, and duration
- Manual triggers are tagged with `trigger_type: "manual"` in the decision log

**Story Points:** 2 | **Priority:** P1

---

## Epic 3: Failover & Routing

---

### Story 3.1 — Human-in-the-Loop Approval Flow

**As a** platform operator,
**I want** to review and approve or reject agent failover recommendations before any routing change is made,
**So that** I maintain control over infrastructure routing decisions.

**Acceptance Criteria:**
- Pending approval cards show: from/to provider, agent recommendation, work plan, expiry time
- Operator can approve or reject from the Routing tab
- Approved failovers execute immediately and update routing state
- Rejected approvals are recorded; agent may resubmit on next evaluation if still degraded
- Approval/rejection is logged with reviewer name and timestamp

**Story Points:** 5 | **Priority:** P0

---

### Story 3.2 — Failover Execution

**As a** platform operator,
**I want** failovers to be executed atomically and immediately upon approval,
**So that** there is no gap in service routing during the transition.

**Acceptance Criteria:**
- `RoutingState` table updated in a single transaction
- `FailoverEvent` record created with: from/to provider, authority, work plan, timestamps
- SignalR `FailoverExecuted` event pushed to all dashboard clients immediately
- Dashboard Service Status Grid and Routing tab reflect the new active provider within one refresh cycle
- Failover is idempotent: executing the same failover twice is safe

**Story Points:** 3 | **Priority:** P0

---

### Story 3.3 — Failover Audit Trail

**As a** compliance/engineering stakeholder,
**I want** a complete audit trail of every failover, including who approved it and why,
**So that** post-incident reviews have full context.

**Acceptance Criteria:**
- `failover_events` table records: from/to provider, authority type, agent recommendation, work plan, approved_by, timestamps, success flag
- `GET /api/routing/failover-events` returns paginated history
- Simulated failovers are flagged `is_simulated: true` and excluded from production reporting
- Failover history is visible in the Routing tab under "Failover History"

**Story Points:** 2 | **Priority:** P1

---

### Story 3.4 — Revert to Primary Provider

**As a** platform operator,
**I want** to revert email routing back to Mailgun (primary) after it recovers,
**So that** the fallback provider (SES) is reserved for future outages.

**Acceptance Criteria:**
- Agent recommends revert after 3 consecutive clean probes from the primary provider
- Operator can manually revert via Routing tab "Revert to Primary" button
- `POST /api/routing/revert/{serviceType}` API endpoint supports programmatic revert
- Revert is recorded as a `FailoverEvent` with authority `human_approved`
- Routing tab shows whether the active provider is primary or fallback at all times

**Story Points:** 3 | **Priority:** P1

---

### Story 3.5 — Approval Expiration

**As a** platform operator,
**I want** failover approvals to expire after 30 minutes if not acted on,
**So that** stale recommendations don't accumulate and the agent re-evaluates with fresh data.

**Acceptance Criteria:**
- `failover_approvals.expires_at` set to `now() + 30 minutes` on creation
- Expired approvals return 400 Bad Request if an operator tries to approve
- `GET /api/routing/approvals` filters out expired approvals automatically
- Agent resubmits a fresh recommendation on next evaluation cycle if still degraded
- Expiry time shown on the approval card in the dashboard

**Story Points:** 2 | **Priority:** P2

---

## Epic 4: Campaign Gate

---

### Story 4.1 — Pre-Launch Campaign Gate Check

**As a** campaign manager,
**I want** the system to run an agent health evaluation before a campaign launches,
**So that** no campaign sends through a degraded or failed messaging service.

**Acceptance Criteria:**
- `POST /api/campaigns/{id}/gate-check` triggers a full agent evaluation scoped to campaign channels
- Gate result is one of: GO, HOLD, REROUTED, CANCELLED
- Result persisted to `campaigns.gate_status` with timestamp
- Agent decision linked via `campaigns.agent_decision_id` for traceability
- SignalR `CampaignGateChanged` event pushed to dashboard on completion

**Story Points:** 5 | **Priority:** P0

---

### Story 4.2 — Per-Channel Campaign Gating

**As a** campaign manager,
**I want** campaigns that use multiple channels (email + SMS) to gate each channel independently,
**So that** a Mailgun outage doesn't prevent the SMS portion of the campaign from sending.

**Acceptance Criteria:**
- `ChannelGateResult` returned for each service type in the campaign
- Email channel can be HOLD while SMS channel is GO simultaneously
- Campaign overall status reflects the worst-case channel (HOLD if any channel is held)
- Dashboard Campaign Gate tab shows per-channel status clearly
- Agent work plan explains which specific channel is affected and why

**Story Points:** 5 | **Priority:** P0

---

### Story 4.3 — Campaign Hold with Agent Reason

**As a** campaign manager,
**I want** held campaigns to display a clear, plain-English reason from the agent,
**So that** I know exactly why the campaign was held and what needs to happen to release it.

**Acceptance Criteria:**
- `campaigns.hold_reason` populated with agent work plan text on hold
- Hold reason displayed prominently on the campaign card in the dashboard
- Hold reason is readable by a non-technical campaign manager (no error codes)
- Reason includes estimated impact and what to watch for before releasing

**Story Points:** 2 | **Priority:** P0

---

### Story 4.4 — Campaign Release

**As a** platform operator,
**I want** to manually release a held campaign or have the agent release it upon service recovery,
**So that** campaigns are not blocked longer than necessary.

**Acceptance Criteria:**
- `POST /api/campaigns/{id}/release` manually releases a held campaign
- Running a new gate check when the service is healthy automatically transitions the campaign to GO
- Release is recorded with timestamp in `campaigns.gate_checked_at`
- Dashboard "Release" button visible on held campaign cards
- Agent can call `release_campaign` tool when it detects full service recovery

**Story Points:** 3 | **Priority:** P1

---

### Story 4.5 — Rerouted Campaign

**As a** campaign manager,
**I want** campaigns to automatically route through the fallback provider when the primary is down but the fallback is healthy,
**So that** campaigns can still launch even during a primary provider outage.

**Acceptance Criteria:**
- When email is routed through SES (fallback), campaigns using email proceed with `gate_status: rerouted`
- `campaigns.reroute_detail` JSONB captures which channel was rerouted and to which provider
- Rerouted campaign cards shown with a blue indicator (distinct from GO and HOLD)
- Agent work plan explicitly states that the campaign will proceed via the fallback provider
- Rerouted campaigns are reverted to primary routing when primary recovers

**Story Points:** 3 | **Priority:** P1

---

## Epic 5: Simulation & Testing

---

### Story 5.1 — Failure Scenario Activation

**As a** developer or demo presenter,
**I want** to activate a specific failure scenario from the dashboard,
**So that** I can test agent behavior and demonstrate the system without real service failures.

**Acceptance Criteria:**
- Simulation tab lists all 15 available scenarios with name and description
- Activating a scenario injects simulated probe data into the health probe layer
- `POST /api/simulation/activate` with optional `triggerAgentEvaluation: true` flag
- Activated scenario name shown in dashboard simulation banner
- Scenario can be changed by activating a different one (replaces previous)

**Story Points:** 3 | **Priority:** P0 (for demo)

---

### Story 5.2 — 15 Supported Failure Scenarios

**As a** developer,
**I want** comprehensive coverage of realistic failure modes across all providers,
**So that** the agent's decision logic is validated against a wide range of conditions.

**Supported scenarios:**

| # | Scenario | Affected Provider(s) | Expected Agent Response |
|---|---|---|---|
| 1 | MailgunCompleteOutage | Mailgun | Immediate failover recommendation |
| 2 | MailgunPartialDegradation | Mailgun | Open incident, recommend failover |
| 3 | MailgunHighLatency | Mailgun | Monitor only (99% success rate) |
| 4 | MailgunHighErrorRate | Mailgun | Open incident, recommend failover |
| 5 | SesCompleteOutage | SES | Alert (backup is down) |
| 6 | SesPartialDegradation | SES | Open incident on backup provider |
| 7 | BothEmailProvidersDown | Mailgun + SES | Hold all email campaigns, critical escalation |
| 8 | TwilioCompleteOutage | Twilio | Hold SMS campaigns, email unaffected |
| 9 | TwilioPartialDegradation | Twilio | Open incident, recommend hold |
| 10 | TwilioHighLatency | Twilio | Monitor for SLA breach |
| 11 | MailgunRecovering | Mailgun | Detect improvement, recommend revert |
| 12 | TwilioRecovering | Twilio | Detect improvement, monitor |
| 13 | MailgunThenSesFailure | Mailgun → SES cascade | Hold all email, critical alert |
| 14 | IntermittentMailgunErrors | Mailgun (flapping) | Monitor trend, no immediate failover |
| 15 | AllServicesDown | All providers | Full campaign hold, critical escalation |

**Story Points:** 5 | **Priority:** P0 (for demo)

---

### Story 5.3 — Auto-Trigger Agent on Scenario Activation

**As a** demo presenter,
**I want** the agent to automatically evaluate the service health immediately after a scenario is activated,
**So that** the demo shows real-time agent response without waiting for the next scheduled check.

**Acceptance Criteria:**
- `TriggerAgentEvaluation: true` (default) in the activation request triggers an immediate agent run
- Agent decision appears in the Agent Log within seconds of scenario activation
- Work plan visible in the dashboard immediately
- Optional: `TriggerAgentEvaluation: false` to activate without triggering (for staged demos)

**Story Points:** 2 | **Priority:** P0 (for demo)

---

### Story 5.4 — Simulation Banner in Dashboard

**As a** dashboard user,
**I want** a prominent banner showing when a simulation is active,
**So that** I don't confuse simulated failures with real production incidents.

**Acceptance Criteria:**
- Yellow banner displayed at the top of the Service Status Grid when simulation is active
- Banner shows the name of the active scenario
- Banner dismissed automatically when simulation is cleared
- Service health cards show a `🧪` badge and scenario name when data is simulated
- Simulated health check results tagged `is_simulated: true` in the DB and API responses

**Story Points:** 2 | **Priority:** P1

---

### Story 5.5 — Clear Simulation / Restore Nominal State

**As a** developer or demo presenter,
**I want** to clear the active simulation and return all services to healthy state,
**So that** I can reset the demo or return to monitoring real service health.

**Acceptance Criteria:**
- `POST /api/simulation/clear` clears the active scenario from `FailureSimulatorService`
- Subsequent probe calls return to real (or healthy simulated) results
- SignalR `SimulationCleared` event pushed to dashboard — banner dismissed
- "Clear Simulation" button available in the Simulation tab and shown on the active banner
- Clearing does NOT resolve open incidents or revert routing — those require explicit operator action

**Story Points:** 1 | **Priority:** P1

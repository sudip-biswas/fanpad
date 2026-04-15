# FanPad Service Health Monitor — Test Plan

## 1. Test Strategy Overview

### Testing Pyramid

```
        ┌─────────────┐
        │    E2E       │  < 10 tests — full demo flows
        ├─────────────┤
        │ Integration  │  ~30 tests — probe + routing + DB
        ├─────────────┤
        │    Unit      │  ~50 tests — simulator, tools, models
        └─────────────┘
```

### Tooling

| Layer | Tools |
|---|---|
| .NET unit / integration | xUnit 2.6, FluentAssertions 6, Moq 4, EF Core In-Memory |
| Angular unit | Jasmine + Karma, Angular TestBed |
| API contract | xUnit + `WebApplicationFactory<Program>` |
| CI | GitHub Actions (dotnet test + ng test) |

### Test Principles
- **No real external calls** — all probes use `FailureSimulatorService` or `MockHttpMessageHandler`
- **No real Claude API calls** — agent tool execution is tested with mocked dispatch
- **Deterministic** — simulated states are deterministic, no flakiness
- **Fast** — in-memory EF for all DB tests, no Docker required for unit/integration

---

## 2. Unit Tests

### 2.1 FailureSimulatorService (17 tests)

**File:** `Probes/FailureSimulatorTests.cs`

| Test | Assertion |
|---|---|
| `WhenNoScenario_IsSimulationActive_IsFalse` | `IsSimulationActive == false` |
| `WhenNoScenario_GetSimulatedState_ReturnsNull` | Returns null for any provider |
| `MailgunCompleteOutage_ReturnsOutageForMailgun_NullForOthers` | Mailgun=MajorOutage, SES=null, Twilio=null |
| `MailgunPartialDegradation_ReturnsCorrectMetrics` | Status=Degraded, Latency=4800, Success=62%, ErrorCode=SMTP_TIMEOUT |
| `MailgunHighLatency_ReturnsHighLatencyButGoodSuccessRate` | Latency>5000, SuccessRate≥99% |
| `MailgunHighErrorRate_ReturnsHighErrorWithLowLatency` | ErrorRate>20%, Latency<500 |
| `BothEmailProvidersDown_ReturnsMajorOutageForBothMailgunAndSes` | Mailgun=Outage, SES=Outage, Twilio=null |
| `SesCompleteOutage_AffectsOnlySes` | SES=MajorOutage, Mailgun=null |
| `TwilioCompleteOutage_AffectsOnlyTwilio` | Twilio=MajorOutage, Mailgun=null |
| `TwilioPartialDegradation_Returns68PercentSuccess` | SuccessRate=68%, ErrorCode=CARRIER_ISSUES |
| `MailgunThenSesFailure_SesFailsAfterThreeCallsOnly` | SES healthy for first 3 calls, fails after |
| `IntermittentMailgunErrors_FailsOnEveryThirdCall` | 3 failures in 9 calls (every 3rd) |
| `AllServicesDown_AffectsAllProviders` | All 3 providers return MajorOutage |
| `MailgunRecovering_Shows92PercentSuccessRate` | Status=Degraded, SuccessRate=92%, ErrorCode=RECOVERING |
| `ClearScenario_ResetsToNone` | After clear: IsActive=false, all providers return null |
| `ActivatingNewScenario_ReplacesExistingOne` | New scenario replaces old; old provider no longer affected |
| `MailgunOutage_AffectsBothProbeSources` | Both InternalSyntheticProbe and ExternalStatusPage return outage |

### 2.2 AgentTools (7 tests)

**File:** `Agent/AgentToolsTests.cs`

| Test | Assertion |
|---|---|
| `GetAllTools_ReturnsExpectedToolCount` | Exactly 11 tools registered |
| `GetAllTools_AllToolsHaveUniqueNames` | No duplicate tool names |
| `GetAllTools_AllToolsHaveNonEmptyDescription` | All descriptions non-null/non-empty |
| `GetAllTools_SubmitFailoverRecommendation_HasRequiredFields` | Required: service_type, to_provider, recommendation, work_plan |
| `GetAllTools_OpenIncident_RequiresProviderAndTitle` | Required: provider, title, severity |
| `ToolNames_MatchConstantValues` | All 11 constant values exist in tool set |
| `GetAllTools_ProviderEnumsContainExpectedValues` | mailgun, ses, twilio present in enum |

### 2.3 ProbeResult Factory Methods

| Test | Assertion |
|---|---|
| `ProbeResult.Operational_HasCorrectStatus` | Status=Operational, SuccessRate=100 |
| `ProbeResult.Degraded_HasCorrectFields` | Status=Degraded, all fields populated |
| `ProbeResult.Outage_HasZeroSuccessRate` | SuccessRate=0, LatencyMs=null |
| `ProbeResult.Unknown_HasNullMetrics` | Status=Unknown, SuccessRate=null |

---

## 3. Integration Tests

### 3.1 Probe Services Under Simulation (12 tests)

**File:** `Integration/ProbeServiceIntegrationTests.cs`

| Test | Provider | Scenario | Expected Result |
|---|---|---|---|
| Mailgun internal probe — outage | Mailgun | MailgunCompleteOutage | Status=MajorOutage, Code=SERVICE_UNAVAILABLE |
| Mailgun internal probe — degraded | Mailgun | MailgunPartialDegradation | Status=Degraded, SuccessRate=62% |
| Mailgun internal probe — healthy | Mailgun | None | Status=Operational, SuccessRate=100% |
| Mailgun external status — outage simulated | Mailgun | MailgunCompleteOutage | Status=MajorOutage, Source=ExternalStatusPage |
| SES internal probe — outage | SES | SesCompleteOutage | Status=MajorOutage, Code=AWS_SES_UNAVAILABLE |
| SES internal probe — healthy | SES | None | Status=Operational |
| Twilio internal probe — outage | Twilio | TwilioCompleteOutage | Status=MajorOutage, Code=TWILIO_UNAVAILABLE |
| Twilio internal probe — degraded | Twilio | TwilioPartialDegradation | Status=Degraded, SuccessRate=68% |
| Internal probe source | Any | None | Source=InternalSyntheticProbe |
| External status source | Any | Any | Source=ExternalStatusPage |
| Mailgun provider identity | — | — | Provider=Mailgun |
| SES provider identity | — | — | Provider=Ses |

### 3.2 RoutingService with In-Memory DB (6 tests)

| Test | Assertion |
|---|---|
| `GetActiveRouteAsync_ReturnsMailgunForEmail` | Active=Mailgun, ServiceType=Email |
| `GetPrimaryProviderAsync_ReturnsMailgun` | IsPrimary=true, Provider=Mailgun |
| `GetFallbackProviderAsync_ReturnsSes` | IsPrimary=false, Provider=Ses |
| `ExecuteFailoverAsync_UpdatesRoutingState` | Active provider changed, FailoverEvent created |
| `ExecuteFailoverAsync_ThrowsWhenTargetDisabled` | Throws InvalidOperationException |
| `RevertToPrimaryAsync_RestoresMailgun` | Active=Mailgun after revert |

---

## 4. Scenario Tests (9 End-to-End Scenarios)

**File:** `Scenarios/FailoverScenarioTests.cs`

### Scenario 1 — Mailgun Complete Outage → Failover to SES

| Phase | Detail |
|---|---|
| **Setup** | Seed DB with Mailgun (primary) + SES (fallback); routing active=Mailgun |
| **Stimulus** | Activate `MailgunCompleteOutage`; execute failover to SES |
| **Expected Agent Response** | Submits `submit_failover_recommendation` with severity=critical, work plan present |
| **Expected System State** | `routing_states.active_service_config_id` = SES; `failover_events` record created |
| **Pass Criteria** | Route.ActiveProvider=SES; FailoverEvent.FromProvider=Mailgun; FailoverEvent.ToProvider=SES; SES simulation state=null (unaffected) |

### Scenario 2 — Mailgun Partial Degradation (62% success)

| Phase | Detail |
|---|---|
| **Setup** | Seed DB; routing active=Mailgun |
| **Stimulus** | Activate `MailgunPartialDegradation` |
| **Expected Agent Response** | Open incident (severity=high); submit failover recommendation |
| **Expected System State** | Incident open for Mailgun; FailoverApproval pending |
| **Pass Criteria** | SimulatedState.ErrorRate > 15%; SimulatedState.LatencyMs > 2000; Agent threshold requires failover recommendation |

### Scenario 3 — Mailgun High Latency Only (99% success, no failover)

| Phase | Detail |
|---|---|
| **Setup** | Seed DB; routing active=Mailgun |
| **Stimulus** | Activate `MailgunHighLatency` |
| **Expected Agent Response** | Monitor only — `no_action` or open low-severity incident; NO failover recommendation |
| **Expected System State** | No routing change; no FailoverApproval created |
| **Pass Criteria** | SimulatedState.SuccessRate ≥ 99%; SimulatedState.ErrorRate < 5% — agent threshold not met for failover |

### Scenario 4 — Both Email Providers Down

| Phase | Detail |
|---|---|
| **Setup** | Seed DB with Mailgun + SES |
| **Stimulus** | Activate `BothEmailProvidersDown` |
| **Expected Agent Response** | Critical alert; hold all email campaigns; NO failover (no healthy alternative) |
| **Expected System State** | All email campaigns in HOLD; critical incident open; no FailoverApproval (nowhere to route) |
| **Pass Criteria** | Mailgun state=MajorOutage; SES state=MajorOutage; agent must NOT submit failover to either |

### Scenario 5 — Twilio Outage Does Not Affect Email

| Phase | Detail |
|---|---|
| **Setup** | Seed DB with Twilio (SMS), Mailgun (email) |
| **Stimulus** | Activate `TwilioCompleteOutage` |
| **Expected Agent Response** | Hold SMS campaigns; email campaigns unaffected |
| **Expected System State** | Email routing unchanged (Mailgun active); SMS campaigns HOLD; no email incidents |
| **Pass Criteria** | Email route still points to Mailgun; Twilio SimulatedState=MajorOutage; Mailgun SimulatedState=null |

### Scenario 6 — Mailgun Recovery → Revert to Primary

| Phase | Detail |
|---|---|
| **Setup** | Execute failover: email routing = SES |
| **Stimulus** | Activate `MailgunRecovering` (92% success); clear simulation; call RevertToPrimary |
| **Expected Agent Response** | Detect improving trend; recommend revert after 3 clean probes |
| **Expected System State** | Email routing reverted to Mailgun |
| **Pass Criteria** | RecoveringState.SuccessRate=92%; After clear: Route.ActiveProvider=Mailgun; Route.IsPrimary=true |

### Scenario 7 — Cascade Failure (Mailgun → SES also fails)

| Phase | Detail |
|---|---|
| **Setup** | Seed DB; routing active=Mailgun |
| **Stimulus** | Activate `MailgunThenSesFailure`; make 4+ probe calls |
| **Expected Agent Response** | Mailgun fails → recommend SES; then SES also fails → hold all email + critical alert |
| **Expected System State** | After cascade detected: all email campaigns HOLD; critical incident open |
| **Pass Criteria** | After 3 calls: Mailgun=MajorOutage; SES=null; After 4th call: SES=MajorOutage also |

### Scenario 8 — Intermittent Errors (Flapping)

| Phase | Detail |
|---|---|
| **Setup** | Seed DB; routing active=Mailgun |
| **Stimulus** | Activate `IntermittentMailgunErrors`; run 9 probe checks |
| **Expected Agent Response** | Monitor trend; do NOT immediately failover (only 1/3 probes fail — < 15% sustained) |
| **Expected System State** | No routing change; no FailoverApproval; possibly a low-severity incident |
| **Pass Criteria** | 3 of 9 probes return Degraded (every 3rd); 6 of 9 return null (healthy) |

### Scenario 9 — All Services Down

| Phase | Detail |
|---|---|
| **Setup** | Seed DB with all providers |
| **Stimulus** | Activate `AllServicesDown` |
| **Expected Agent Response** | Critical escalation; hold ALL campaigns (email and SMS); NO failover (no healthy provider) |
| **Expected System State** | All campaigns HOLD; critical incident open for all providers |
| **Pass Criteria** | Mailgun=MajorOutage; SES=MajorOutage; Twilio=MajorOutage — all three |

---

## 5. API Contract Tests

### HealthController

| Endpoint | Case | Expected |
|---|---|---|
| `GET /api/health/summary` | All services healthy | 200, array of ServiceHealthSummary |
| `GET /api/health/summary` | Simulation active | 200, `isSimulated: true` in affected services |
| `POST /api/health/evaluate` | No filter | 200, AgentDecision with decision + work_plan |
| `POST /api/health/evaluate?provider=mailgun` | Provider filter | 200, scoped decision |
| `GET /api/health/history?minutes=30` | Valid window | 200, results array |
| `GET /api/health/decisions?limit=5` | Limit param | 200, max 5 decisions |

### RoutingController

| Endpoint | Case | Expected |
|---|---|---|
| `GET /api/routing` | Normal state | 200, routing states for email + sms |
| `GET /api/routing/approvals` | No pending | 200, empty array |
| `POST /api/routing/approvals/{id}/approve` | Valid pending | 200, failover executed |
| `POST /api/routing/approvals/{id}/approve` | Expired | 400, error message |
| `POST /api/routing/approvals/{id}/reject` | Valid pending | 200, status=rejected |
| `POST /api/routing/revert/email` | On fallback | 200, route changed to primary |
| `POST /api/routing/revert/email` | Already primary | 200, no-op |

### SimulationController

| Endpoint | Case | Expected |
|---|---|---|
| `GET /api/simulation/scenarios` | Always | 200, 15 scenarios listed |
| `POST /api/simulation/activate` | Valid scenario | 200, `activated: true` |
| `POST /api/simulation/activate` | Invalid scenario | 400, error message |
| `POST /api/simulation/clear` | Active simulation | 200, `cleared: true`, previous scenario in response |
| `GET /api/simulation/status` | Active | 200, `isActive: true`, scenario name |
| `GET /api/simulation/status` | Inactive | 200, `isActive: false` |

### CampaignGateController

| Endpoint | Case | Expected |
|---|---|---|
| `GET /api/campaigns` | Seeded data | 200, 4 sample campaigns |
| `POST /api/campaigns/{id}/gate-check` | Healthy services | 200, `gateStatus: "go"` |
| `POST /api/campaigns/{id}/gate-check` | Mailgun outage | 200, `gateStatus: "hold"`, work plan present |
| `POST /api/campaigns/{id}/release` | Held campaign | 200, `gateStatus: "go"` |
| `POST /api/campaigns/{nonexistent}/gate-check` | Unknown ID | 404 |

### SignalR Hub Events

| Event | Trigger | Payload Fields |
|---|---|---|
| `AgentDecisionLogged` | Agent evaluation complete | `id`, `decision`, `workPlan`, `decidedAt` |
| `FailoverExecuted` | Failover approved and executed | `serviceType`, `fromProvider`, `toProvider`, `approvedBy` |
| `CampaignGateChanged` | Gate check complete | `campaignId`, `campaignName`, `gateStatus`, `workPlan` |
| `SimulationActivated` | Scenario activated | `scenario`, `name`, `description` |
| `SimulationCleared` | Simulation cleared | `previousScenario` |

---

## 6. Angular Component Tests

### ServiceStatusGridComponent

| Test | Assertion |
|---|---|
| Renders all service cards | One card per enabled service config |
| Shows simulation banner when `isSimulated: true` | Banner visible with scenario name |
| Hides simulation banner when cleared | Banner removed |
| Status class applied correctly | `.status-operational`, `.status-degraded`, etc. |
| Metrics shown when status not unknown | Latency, success rate, error rate rendered |
| Auto-refreshes on `agentDecision$` event | `getSummary()` called on SignalR push |
| Shows incident chips when open incidents exist | Incident severity and title displayed |

### AgentLogComponent

| Test | Assertion |
|---|---|
| Loads decisions on init | `getDecisions()` called, items rendered |
| Shows empty state when no decisions | Empty message displayed |
| Work plan section shown when present | Work plan text rendered |
| Reasoning toggle expands/collapses | Reasoning text shown/hidden on click |
| Trigger Evaluation button calls API | `triggerEvaluation()` called, button disabled during load |
| Decision badge reflects decision type | Class `decision-recommend_failover` applied for failover decisions |
| Auto-refreshes on `agentDecision$` event | `loadDecisions()` called on SignalR push |

### SimulationControlComponent

| Test | Assertion |
|---|---|
| Lists all scenarios categorized | Email, SMS, complex groups rendered |
| Shows active scenario banner when simulation active | Banner visible with scenario name |
| Selecting a scenario enables activate button | Button enabled after selection |
| Activate calls service with correct scenario ID | `activateScenario(scenarioId)` called |
| Clear button calls clearSimulation | `clearSimulation()` called, banner hidden |
| Result message shown on success | Success message rendered after activation |

### RoutingStateComponent

| Test | Assertion |
|---|---|
| Shows active provider for each service type | Email and SMS routing cards rendered |
| PRIMARY/FALLBACK badge correct | Badge reflects `isPrimary` flag |
| Pending approvals shown | Approval cards rendered when `getPendingApprovals()` returns data |
| Approve button calls service | `approveFailover()` called with correct approval ID |
| Reject button calls service | `rejectFailover()` called |
| Revert button shown only on fallback | Button hidden when active provider is primary |
| Auto-refreshes on `failoverExecuted$` | `loadAll()` called on SignalR push |

### CampaignGateComponent

| Test | Assertion |
|---|---|
| Lists all campaigns | One card per campaign |
| Gate status icon matches status | 🟢 GO, 🟡 HOLD, 🔵 REROUTED |
| Hold reason shown for held campaigns | Hold reason text visible |
| Gate Check button calls service | `checkCampaignGate()` called |
| Release button visible only for held campaigns | Button hidden for GO campaigns |
| Release calls service | `releaseCampaign()` called with correct ID |
| Auto-refreshes on `campaignGateChanged$` | `load()` called on SignalR push |

---

## 7. Test Coverage Goals

| Layer | Target | Rationale |
|---|---|---|
| Core models + enums | 100% | Pure data, no logic |
| FailureSimulatorService | 95%+ | Critical for all demo scenarios |
| RoutingService | 90%+ | Core business logic |
| Probe services | 85%+ | Simulation paths well-tested |
| AgentOrchestrationService | 70%+ | Tool dispatch and persistence logic |
| API Controllers | 80%+ | All happy paths + key error cases |
| Angular Components | 70%+ | Key interactions and state changes |

---

## 8. Test Execution

```bash
# Run all backend tests
cd backend
dotnet test --logger trx --results-directory TestResults

# Run with coverage (requires coverlet)
dotnet test --collect:"XPlat Code Coverage"

# Run specific test file
dotnet test --filter "FullyQualifiedName~FailoverScenarioTests"

# Run Angular tests
cd frontend/fanpad-dashboard
npm test

# Run Angular tests headless (CI)
npm test -- --watch=false --browsers=ChromeHeadless
```

---

## 9. Known Gaps and Future Work

| Gap | Priority | Notes |
|---|---|---|
| Agent orchestration E2E tests with real Claude API | P2 | Requires API key in CI; expensive |
| Load testing: 100 concurrent health checks | P2 | Use k6 or Artillery |
| PostgreSQL migration integration tests | P2 | Test schema migrations don't break existing data |
| SignalR connection resilience tests | P2 | Simulate disconnect/reconnect cycles |
| Approval expiry automated test | P1 | Mock system clock to test 30-min expiry |

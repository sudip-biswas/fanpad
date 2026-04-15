# FanPad Service Health Monitor — Operations Runbook

## Common Operator Workflows

---

### 1. Mailgun is Degraded — What Do I Do?

**Detection:** Agent opens an incident, dashboard shows yellow/red status on Mailgun card.
Check the Agent Log tab for the work plan.

**Steps:**
1. Review the **work plan** in the Agent Log or the pending approval card in the Routing tab
2. Check the Routing tab for a **pending failover approval**
3. If approval exists: review the recommendation, click **Approve Failover**
4. SES becomes the active email provider immediately
5. Monitor the SES health card — should show green within 1-2 probe cycles
6. When Mailgun recovers: agent will detect and recommend **revert to primary**
7. Click **Revert to Primary** or approve the revert recommendation

---

### 2. A Campaign Is On Hold — How Do I Release It?

**Detection:** Campaign Gate tab shows a campaign with status **HOLD** and a hold reason.

**Steps:**
1. Read the hold reason — it explains which service was unhealthy at gate check time
2. Check Service Status — if the affected service is now healthy, it's safe to release
3. Click **Gate Check** to run a fresh agent evaluation for that campaign
4. If the agent returns **GO**, the campaign is automatically released
5. Or click **Release** to manually override the hold

---

### 3. All Email Services Are Down

**Detection:** Both Mailgun and SES cards show red. Agent work plan states "no email fallback available."

**Steps:**
1. Do NOT approve any failover (there is nowhere to route)
2. All email campaigns are held automatically by the agent
3. Notify the artist/team that email delivery is paused
4. Monitor both providers' external status pages (links in the service config)
5. When either service recovers: agent will detect and recommend transition
6. Check agent log for updated work plan as the situation evolves

---

### 4. How to Run a Demo Walkthrough

**Step 1: Baseline** — Dashboard shows all services green. All campaigns are GO.

**Step 2: Activate failure** — Go to Simulation tab. Select "Mailgun Complete Outage". Click Activate.
The agent automatically evaluates and the Agent Log shows a `recommend_failover` decision.

**Step 3: Review recommendation** — Agent Log shows the work plan. Routing tab shows a pending approval.

**Step 4: Approve failover** — Click Approve. Dashboard updates: email now routes through SES.

**Step 5: Check campaigns** — Campaign Gate tab shows email campaigns rerouted or held depending on scenario.

**Step 6: Simulate recovery** — Select "Mailgun Recovering" scenario. Agent detects improving metrics.

**Step 7: Revert** — Agent recommends revert after 3 clean probes. Click approve. Mailgun is primary again.

**Step 8: Clear simulation** — Click "Clear Simulation". System returns to nominal.

---

### 5. Approvals Are Expiring

Failover approvals expire after **30 minutes**. If an approval expires:
- The agent will detect the service is still degraded on next poll
- A new recommendation and approval will be submitted
- No action needed — the system is self-correcting

---

### 6. Agent Evaluation Is Stuck

**Symptom:** Agent Log hasn't updated in > 5 minutes, background service seems stalled.

**Steps:**
1. POST `/api/health/evaluate` to trigger a manual evaluation
2. Check application logs for exceptions from `AgentOrchestrationService`
3. Verify `ANTHROPIC_API_KEY` environment variable is set correctly
4. Check Anthropic API status (https://status.anthropic.com)
5. If Claude API is unreachable, the background service will retry on next cycle

---

### 7. Database Maintenance

**View current routing:**
```sql
SELECT * FROM v_service_health_summary;
```

**View open incidents:**
```sql
SELECT i.title, i.severity, i.opened_at, sc.provider
FROM incidents i
JOIN service_configs sc ON sc.id = i.service_config_id
WHERE i.status != 'resolved'
ORDER BY i.opened_at DESC;
```

**View recent agent decisions:**
```sql
SELECT trigger_type, decision, work_plan, decided_at, duration_ms
FROM agent_decisions
ORDER BY decided_at DESC
LIMIT 20;
```

**Clean up old health check results (keep 7 days):**
```sql
DELETE FROM health_check_results WHERE checked_at < NOW() - INTERVAL '7 days';
```

---

### 8. Resetting Simulation State (Recovery)

If a demo simulation gets stuck:
```bash
curl -X POST http://localhost:5000/api/simulation/clear
```

Or from the dashboard: Simulation tab → Clear Simulation button.

---

## Alert Severity Reference

| Severity | Error Rate | Action |
|---|---|---|
| Low | < 5% transient | Monitor only |
| Medium | 5–15% sustained | Open incident, prepare for failover |
| High | > 15% sustained OR external incident | Recommend failover |
| Critical | 0% / unreachable | Immediate failover recommendation, hold campaigns |
| Catastrophic | Primary + fallback both down | Hold all, escalate to engineering |

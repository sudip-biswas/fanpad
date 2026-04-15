-- FanPad Service Health Monitor - Initial Schema
-- PostgreSQL

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ─────────────────────────────────────────────
-- ENUMS
-- ─────────────────────────────────────────────

CREATE TYPE service_type AS ENUM ('email', 'sms', 'push');

CREATE TYPE service_provider AS ENUM ('mailgun', 'ses', 'twilio', 'sns');

CREATE TYPE health_status AS ENUM ('operational', 'degraded', 'partial_outage', 'major_outage', 'unknown');

CREATE TYPE incident_severity AS ENUM ('low', 'medium', 'high', 'critical');

CREATE TYPE incident_status AS ENUM ('open', 'monitoring', 'resolved');

CREATE TYPE routing_action AS ENUM ('auto', 'manual_override', 'agent_recommended');

CREATE TYPE campaign_gate_status AS ENUM ('go', 'hold', 'rerouted', 'cancelled');

CREATE TYPE failover_authority AS ENUM ('agent_auto', 'human_approved', 'human_rejected');

-- ─────────────────────────────────────────────
-- SERVICE CONFIGURATIONS
-- ─────────────────────────────────────────────

CREATE TABLE service_configs (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    provider        service_provider NOT NULL,
    service_type    service_type NOT NULL,
    display_name    VARCHAR(100) NOT NULL,
    is_primary      BOOLEAN NOT NULL DEFAULT FALSE,
    priority        INT NOT NULL DEFAULT 1,          -- lower = higher priority
    is_enabled      BOOLEAN NOT NULL DEFAULT TRUE,
    config_json     JSONB NOT NULL DEFAULT '{}',     -- provider-specific config (keys, endpoints)
    status_page_url VARCHAR(500),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (provider, service_type)
);

-- ─────────────────────────────────────────────
-- HEALTH CHECK LOG
-- ─────────────────────────────────────────────

CREATE TABLE health_check_results (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    service_config_id   UUID NOT NULL REFERENCES service_configs(id),
    checked_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status              health_status NOT NULL,
    latency_ms          INT,                         -- probe round-trip latency
    success_rate        NUMERIC(5,2),                -- 0.00–100.00
    error_rate          NUMERIC(5,2),
    error_code          VARCHAR(50),
    error_message       TEXT,
    external_status     health_status,               -- from status page
    internal_status     health_status,               -- from synthetic probe
    probe_detail_json   JSONB NOT NULL DEFAULT '{}', -- raw probe response
    is_simulated        BOOLEAN NOT NULL DEFAULT FALSE,
    simulation_scenario VARCHAR(100)
);

CREATE INDEX idx_hcr_service_checked ON health_check_results (service_config_id, checked_at DESC);
CREATE INDEX idx_hcr_checked_at ON health_check_results (checked_at DESC);

-- ─────────────────────────────────────────────
-- ROUTING STATE  (current active provider per service type)
-- ─────────────────────────────────────────────

CREATE TABLE routing_states (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    service_type            service_type NOT NULL UNIQUE,
    active_service_config_id UUID NOT NULL REFERENCES service_configs(id),
    action                  routing_action NOT NULL DEFAULT 'auto',
    reason                  TEXT,
    changed_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    changed_by              VARCHAR(100) DEFAULT 'system'
);

-- ─────────────────────────────────────────────
-- INCIDENTS
-- ─────────────────────────────────────────────

CREATE TABLE incidents (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    service_config_id   UUID NOT NULL REFERENCES service_configs(id),
    title               VARCHAR(300) NOT NULL,
    description         TEXT,
    severity            incident_severity NOT NULL,
    status              incident_status NOT NULL DEFAULT 'open',
    opened_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at         TIMESTAMPTZ,
    work_plan           TEXT,                        -- agent-generated work plan
    affected_campaigns  UUID[],
    is_simulated        BOOLEAN NOT NULL DEFAULT FALSE,
    simulation_scenario VARCHAR(100)
);

CREATE INDEX idx_incidents_status ON incidents (status);
CREATE INDEX idx_incidents_service ON incidents (service_config_id, status);

CREATE TABLE incident_updates (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    incident_id UUID NOT NULL REFERENCES incidents(id) ON DELETE CASCADE,
    message     TEXT NOT NULL,
    author      VARCHAR(100) NOT NULL DEFAULT 'agent',
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────────
-- FAILOVER EVENTS
-- ─────────────────────────────────────────────

CREATE TABLE failover_events (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    service_type            service_type NOT NULL,
    from_provider           service_provider NOT NULL,
    to_provider             service_provider NOT NULL,
    incident_id             UUID REFERENCES incidents(id),
    authority               failover_authority NOT NULL DEFAULT 'human_approved',
    agent_recommendation    TEXT,
    work_plan               TEXT,
    approved_by             VARCHAR(100),
    initiated_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at            TIMESTAMPTZ,
    reverted_at             TIMESTAMPTZ,
    success                 BOOLEAN NOT NULL DEFAULT TRUE,
    is_simulated            BOOLEAN NOT NULL DEFAULT FALSE
);

-- ─────────────────────────────────────────────
-- AGENT DECISIONS  (full reasoning log)
-- ─────────────────────────────────────────────

CREATE TABLE agent_decisions (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    trigger_type        VARCHAR(50) NOT NULL,        -- 'scheduled', 'campaign_gate', 'manual'
    trigger_context     JSONB NOT NULL DEFAULT '{}',
    input_summary       TEXT NOT NULL,
    reasoning           TEXT,                        -- agent chain-of-thought
    decision            VARCHAR(50) NOT NULL,        -- 'no_action', 'recommend_failover', 'hold_campaign', etc.
    decision_detail     TEXT,
    actions_taken       JSONB NOT NULL DEFAULT '[]',
    work_plan           TEXT,
    model_used          VARCHAR(100) DEFAULT 'claude-sonnet-4-6',
    prompt_tokens       INT,
    completion_tokens   INT,
    decided_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    duration_ms         INT,
    incident_id         UUID REFERENCES incidents(id),
    failover_event_id   UUID REFERENCES failover_events(id)
);

CREATE INDEX idx_agent_decisions_decided_at ON agent_decisions (decided_at DESC);

-- ─────────────────────────────────────────────
-- CAMPAIGNS
-- ─────────────────────────────────────────────

CREATE TABLE campaigns (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name                VARCHAR(300) NOT NULL,
    artist_name         VARCHAR(200),
    service_types       service_type[] NOT NULL,     -- e.g. ['email', 'sms']
    scheduled_at        TIMESTAMPTZ,
    gate_status         campaign_gate_status NOT NULL DEFAULT 'go',
    gate_checked_at     TIMESTAMPTZ,
    hold_reason         TEXT,
    reroute_detail      JSONB,
    agent_decision_id   UUID REFERENCES agent_decisions(id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────────
-- PENDING FAILOVER APPROVALS  (human-in-the-loop)
-- ─────────────────────────────────────────────

CREATE TABLE failover_approvals (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    agent_decision_id       UUID NOT NULL REFERENCES agent_decisions(id),
    service_type            service_type NOT NULL,
    from_provider           service_provider NOT NULL,
    to_provider             service_provider NOT NULL,
    agent_recommendation    TEXT NOT NULL,
    work_plan               TEXT NOT NULL,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending', -- pending, approved, rejected
    reviewed_by             VARCHAR(100),
    review_note             TEXT,
    requested_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reviewed_at             TIMESTAMPTZ,
    expires_at              TIMESTAMPTZ NOT NULL DEFAULT (NOW() + INTERVAL '30 minutes')
);

CREATE INDEX idx_approvals_status ON failover_approvals (status);

-- ─────────────────────────────────────────────
-- FUNCTIONS & TRIGGERS
-- ─────────────────────────────────────────────

CREATE OR REPLACE FUNCTION update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_service_configs_updated_at
    BEFORE UPDATE ON service_configs
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();

CREATE TRIGGER trg_campaigns_updated_at
    BEFORE UPDATE ON campaigns
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();

-- View: latest health status per service
CREATE VIEW v_service_health_summary AS
SELECT
    sc.id,
    sc.provider,
    sc.service_type,
    sc.display_name,
    sc.is_primary,
    sc.priority,
    sc.is_enabled,
    hcr.status          AS latest_status,
    hcr.latency_ms      AS latest_latency_ms,
    hcr.success_rate    AS latest_success_rate,
    hcr.checked_at      AS last_checked_at,
    hcr.is_simulated,
    hcr.simulation_scenario,
    rs.active_service_config_id = sc.id AS is_active_route
FROM service_configs sc
LEFT JOIN LATERAL (
    SELECT * FROM health_check_results
    WHERE service_config_id = sc.id
    ORDER BY checked_at DESC
    LIMIT 1
) hcr ON TRUE
LEFT JOIN routing_states rs ON rs.service_type = sc.service_type;

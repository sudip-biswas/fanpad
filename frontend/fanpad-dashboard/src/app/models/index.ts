// ── Core domain models matching the .NET API responses ──────────────────────

export type HealthStatus = 'operational' | 'degraded' | 'partial_outage' | 'major_outage' | 'unknown';
export type ServiceType  = 'email' | 'sms' | 'push';
export type Provider     = 'mailgun' | 'ses' | 'twilio' | 'sns';
export type GateStatus   = 'go' | 'hold' | 'rerouted' | 'cancelled';
export type IncidentSeverity = 'low' | 'medium' | 'high' | 'critical';
export type IncidentStatus   = 'open' | 'monitoring' | 'resolved';
export type FailureScenario  = string;

export interface ServiceHealthSummary {
  id: string;
  provider: Provider;
  serviceType: ServiceType;
  displayName: string;
  isPrimary: boolean;
  isActiveRoute: boolean;
  status: HealthStatus;
  latencyMs?: number;
  successRate?: number;
  errorRate?: number;
  lastChecked?: string;
  isSimulated: boolean;
  simulationScenario?: string;
  openIncidents: IncidentSummary[];
}

export interface HealthCheckRecord {
  id: string;
  provider: Provider;
  serviceType: ServiceType;
  checkedAt: string;
  status: HealthStatus;
  latencyMs?: number;
  successRate?: number;
  errorRate?: number;
  errorCode?: string;
  isSimulated: boolean;
  simulationScenario?: string;
}

export interface Incident {
  id: string;
  provider: Provider;
  displayName: string;
  title: string;
  description?: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  openedAt: string;
  resolvedAt?: string;
  workPlan?: string;
  isSimulated: boolean;
  simulationScenario?: string;
  updates: IncidentUpdate[];
}

export interface IncidentSummary {
  id: string;
  title: string;
  severity: IncidentSeverity;
  openedAt: string;
}

export interface IncidentUpdate {
  message: string;
  author: string;
  createdAt: string;
}

export interface AgentDecision {
  id: string;
  triggerType: string;
  decision: string;
  decisionDetail?: string;
  workPlan?: string;
  reasoning?: string;
  decidedAt: string;
  durationMs?: number;
  modelUsed?: string;
}

export interface RoutingState {
  serviceType: ServiceType;
  activeProvider: Provider;
  displayName: string;
  isPrimary: boolean;
  action: string;
  reason?: string;
  changedAt: string;
  changedBy: string;
}

export interface FailoverApproval {
  id: string;
  serviceType: ServiceType;
  fromProvider: Provider;
  toProvider: Provider;
  agentRecommendation: string;
  workPlan: string;
  status: string;
  requestedAt: string;
  expiresAt: string;
  agentDecisionId: string;
}

export interface Campaign {
  id: string;
  name: string;
  artistName?: string;
  serviceTypes: ServiceType[];
  scheduledAt?: string;
  gateStatus: GateStatus;
  gateCheckedAt?: string;
  holdReason?: string;
  createdAt: string;
}

export interface SimulationScenarioInfo {
  id: string;
  name: string;
  description: string;
  affectedServices: Provider[];
}

export interface SimulationStatus {
  isActive: boolean;
  activeScenario: string;
  description: string;
  scenarios: SimulationScenarioInfo[];
}

// ── SignalR push event payloads ───────────────────────────────────────────────

export interface AgentDecisionEvent {
  id: string;
  decision: string;
  workPlan?: string;
  decidedAt: string;
  durationMs?: number;
}

export interface FailoverExecutedEvent {
  failoverEventId?: string;
  serviceType: ServiceType;
  fromProvider?: Provider;
  toProvider?: Provider;
  activeProvider?: Provider;
  approvedBy?: string;
  workPlan?: string;
  reason?: string;
}

export interface CampaignGateChangedEvent {
  campaignId: string;
  campaignName: string;
  gateStatus: GateStatus;
  workPlan?: string;
}

export interface SimulationActivatedEvent {
  scenario: string;
  name: string;
  description: string;
}

import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AgentDecision,
  Campaign,
  FailoverApproval,
  HealthCheckRecord,
  Incident,
  RoutingState,
  ServiceHealthSummary,
  SimulationStatus,
} from '../models';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class HealthMonitorService {
  private readonly base = environment.apiBase;

  constructor(private http: HttpClient) {}

  // ── Health ────────────────────────────────────────────────────────────────

  getSummary(): Observable<ServiceHealthSummary[]> {
    return this.http.get<ServiceHealthSummary[]>(`${this.base}/health/summary`);
  }

  getHistory(provider?: string, minutes = 60): Observable<HealthCheckRecord[]> {
    const params: Record<string, string> = { minutes: String(minutes) };
    if (provider) params['provider'] = provider;
    return this.http.get<HealthCheckRecord[]>(`${this.base}/health/history`, { params });
  }

  triggerEvaluation(provider?: string): Observable<AgentDecision> {
    const params = provider ? { provider } : {};
    return this.http.post<AgentDecision>(`${this.base}/health/evaluate`, null, { params });
  }

  getDecisions(limit = 20): Observable<AgentDecision[]> {
    return this.http.get<AgentDecision[]>(`${this.base}/health/decisions`, { params: { limit: String(limit) } });
  }

  // ── Incidents ─────────────────────────────────────────────────────────────

  getIncidents(status?: string): Observable<Incident[]> {
    const params = status ? { status } : {};
    return this.http.get<Incident[]>(`${this.base}/incidents`, { params });
  }

  // ── Routing ───────────────────────────────────────────────────────────────

  getRoutes(): Observable<RoutingState[]> {
    return this.http.get<RoutingState[]>(`${this.base}/routing`);
  }

  getPendingApprovals(): Observable<FailoverApproval[]> {
    return this.http.get<FailoverApproval[]>(`${this.base}/routing/approvals`);
  }

  approveFailover(approvalId: string, reviewedBy?: string, note?: string): Observable<any> {
    return this.http.post(`${this.base}/routing/approvals/${approvalId}/approve`, { reviewedBy, note });
  }

  rejectFailover(approvalId: string, note?: string): Observable<any> {
    return this.http.post(`${this.base}/routing/approvals/${approvalId}/reject`, { note });
  }

  revertToPrimary(serviceType: string, reason?: string): Observable<any> {
    return this.http.post(`${this.base}/routing/revert/${serviceType}`, { reason });
  }

  // ── Campaigns ─────────────────────────────────────────────────────────────

  getCampaigns(): Observable<Campaign[]> {
    return this.http.get<Campaign[]>(`${this.base}/campaigns`);
  }

  checkCampaignGate(campaignId: string): Observable<any> {
    return this.http.post(`${this.base}/campaigns/${campaignId}/gate-check`, null);
  }

  releaseCampaign(campaignId: string, note?: string): Observable<any> {
    return this.http.post(`${this.base}/campaigns/${campaignId}/release`, { note });
  }

  // ── Simulation ────────────────────────────────────────────────────────────

  getSimulationStatus(): Observable<SimulationStatus> {
    return this.http.get<SimulationStatus>(`${this.base}/simulation/scenarios`);
  }

  activateScenario(scenario: string, triggerAgent = true): Observable<any> {
    return this.http.post(`${this.base}/simulation/activate`, {
      scenario,
      triggerAgentEvaluation: triggerAgent,
    });
  }

  clearSimulation(): Observable<any> {
    return this.http.post(`${this.base}/simulation/clear`, null);
  }
}

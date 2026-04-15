import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SignalRService } from '../../services/signalr.service';
import { ServiceHealthSummary, HealthStatus } from '../../models';

@Component({
  selector: 'app-service-status-grid',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="status-grid">
      <div class="grid-header">
        <h2>Service Health</h2>
        <button class="btn-refresh" (click)="refresh()" [disabled]="loading">
          {{ loading ? 'Checking...' : '↻ Refresh' }}
        </button>
      </div>

      <div *ngIf="simulationBanner" class="simulation-banner">
        🧪 SIMULATION ACTIVE: {{ simulationBanner }}
      </div>

      <div class="cards">
        <div
          *ngFor="let svc of services"
          class="service-card"
          [class]="'status-' + svc.status"
        >
          <div class="card-header">
            <span class="provider-icon">{{ getProviderIcon(svc.provider) }}</span>
            <span class="provider-name">{{ svc.displayName }}</span>
            <span class="route-badge" *ngIf="svc.isActiveRoute">ACTIVE</span>
            <span class="primary-badge" *ngIf="svc.isPrimary && !svc.isActiveRoute">PRIMARY</span>
          </div>

          <div class="status-indicator">
            <span class="status-dot"></span>
            <span class="status-text">{{ formatStatus(svc.status) }}</span>
          </div>

          <div class="metrics" *ngIf="svc.status !== 'unknown'">
            <div class="metric" *ngIf="svc.latencyMs != null">
              <span class="label">Latency</span>
              <span class="value" [class.warn]="svc.latencyMs > 2000" [class.crit]="svc.latencyMs > 5000">
                {{ svc.latencyMs }}ms
              </span>
            </div>
            <div class="metric" *ngIf="svc.successRate != null">
              <span class="label">Success</span>
              <span class="value" [class.warn]="svc.successRate < 90" [class.crit]="svc.successRate < 70">
                {{ svc.successRate | number:'1.0-0' }}%
              </span>
            </div>
            <div class="metric" *ngIf="svc.errorRate != null">
              <span class="label">Error</span>
              <span class="value" [class.warn]="svc.errorRate > 10" [class.crit]="svc.errorRate > 30">
                {{ svc.errorRate | number:'1.0-0' }}%
              </span>
            </div>
          </div>

          <div class="last-checked" *ngIf="svc.lastChecked">
            Last checked: {{ svc.lastChecked | date:'HH:mm:ss' }}
          </div>

          <div class="incidents" *ngIf="svc.openIncidents?.length">
            <div class="incident-chip" *ngFor="let i of svc.openIncidents">
              ⚠️ {{ i.severity.toUpperCase() }}: {{ i.title }}
            </div>
          </div>

          <div class="sim-badge" *ngIf="svc.isSimulated">
            🧪 {{ svc.simulationScenario }}
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .status-grid { padding: 16px; }
    .grid-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .grid-header h2 { margin: 0; font-size: 20px; font-weight: 600; }
    .btn-refresh { padding: 6px 16px; border: 1px solid #ccc; border-radius: 6px; cursor: pointer; background: white; }
    .btn-refresh:disabled { opacity: 0.6; cursor: not-allowed; }

    .simulation-banner {
      background: #fff3cd; border: 1px solid #ffc107; border-radius: 6px;
      padding: 10px 16px; margin-bottom: 16px; font-weight: 500; color: #856404;
    }

    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }

    .service-card {
      background: white; border-radius: 12px; padding: 16px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.08); border-left: 4px solid #ccc;
      transition: all 0.3s ease;
    }
    .service-card.status-operational { border-left-color: #22c55e; }
    .service-card.status-degraded    { border-left-color: #f59e0b; }
    .service-card.status-partial_outage { border-left-color: #f97316; }
    .service-card.status-major_outage   { border-left-color: #ef4444; }
    .service-card.status-unknown        { border-left-color: #9ca3af; }

    .card-header { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
    .provider-icon { font-size: 20px; }
    .provider-name { font-weight: 600; font-size: 16px; flex: 1; }
    .route-badge { background: #3b82f6; color: white; border-radius: 4px; padding: 2px 6px; font-size: 11px; font-weight: 700; }
    .primary-badge { background: #e5e7eb; color: #374151; border-radius: 4px; padding: 2px 6px; font-size: 11px; }

    .status-indicator { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
    .status-dot {
      width: 10px; height: 10px; border-radius: 50%;
      background: #ccc; flex-shrink: 0;
    }
    .status-operational .status-dot { background: #22c55e; }
    .status-degraded    .status-dot { background: #f59e0b; }
    .status-partial_outage .status-dot { background: #f97316; }
    .status-major_outage   .status-dot { background: #ef4444; animation: pulse 1s infinite; }
    .status-text { font-weight: 500; text-transform: capitalize; }

    @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .metrics { display: flex; gap: 16px; margin-bottom: 8px; }
    .metric { display: flex; flex-direction: column; }
    .metric .label { font-size: 11px; color: #9ca3af; font-weight: 500; }
    .metric .value { font-size: 15px; font-weight: 600; }
    .metric .value.warn { color: #f59e0b; }
    .metric .value.crit { color: #ef4444; }

    .last-checked { font-size: 11px; color: #9ca3af; margin-top: 8px; }
    .incidents { margin-top: 8px; }
    .incident-chip { background: #fef3c7; border: 1px solid #fbbf24; border-radius: 4px; padding: 4px 8px; font-size: 12px; margin-bottom: 4px; }
    .sim-badge { margin-top: 8px; background: #ede9fe; color: #7c3aed; border-radius: 4px; padding: 4px 8px; font-size: 11px; }
  `]
})
export class ServiceStatusGridComponent implements OnInit, OnDestroy {
  services: ServiceHealthSummary[] = [];
  loading = false;
  simulationBanner: string | null = null;
  private subs = new Subscription();

  constructor(
    private healthService: HealthMonitorService,
    private signalR: SignalRService
  ) {}

  ngOnInit(): void {
    this.refresh();

    // Auto-refresh on agent decisions
    this.subs.add(
      this.signalR.agentDecision$.subscribe(() => this.refresh())
    );
    this.subs.add(
      this.signalR.failoverExecuted$.subscribe(() => this.refresh())
    );
    this.subs.add(
      this.signalR.simulationActivated$.subscribe(ev => {
        this.simulationBanner = ev.name;
        this.refresh();
      })
    );
    this.subs.add(
      this.signalR.simulationCleared$.subscribe(() => {
        this.simulationBanner = null;
        this.refresh();
      })
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  refresh(): void {
    this.loading = true;
    this.healthService.getSummary().subscribe({
      next: data => { this.services = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  getProviderIcon(provider: string): string {
    const icons: Record<string, string> = {
      mailgun: '📧', ses: '☁️', twilio: '📱', sns: '🔔'
    };
    return icons[provider] ?? '🔌';
  }

  formatStatus(status: HealthStatus): string {
    return status.replace(/_/g, ' ');
  }
}

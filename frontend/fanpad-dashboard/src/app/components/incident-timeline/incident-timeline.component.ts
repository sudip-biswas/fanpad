import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SignalRService } from '../../services/signalr.service';
import { Incident, IncidentSeverity, IncidentStatus } from '../../models';

@Component({
  selector: 'app-incident-timeline',
  standalone: true,
  imports: [CommonModule, DatePipe],
  template: `
    <div class="timeline-panel">
      <div class="panel-header">
        <h2>Incident Timeline</h2>
        <div class="filter-pills">
          <button
            *ngFor="let f of filters"
            class="pill"
            [class.active]="activeFilter === f.value"
            (click)="setFilter(f.value)"
          >{{ f.label }}</button>
        </div>
      </div>

      <!-- Summary stats -->
      <div class="stats-row">
        <div class="stat" *ngFor="let s of stats">
          <span class="stat-num" [style.color]="s.color">{{ s.value }}</span>
          <span class="stat-label">{{ s.label }}</span>
        </div>
      </div>

      <!-- Timeline -->
      <div class="timeline" *ngIf="incidents.length; else empty">
        <div class="timeline-item" *ngFor="let i of incidents; trackBy: trackById">
          <!-- Spine connector -->
          <div class="spine">
            <div class="spine-dot" [class]="'sev-' + i.severity"></div>
            <div class="spine-line" *ngIf="!isLast(i)"></div>
          </div>

          <!-- Content -->
          <div class="item-content" [class]="'status-' + i.status">
            <!-- Header row -->
            <div class="item-header">
              <div class="left">
                <span class="provider-badge">{{ getProviderIcon(i.provider) }} {{ i.displayName }}</span>
                <span class="severity-chip" [class]="'sev-chip-' + i.severity">
                  {{ i.severity.toUpperCase() }}
                </span>
                <span class="status-chip" [class]="'status-chip-' + i.status">
                  {{ formatStatus(i.status) }}
                </span>
                <span class="sim-chip" *ngIf="i.isSimulated">🧪 SIM</span>
              </div>
              <div class="right">
                <span class="time-open">{{ i.openedAt | date:'MMM d, HH:mm' }}</span>
                <span class="duration" *ngIf="i.status === 'resolved' && i.resolvedAt">
                  · {{ getDuration(i.openedAt, i.resolvedAt) }}
                </span>
              </div>
            </div>

            <!-- Title -->
            <div class="item-title">{{ i.title }}</div>

            <!-- Description -->
            <div class="item-desc" *ngIf="i.description">{{ i.description }}</div>

            <!-- Work plan -->
            <details class="work-plan-details" *ngIf="i.workPlan">
              <summary>📋 Work Plan</summary>
              <div class="work-plan-body">{{ i.workPlan }}</div>
            </details>

            <!-- Updates thread -->
            <div class="updates" *ngIf="i.updates?.length">
              <div class="update-item" *ngFor="let u of i.updates">
                <span class="update-author">{{ u.author }}</span>
                <span class="update-time">{{ u.createdAt | date:'HH:mm' }}</span>
                <span class="update-msg">{{ u.message }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <ng-template #empty>
        <div class="empty-state">
          <div class="empty-icon">✅</div>
          <div class="empty-text">
            {{ activeFilter === 'open' ? 'No open incidents — all services healthy.' : 'No incidents found.' }}
          </div>
        </div>
      </ng-template>
    </div>
  `,
  styles: [`
    .timeline-panel { padding: 16px; }

    .panel-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; flex-wrap: wrap; gap: 10px; }
    .panel-header h2 { margin: 0; font-size: 20px; font-weight: 600; }

    .filter-pills { display: flex; gap: 6px; }
    .pill { padding: 5px 14px; border-radius: 20px; border: 1px solid #e2e8f0; background: white; font-size: 13px; font-weight: 500; color: #64748b; cursor: pointer; transition: all 0.15s; }
    .pill:hover { border-color: #3b82f6; color: #3b82f6; }
    .pill.active { background: #3b82f6; border-color: #3b82f6; color: white; }

    .stats-row { display: flex; gap: 24px; margin-bottom: 20px; padding: 14px 20px; background: white; border-radius: 10px; box-shadow: 0 1px 4px rgba(0,0,0,0.06); }
    .stat { display: flex; flex-direction: column; align-items: center; gap: 2px; }
    .stat-num { font-size: 24px; font-weight: 700; line-height: 1; }
    .stat-label { font-size: 11px; color: #94a3b8; font-weight: 500; text-transform: uppercase; letter-spacing: 0.5px; }

    /* ── Timeline ─────────────────────────────────────────────────────────── */
    .timeline { display: flex; flex-direction: column; gap: 0; }

    .timeline-item { display: flex; gap: 0; }

    .spine { display: flex; flex-direction: column; align-items: center; padding-right: 16px; flex-shrink: 0; width: 32px; }
    .spine-dot { width: 14px; height: 14px; border-radius: 50%; margin-top: 18px; flex-shrink: 0; border: 2px solid white; box-shadow: 0 0 0 2px currentColor; }
    .spine-dot.sev-low      { color: #22c55e; background: #22c55e; }
    .spine-dot.sev-medium   { color: #f59e0b; background: #f59e0b; }
    .spine-dot.sev-high     { color: #f97316; background: #f97316; }
    .spine-dot.sev-critical { color: #ef4444; background: #ef4444; animation: pulse 1.2s infinite; }
    .spine-line { flex: 1; width: 2px; background: #e2e8f0; min-height: 16px; margin: 4px 0; }

    .item-content {
      flex: 1; background: white; border-radius: 10px; padding: 14px 16px;
      margin-bottom: 12px; box-shadow: 0 1px 4px rgba(0,0,0,0.06);
      border-left: 3px solid #e2e8f0; animation: fadeIn 0.25s ease;
    }
    .item-content.status-open      { border-left-color: #ef4444; }
    .item-content.status-monitoring { border-left-color: #f59e0b; }
    .item-content.status-resolved  { border-left-color: #22c55e; opacity: 0.8; }

    .item-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 8px; flex-wrap: wrap; gap: 6px; }
    .left { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
    .right { display: flex; align-items: center; gap: 4px; white-space: nowrap; }

    .provider-badge { font-weight: 600; font-size: 13px; color: #374151; }
    .severity-chip { padding: 2px 7px; border-radius: 4px; font-size: 11px; font-weight: 700; }
    .sev-chip-low      { background: #dcfce7; color: #166534; }
    .sev-chip-medium   { background: #fef3c7; color: #92400e; }
    .sev-chip-high     { background: #ffedd5; color: #c2410c; }
    .sev-chip-critical { background: #fee2e2; color: #991b1b; }

    .status-chip { padding: 2px 7px; border-radius: 4px; font-size: 11px; font-weight: 600; }
    .status-chip-open       { background: #fee2e2; color: #991b1b; }
    .status-chip-monitoring { background: #fef3c7; color: #92400e; }
    .status-chip-resolved   { background: #dcfce7; color: #166534; }

    .sim-chip { background: #ede9fe; color: #6d28d9; padding: 2px 6px; border-radius: 4px; font-size: 10px; font-weight: 700; }

    .time-open { font-size: 12px; color: #94a3b8; }
    .duration  { font-size: 12px; color: #94a3b8; }

    .item-title { font-weight: 600; font-size: 14px; color: #1e293b; margin-bottom: 4px; }
    .item-desc  { font-size: 13px; color: #64748b; margin-bottom: 8px; line-height: 1.5; }

    .work-plan-details { margin-top: 8px; }
    .work-plan-details summary { font-size: 12px; font-weight: 600; color: #374151; cursor: pointer; user-select: none; padding: 4px 0; }
    .work-plan-details summary:hover { color: #3b82f6; }
    .work-plan-body { margin-top: 8px; background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 6px; padding: 10px 12px; font-size: 12px; color: #15803d; white-space: pre-wrap; line-height: 1.6; }

    .updates { margin-top: 10px; border-top: 1px solid #f1f5f9; padding-top: 8px; display: flex; flex-direction: column; gap: 6px; }
    .update-item { display: flex; align-items: baseline; gap: 8px; font-size: 12px; }
    .update-author { font-weight: 600; color: #475569; flex-shrink: 0; }
    .update-time   { color: #94a3b8; flex-shrink: 0; }
    .update-msg    { color: #374151; }

    .empty-state { display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 64px 20px; gap: 12px; }
    .empty-icon { font-size: 40px; }
    .empty-text { font-size: 15px; color: #94a3b8; font-weight: 500; }

    @keyframes fadeIn { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: translateY(0); } }
    @keyframes pulse  { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `]
})
export class IncidentTimelineComponent implements OnInit, OnDestroy {
  incidents: Incident[] = [];
  activeFilter: string = 'open';
  private subs = new Subscription();

  filters = [
    { label: 'Open',      value: 'open' },
    { label: 'All',       value: 'all' },
    { label: 'Resolved',  value: 'resolved' },
  ];

  constructor(
    private healthService: HealthMonitorService,
    private signalR: SignalRService
  ) {}

  ngOnInit(): void {
    this.load();
    this.subs.add(this.signalR.incidentOpened$.subscribe(() => this.load()));
    this.subs.add(this.signalR.incidentResolved$.subscribe(() => this.load()));
    this.subs.add(this.signalR.agentDecision$.subscribe(() => this.load()));
  }

  ngOnDestroy(): void { this.subs.unsubscribe(); }

  setFilter(f: string): void {
    this.activeFilter = f;
    this.load();
  }

  load(): void {
    const status = this.activeFilter === 'all' ? undefined : this.activeFilter;
    this.healthService.getIncidents(status).subscribe(data => this.incidents = data);
  }

  get stats() {
    const all = this.incidents;
    return [
      { label: 'Open',      value: all.filter(i => i.status === 'open').length,       color: '#ef4444' },
      { label: 'Monitoring',value: all.filter(i => i.status === 'monitoring').length,  color: '#f59e0b' },
      { label: 'Resolved',  value: all.filter(i => i.status === 'resolved').length,   color: '#22c55e' },
      { label: 'Critical',  value: all.filter(i => i.severity === 'critical').length, color: '#dc2626' },
    ];
  }

  trackById(_: number, i: Incident): string { return i.id; }

  isLast(i: Incident): boolean {
    return this.incidents.indexOf(i) === this.incidents.length - 1;
  }

  getProviderIcon(provider: string): string {
    const m: Record<string, string> = { mailgun: '📧', ses: '☁️', twilio: '📱' };
    return m[provider] ?? '🔌';
  }

  formatStatus(s: IncidentStatus): string {
    return { open: 'Open', monitoring: 'Monitoring', resolved: 'Resolved' }[s] ?? s;
  }

  getDuration(from: string, to: string): string {
    const ms = new Date(to).getTime() - new Date(from).getTime();
    const m = Math.round(ms / 60000);
    if (m < 60) return `${m}m`;
    return `${Math.floor(m / 60)}h ${m % 60}m`;
  }
}

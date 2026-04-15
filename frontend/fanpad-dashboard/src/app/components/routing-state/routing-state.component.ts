import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SignalRService } from '../../services/signalr.service';
import { FailoverApproval, RoutingState } from '../../models';

@Component({
  selector: 'app-routing-state',
  standalone: true,
  imports: [CommonModule, DatePipe],
  template: `
    <div class="routing-panel">
      <h2>Routing & Failover</h2>

      <div class="routes">
        <div class="route-card" *ngFor="let r of routes">
          <div class="route-header">
            <span class="channel-icon">{{ r.serviceType === 'email' ? '📧' : '📱' }}</span>
            <span class="channel-name">{{ r.serviceType | titlecase }}</span>
            <span class="route-action" [class]="'action-' + r.action">{{ r.action }}</span>
          </div>
          <div class="active-provider">
            <span class="arrow">→</span>
            <span class="provider-name">{{ r.displayName }}</span>
            <span class="primary-tag" *ngIf="r.isPrimary">PRIMARY</span>
            <span class="fallback-tag" *ngIf="!r.isPrimary">FALLBACK</span>
          </div>
          <div class="route-reason" *ngIf="r.reason">{{ r.reason }}</div>
          <div class="route-meta">Changed {{ r.changedAt | date:'HH:mm:ss' }} by {{ r.changedBy }}</div>
          <button class="btn-revert" *ngIf="!r.isPrimary" (click)="revert(r.serviceType)" [disabled]="loading">
            ↩ Revert to Primary
          </button>
        </div>
      </div>

      <!-- Pending Approvals -->
      <div class="approvals-section" *ngIf="pendingApprovals.length">
        <h3>⏳ Pending Failover Approvals</h3>
        <div class="approval-card" *ngFor="let a of pendingApprovals">
          <div class="approval-route">
            {{ a.serviceType | titlecase }}: {{ a.fromProvider }} → {{ a.toProvider }}
          </div>
          <div class="approval-recommendation">
            <strong>Agent recommendation:</strong> {{ a.agentRecommendation }}
          </div>
          <div class="work-plan-box">
            <div class="wp-label">Work Plan</div>
            {{ a.workPlan }}
          </div>
          <div class="approval-expiry">Expires at {{ a.expiresAt | date:'HH:mm' }}</div>
          <div class="approval-actions">
            <button class="btn-approve" (click)="approve(a)" [disabled]="loading">✓ Approve Failover</button>
            <button class="btn-reject" (click)="reject(a)" [disabled]="loading">✕ Reject</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .routing-panel { padding: 16px; }
    h2 { margin: 0 0 16px 0; font-size: 20px; font-weight: 600; }

    .routes { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 16px; margin-bottom: 24px; }

    .route-card { background: white; border-radius: 10px; padding: 16px; box-shadow: 0 2px 6px rgba(0,0,0,0.06); }
    .route-header { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
    .channel-icon { font-size: 20px; }
    .channel-name { font-weight: 600; font-size: 16px; flex: 1; }
    .route-action { padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 700; text-transform: uppercase; }
    .action-auto { background: #dcfce7; color: #166534; }
    .action-agent_recommended { background: #fef3c7; color: #92400e; }
    .action-manual_override { background: #dbeafe; color: #1e40af; }

    .active-provider { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .arrow { color: #9ca3af; }
    .provider-name { font-size: 15px; font-weight: 600; }
    .primary-tag { background: #dcfce7; color: #166534; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 700; }
    .fallback-tag { background: #fef3c7; color: #92400e; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 700; }

    .route-reason { font-size: 12px; color: #6b7280; margin-bottom: 4px; }
    .route-meta { font-size: 11px; color: #9ca3af; }
    .btn-revert { margin-top: 10px; padding: 6px 14px; border: 1px solid #d97706; color: #d97706; background: transparent; border-radius: 6px; cursor: pointer; font-size: 13px; }
    .btn-revert:hover { background: #fffbeb; }

    .approvals-section h3 { margin: 0 0 12px 0; font-size: 16px; font-weight: 600; color: #d97706; }

    .approval-card { background: #fffbeb; border: 2px solid #fbbf24; border-radius: 10px; padding: 16px; margin-bottom: 12px; }
    .approval-route { font-weight: 700; font-size: 15px; margin-bottom: 8px; color: #92400e; }
    .approval-recommendation { font-size: 13px; color: #374151; margin-bottom: 10px; line-height: 1.5; }
    .work-plan-box { background: white; border: 1px solid #fde68a; border-radius: 6px; padding: 12px; margin-bottom: 10px; font-size: 13px; white-space: pre-wrap; line-height: 1.6; }
    .wp-label { font-size: 11px; font-weight: 700; color: #92400e; text-transform: uppercase; margin-bottom: 4px; }
    .approval-expiry { font-size: 12px; color: #9ca3af; margin-bottom: 10px; }
    .approval-actions { display: flex; gap: 10px; }
    .btn-approve { padding: 8px 20px; background: #22c55e; color: white; border: none; border-radius: 6px; cursor: pointer; font-weight: 600; }
    .btn-approve:hover { background: #16a34a; }
    .btn-reject { padding: 8px 16px; background: transparent; border: 1px solid #ef4444; color: #ef4444; border-radius: 6px; cursor: pointer; }
    .btn-reject:disabled, .btn-approve:disabled { opacity: 0.6; cursor: not-allowed; }
  `]
})
export class RoutingStateComponent implements OnInit, OnDestroy {
  routes: RoutingState[] = [];
  pendingApprovals: FailoverApproval[] = [];
  loading = false;
  private subs = new Subscription();

  constructor(private healthService: HealthMonitorService, private signalR: SignalRService) {}

  ngOnInit(): void {
    this.loadAll();
    this.subs.add(this.signalR.failoverExecuted$.subscribe(() => this.loadAll()));
    this.subs.add(this.signalR.agentDecision$.subscribe(() => this.loadApprovals()));
  }

  ngOnDestroy(): void { this.subs.unsubscribe(); }

  loadAll(): void {
    this.loadRoutes();
    this.loadApprovals();
  }

  loadRoutes(): void {
    this.healthService.getRoutes().subscribe(r => this.routes = r);
  }

  loadApprovals(): void {
    this.healthService.getPendingApprovals().subscribe(a => this.pendingApprovals = a);
  }

  approve(a: FailoverApproval): void {
    this.loading = true;
    this.healthService.approveFailover(a.id, 'operator').subscribe({
      next: () => { this.loading = false; this.loadAll(); },
      error: () => { this.loading = false; }
    });
  }

  reject(a: FailoverApproval): void {
    this.loading = true;
    this.healthService.rejectFailover(a.id).subscribe({
      next: () => { this.loading = false; this.loadApprovals(); },
      error: () => { this.loading = false; }
    });
  }

  revert(serviceType: string): void {
    this.loading = true;
    this.healthService.revertToPrimary(serviceType, 'Manual revert by operator').subscribe({
      next: () => { this.loading = false; this.loadAll(); },
      error: () => { this.loading = false; }
    });
  }
}

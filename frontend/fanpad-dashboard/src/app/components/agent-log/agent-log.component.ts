import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SignalRService } from '../../services/signalr.service';
import { AgentDecision } from '../../models';

@Component({
  selector: 'app-agent-log',
  standalone: true,
  imports: [CommonModule, DatePipe],
  template: `
    <div class="agent-log">
      <div class="log-header">
        <h2>Agent Decision Log</h2>
        <button class="btn-trigger" (click)="triggerEvaluation()" [disabled]="triggering">
          {{ triggering ? 'Evaluating...' : '▶ Trigger Evaluation' }}
        </button>
      </div>

      <div class="decisions">
        <div class="decision-item" *ngFor="let d of decisions" [class]="'decision-' + normalizeDecision(d.decision)">
          <div class="decision-meta">
            <span class="decision-badge">{{ formatDecision(d.decision) }}</span>
            <span class="trigger-badge">{{ d.triggerType }}</span>
            <span class="time">{{ d.decidedAt | date:'HH:mm:ss' }}</span>
            <span class="duration" *ngIf="d.durationMs">{{ d.durationMs }}ms</span>
          </div>

          <div class="work-plan" *ngIf="d.workPlan">
            <div class="wp-label">Work Plan</div>
            <div class="wp-text">{{ d.workPlan }}</div>
          </div>

          <div class="reasoning-toggle" (click)="toggleReasoning(d.id)">
            {{ expanded.has(d.id) ? '▼ Hide' : '▶ Show' }} Agent Reasoning
          </div>
          <div class="reasoning-text" *ngIf="expanded.has(d.id) && d.reasoning">
            {{ d.reasoning }}
          </div>
        </div>

        <div class="empty" *ngIf="!decisions.length">
          No agent decisions yet. Trigger an evaluation or wait for the scheduled check.
        </div>
      </div>
    </div>
  `,
  styles: [`
    .agent-log { padding: 16px; }
    .log-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .log-header h2 { margin: 0; font-size: 20px; font-weight: 600; }
    .btn-trigger { padding: 8px 18px; background: #3b82f6; color: white; border: none; border-radius: 6px; cursor: pointer; font-weight: 500; }
    .btn-trigger:disabled { opacity: 0.6; cursor: not-allowed; }

    .decisions { display: flex; flex-direction: column; gap: 12px; }

    .decision-item {
      background: white; border-radius: 10px; padding: 16px;
      box-shadow: 0 2px 6px rgba(0,0,0,0.06); border-left: 4px solid #e5e7eb;
    }
    .decision-no_action      { border-left-color: #22c55e; }
    .decision-recommend_failover { border-left-color: #f59e0b; }
    .decision-hold_campaign  { border-left-color: #f97316; }
    .decision-open_incident  { border-left-color: #ef4444; }

    .decision-meta { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    .decision-badge {
      padding: 3px 10px; border-radius: 4px; font-size: 12px; font-weight: 600;
      background: #f3f4f6; color: #374151;
    }
    .trigger-badge { padding: 2px 8px; background: #ede9fe; color: #6d28d9; border-radius: 4px; font-size: 11px; }
    .time, .duration { font-size: 12px; color: #9ca3af; }

    .work-plan { margin: 10px 0; background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 6px; padding: 12px; }
    .wp-label { font-size: 11px; font-weight: 700; color: #166534; margin-bottom: 4px; text-transform: uppercase; letter-spacing: 0.5px; }
    .wp-text { font-size: 13px; color: #15803d; line-height: 1.6; white-space: pre-wrap; }

    .reasoning-toggle { font-size: 12px; color: #6b7280; cursor: pointer; margin-top: 8px; user-select: none; }
    .reasoning-toggle:hover { color: #3b82f6; }
    .reasoning-text { font-size: 12px; color: #6b7280; background: #f9fafb; border-radius: 6px; padding: 10px; margin-top: 6px; white-space: pre-wrap; line-height: 1.6; max-height: 300px; overflow-y: auto; }

    .empty { text-align: center; color: #9ca3af; padding: 40px; }
  `]
})
export class AgentLogComponent implements OnInit, OnDestroy {
  decisions: AgentDecision[] = [];
  triggering = false;
  expanded = new Set<string>();
  private subs = new Subscription();

  constructor(
    private healthService: HealthMonitorService,
    private signalR: SignalRService
  ) {}

  ngOnInit(): void {
    this.loadDecisions();
    this.subs.add(
      this.signalR.agentDecision$.subscribe(() => this.loadDecisions())
    );
  }

  ngOnDestroy(): void { this.subs.unsubscribe(); }

  loadDecisions(): void {
    this.healthService.getDecisions(30).subscribe(d => this.decisions = d);
  }

  triggerEvaluation(): void {
    this.triggering = true;
    this.healthService.triggerEvaluation().subscribe({
      next: () => { this.triggering = false; this.loadDecisions(); },
      error: () => { this.triggering = false; }
    });
  }

  toggleReasoning(id: string): void {
    if (this.expanded.has(id)) this.expanded.delete(id);
    else this.expanded.add(id);
  }

  formatDecision(d: string): string {
    return d.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }

  normalizeDecision(d: string): string {
    return d.replace(/_/g, '_');
  }
}

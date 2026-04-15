import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SignalRService } from '../../services/signalr.service';
import { Campaign, GateStatus } from '../../models';

@Component({
  selector: 'app-campaign-gate',
  standalone: true,
  imports: [CommonModule, DatePipe],
  template: `
    <div class="campaigns-panel">
      <h2>Campaign Gate</h2>

      <div class="campaigns">
        <div class="campaign-card" *ngFor="let c of campaigns" [class]="'gate-' + c.gateStatus">
          <div class="campaign-header">
            <div class="gate-indicator">
              <span class="gate-icon">{{ getGateIcon(c.gateStatus) }}</span>
              <span class="gate-label">{{ c.gateStatus | uppercase }}</span>
            </div>
            <div class="campaign-actions">
              <button class="btn-check" (click)="checkGate(c)" [disabled]="checking === c.id">
                {{ checking === c.id ? '...' : '✓ Gate Check' }}
              </button>
              <button class="btn-release" *ngIf="c.gateStatus === 'hold'" (click)="release(c)" [disabled]="checking === c.id">
                ↑ Release
              </button>
            </div>
          </div>

          <div class="campaign-name">{{ c.name }}</div>
          <div class="campaign-artist" *ngIf="c.artistName">{{ c.artistName }}</div>

          <div class="channels">
            <span class="channel-tag" *ngFor="let t of c.serviceTypes">
              {{ t === 'email' ? '📧' : '📱' }} {{ t }}
            </span>
          </div>

          <div class="scheduled" *ngIf="c.scheduledAt">
            Scheduled: {{ c.scheduledAt | date:'MMM d, HH:mm' }}
          </div>

          <div class="hold-reason" *ngIf="c.holdReason">
            <strong>Hold reason:</strong> {{ c.holdReason }}
          </div>
        </div>

        <div class="empty" *ngIf="!campaigns.length">
          No campaigns found.
        </div>
      </div>
    </div>
  `,
  styles: [`
    .campaigns-panel { padding: 16px; }
    h2 { margin: 0 0 16px 0; font-size: 20px; font-weight: 600; }
    .campaigns { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; }

    .campaign-card { background: white; border-radius: 10px; padding: 16px; box-shadow: 0 2px 6px rgba(0,0,0,0.06); border-top: 3px solid #e5e7eb; }
    .campaign-card.gate-go       { border-top-color: #22c55e; }
    .campaign-card.gate-hold     { border-top-color: #f59e0b; }
    .campaign-card.gate-rerouted { border-top-color: #3b82f6; }
    .campaign-card.gate-cancelled { border-top-color: #ef4444; }

    .campaign-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; }
    .gate-indicator { display: flex; align-items: center; gap: 6px; }
    .gate-icon { font-size: 18px; }
    .gate-label { font-size: 12px; font-weight: 700; }
    .gate-go .gate-label { color: #16a34a; }
    .gate-hold .gate-label { color: #d97706; }
    .gate-rerouted .gate-label { color: #2563eb; }

    .campaign-actions { display: flex; gap: 6px; }
    .btn-check { padding: 4px 10px; background: #3b82f6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; }
    .btn-release { padding: 4px 10px; background: #22c55e; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; }
    .btn-check:disabled, .btn-release:disabled { opacity: 0.6; cursor: not-allowed; }

    .campaign-name { font-weight: 600; font-size: 15px; margin-bottom: 2px; }
    .campaign-artist { font-size: 13px; color: #6b7280; margin-bottom: 8px; }

    .channels { display: flex; gap: 6px; margin-bottom: 8px; }
    .channel-tag { background: #f3f4f6; color: #374151; padding: 3px 8px; border-radius: 4px; font-size: 12px; }

    .scheduled { font-size: 12px; color: #9ca3af; margin-bottom: 6px; }

    .hold-reason { background: #fffbeb; border: 1px solid #fde68a; border-radius: 6px; padding: 8px; font-size: 12px; color: #92400e; line-height: 1.5; white-space: pre-wrap; }

    .empty { text-align: center; color: #9ca3af; padding: 40px; }
  `]
})
export class CampaignGateComponent implements OnInit, OnDestroy {
  campaigns: Campaign[] = [];
  checking: string | null = null;
  private subs = new Subscription();

  constructor(private healthService: HealthMonitorService, private signalR: SignalRService) {}

  ngOnInit(): void {
    this.load();
    this.subs.add(this.signalR.campaignGateChanged$.subscribe(() => this.load()));
  }

  ngOnDestroy(): void { this.subs.unsubscribe(); }

  load(): void {
    this.healthService.getCampaigns().subscribe(c => this.campaigns = c);
  }

  checkGate(c: Campaign): void {
    this.checking = c.id;
    this.healthService.checkCampaignGate(c.id).subscribe({
      next: () => { this.checking = null; this.load(); },
      error: () => { this.checking = null; }
    });
  }

  release(c: Campaign): void {
    this.checking = c.id;
    this.healthService.releaseCampaign(c.id, 'Manually released by operator').subscribe({
      next: () => { this.checking = null; this.load(); },
      error: () => { this.checking = null; }
    });
  }

  getGateIcon(status: GateStatus): string {
    const icons: Record<GateStatus, string> = {
      go: '🟢', hold: '🟡', rerouted: '🔵', cancelled: '🔴'
    };
    return icons[status] ?? '⚪';
  }
}

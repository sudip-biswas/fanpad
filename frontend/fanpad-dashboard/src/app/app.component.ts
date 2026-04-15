import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as signalR from '@microsoft/signalr';
import { SignalRService } from './services/signalr.service';
import { ServiceStatusGridComponent } from './components/service-status-grid/service-status-grid.component';
import { AgentLogComponent } from './components/agent-log/agent-log.component';
import { RoutingStateComponent } from './components/routing-state/routing-state.component';
import { CampaignGateComponent } from './components/campaign-gate/campaign-gate.component';
import { SimulationControlComponent } from './components/simulation-control/simulation-control.component';
import { IncidentTimelineComponent } from './components/incident-timeline/incident-timeline.component';
import { HealthChartComponent } from './components/health-chart/health-chart.component';
import { environment } from '../environments/environment';

type Tab = 'status' | 'incidents' | 'chart' | 'routing' | 'campaigns' | 'agent' | 'simulation';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    ServiceStatusGridComponent,
    AgentLogComponent,
    RoutingStateComponent,
    CampaignGateComponent,
    SimulationControlComponent,
    IncidentTimelineComponent,
    HealthChartComponent,
  ],
  template: `
    <div class="shell">
      <!-- Top nav -->
      <header class="topbar">
        <div class="brand">
          <span class="logo">🎵</span>
          <span class="brand-name">Fanpad</span>
          <span class="brand-sub">Service Health Monitor</span>
        </div>
        <div class="connection-status" [class]="connectionClass">
          <span class="dot"></span>
          {{ connectionLabel }}
        </div>
      </header>

      <!-- Tab nav -->
      <nav class="tabnav">
        <button
          *ngFor="let t of tabs"
          class="tab"
          [class.active]="activeTab === t.id"
          (click)="activeTab = t.id"
        >
          {{ t.icon }} {{ t.label }}
          <span class="tab-badge" *ngIf="t.badge">{{ t.badge }}</span>
        </button>
      </nav>

      <!-- Content -->
      <main class="content">
        <app-service-status-grid  *ngIf="activeTab === 'status'" />
        <app-incident-timeline    *ngIf="activeTab === 'incidents'" />
        <app-health-chart         *ngIf="activeTab === 'chart'" />
        <app-routing-state        *ngIf="activeTab === 'routing'" />
        <app-campaign-gate        *ngIf="activeTab === 'campaigns'" />
        <app-agent-log            *ngIf="activeTab === 'agent'" />
        <app-simulation-control   *ngIf="activeTab === 'simulation'" />
      </main>
    </div>
  `,
  styles: [`
    :host { display: block; font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; }
    * { box-sizing: border-box; }

    .shell { min-height: 100vh; background: #f8fafc; }

    /* ── Topbar ─────────────────────────────────────────────────────────── */
    .topbar {
      background: #1e293b; color: white; padding: 0 24px;
      height: 56px; display: flex; align-items: center; justify-content: space-between;
      box-shadow: 0 2px 8px rgba(0,0,0,0.25); position: sticky; top: 0; z-index: 100;
    }
    .brand { display: flex; align-items: center; gap: 10px; }
    .logo  { font-size: 22px; }
    .brand-name { font-size: 18px; font-weight: 700; letter-spacing: -0.3px; }
    .brand-sub  { font-size: 12px; color: #94a3b8; margin-left: 4px; }

    .connection-status { display: flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 500; }
    .connection-status .dot { width: 8px; height: 8px; border-radius: 50%; background: #9ca3af; transition: background 0.3s; }
    .connection-status.connected    .dot { background: #22c55e; }
    .connection-status.reconnecting .dot { background: #f59e0b; animation: pulse 1s infinite; }
    .connection-status.disconnected .dot { background: #ef4444; }

    /* ── Tab nav ────────────────────────────────────────────────────────── */
    .tabnav {
      background: white; border-bottom: 1px solid #e2e8f0;
      padding: 0 24px; display: flex; gap: 2px;
      position: sticky; top: 56px; z-index: 99;
      box-shadow: 0 1px 4px rgba(0,0,0,0.04);
      overflow-x: auto; scrollbar-width: none;
    }
    .tabnav::-webkit-scrollbar { display: none; }

    .tab {
      padding: 14px 18px; border: none; background: transparent; cursor: pointer;
      font-size: 13px; font-weight: 500; color: #64748b;
      border-bottom: 2px solid transparent; transition: all 0.15s;
      white-space: nowrap; display: flex; align-items: center; gap: 6px;
    }
    .tab:hover  { color: #1e293b; }
    .tab.active { color: #3b82f6; border-bottom-color: #3b82f6; }

    .tab-badge {
      background: #ef4444; color: white; border-radius: 10px;
      padding: 1px 6px; font-size: 10px; font-weight: 700;
    }

    /* ── Content ────────────────────────────────────────────────────────── */
    .content { padding: 24px; max-width: 1440px; margin: 0 auto; }

    @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `]
})
export class AppComponent implements OnInit {
  activeTab: Tab = 'status';
  connectionLabel = 'Connecting...';
  connectionClass = 'connecting';

  tabs: { id: Tab; label: string; icon: string; badge?: number }[] = [
    { id: 'status',     label: 'Service Status',    icon: '📊' },
    { id: 'incidents',  label: 'Incidents',          icon: '🚨' },
    { id: 'chart',      label: 'Health History',     icon: '📈' },
    { id: 'routing',    label: 'Routing',            icon: '🔀' },
    { id: 'campaigns',  label: 'Campaigns',          icon: '📣' },
    { id: 'agent',      label: 'Agent Log',          icon: '🤖' },
    { id: 'simulation', label: 'Simulation',         icon: '🧪' },
  ];

  constructor(private signalR: SignalRService) {}

  ngOnInit(): void {
    this.signalR.start(environment.signalRHub);

    this.signalR.connectionState$.subscribe((state: signalR.HubConnectionState) => {
      const map: Record<string, [string, string]> = {
        'Connected':    ['Connected',      'connected'],
        'Reconnecting': ['Reconnecting...','reconnecting'],
        'Disconnected': ['Disconnected',   'disconnected'],
        'Connecting':   ['Connecting...',  'connecting'],
      };
      const [label, cls] = map[state] ?? ['Unknown', 'connecting'];
      this.connectionLabel = label;
      this.connectionClass = cls;
    });
  }
}

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SimulationScenarioInfo, SimulationStatus } from '../../models';

@Component({
  selector: 'app-simulation-control',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="sim-panel">
      <div class="sim-header">
        <h2>🧪 Failure Simulation</h2>
        <div class="sim-status" [class.active]="status?.isActive">
          {{ status?.isActive ? 'SIMULATION ACTIVE' : 'No simulation' }}
        </div>
      </div>

      <p class="sim-desc">
        Inject failure scenarios to test agent behavior. Select a scenario and trigger
        to see the agent detect the problem and produce a work plan.
      </p>

      <!-- Active scenario banner -->
      <div class="active-banner" *ngIf="status?.isActive">
        <strong>Active:</strong> {{ formatScenarioName(status!.activeScenario) }}
        <br><small>{{ status?.description }}</small>
        <button class="btn-clear" (click)="clearScenario()" [disabled]="loading">
          ✕ Clear Simulation
        </button>
      </div>

      <!-- Scenario groups -->
      <div class="scenario-groups">
        <div class="scenario-group">
          <h3>📧 Email Scenarios</h3>
          <div class="scenario-list">
            <div
              *ngFor="let s of emailScenarios"
              class="scenario-card"
              [class.selected]="selectedScenario === s.id"
              (click)="selectScenario(s)"
            >
              <div class="scenario-name">{{ s.name }}</div>
              <div class="scenario-desc">{{ s.description }}</div>
              <div class="affected-tags">
                <span class="tag" *ngFor="let svc of s.affectedServices">{{ svc }}</span>
              </div>
            </div>
          </div>
        </div>

        <div class="scenario-group">
          <h3>📱 SMS Scenarios</h3>
          <div class="scenario-list">
            <div
              *ngFor="let s of smsScenarios"
              class="scenario-card"
              [class.selected]="selectedScenario === s.id"
              (click)="selectScenario(s)"
            >
              <div class="scenario-name">{{ s.name }}</div>
              <div class="scenario-desc">{{ s.description }}</div>
              <div class="affected-tags">
                <span class="tag" *ngFor="let svc of s.affectedServices">{{ svc }}</span>
              </div>
            </div>
          </div>
        </div>

        <div class="scenario-group">
          <h3>⚡ Complex / Cascade Scenarios</h3>
          <div class="scenario-list">
            <div
              *ngFor="let s of complexScenarios"
              class="scenario-card"
              [class.selected]="selectedScenario === s.id"
              (click)="selectScenario(s)"
            >
              <div class="scenario-name">{{ s.name }}</div>
              <div class="scenario-desc">{{ s.description }}</div>
              <div class="affected-tags">
                <span class="tag" *ngFor="let svc of s.affectedServices">{{ svc }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Selected scenario detail -->
      <div class="selected-detail" *ngIf="selected">
        <h3>Selected: {{ selected.name }}</h3>
        <p>{{ selected.description }}</p>
        <div class="trigger-options">
          <label>
            <input type="checkbox" [(ngModel)]="triggerAgent" />
            Automatically trigger agent evaluation after activation
          </label>
        </div>
        <div class="action-buttons">
          <button class="btn-activate" (click)="activateScenario()" [disabled]="loading">
            {{ loading ? 'Activating...' : '▶ Activate Scenario' }}
          </button>
        </div>
      </div>

      <div class="result-message success" *ngIf="resultMessage">
        ✓ {{ resultMessage }}
      </div>
    </div>
  `,
  styles: [`
    .sim-panel { padding: 16px; }
    .sim-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
    .sim-header h2 { margin: 0; font-size: 20px; font-weight: 600; }
    .sim-status { padding: 4px 12px; border-radius: 20px; font-size: 12px; font-weight: 600; background: #f3f4f6; color: #9ca3af; }
    .sim-status.active { background: #fef3c7; color: #92400e; }
    .sim-desc { color: #6b7280; font-size: 14px; margin-bottom: 16px; }

    .active-banner { background: #fff3cd; border: 1px solid #ffc107; border-radius: 8px; padding: 12px 16px; margin-bottom: 16px; }
    .active-banner strong { color: #856404; }
    .active-banner small { color: #6b7280; }
    .btn-clear { margin-top: 8px; padding: 4px 12px; background: transparent; border: 1px solid #d97706; color: #d97706; border-radius: 4px; cursor: pointer; font-size: 12px; }

    .scenario-groups { display: flex; flex-direction: column; gap: 20px; margin-bottom: 20px; }
    .scenario-group h3 { margin: 0 0 10px 0; font-size: 14px; font-weight: 600; color: #374151; }
    .scenario-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 10px; }

    .scenario-card {
      background: white; border: 2px solid #e5e7eb; border-radius: 8px; padding: 12px;
      cursor: pointer; transition: all 0.2s;
    }
    .scenario-card:hover { border-color: #3b82f6; box-shadow: 0 2px 8px rgba(59,130,246,0.15); }
    .scenario-card.selected { border-color: #3b82f6; background: #eff6ff; }
    .scenario-name { font-weight: 600; font-size: 13px; color: #1f2937; margin-bottom: 4px; }
    .scenario-desc { font-size: 12px; color: #6b7280; line-height: 1.5; margin-bottom: 8px; }
    .affected-tags { display: flex; gap: 4px; flex-wrap: wrap; }
    .tag { background: #e5e7eb; color: #374151; padding: 2px 6px; border-radius: 4px; font-size: 10px; font-weight: 600; text-transform: uppercase; }

    .selected-detail { background: white; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px; margin-bottom: 16px; }
    .selected-detail h3 { margin: 0 0 8px 0; font-size: 15px; color: #1f2937; }
    .selected-detail p { color: #6b7280; font-size: 13px; margin: 0 0 12px 0; }
    .trigger-options { margin-bottom: 12px; font-size: 13px; color: #374151; }
    .trigger-options label { display: flex; align-items: center; gap: 6px; cursor: pointer; }

    .action-buttons { display: flex; gap: 10px; }
    .btn-activate { padding: 10px 24px; background: #3b82f6; color: white; border: none; border-radius: 6px; cursor: pointer; font-weight: 600; font-size: 14px; }
    .btn-activate:hover { background: #2563eb; }
    .btn-activate:disabled { opacity: 0.6; cursor: not-allowed; }

    .result-message { padding: 10px 16px; border-radius: 6px; font-size: 13px; }
    .result-message.success { background: #f0fdf4; border: 1px solid #bbf7d0; color: #166534; }
  `]
})
export class SimulationControlComponent implements OnInit {
  status: SimulationStatus | null = null;
  selectedScenario: string | null = null;
  selected: SimulationScenarioInfo | null = null;
  triggerAgent = true;
  loading = false;
  resultMessage: string | null = null;

  emailScenarios: SimulationScenarioInfo[] = [];
  smsScenarios: SimulationScenarioInfo[] = [];
  complexScenarios: SimulationScenarioInfo[] = [];

  private emailKeywords = ['mailgun', 'ses', 'email', 'ses'];
  private smsKeywords = ['twilio'];
  private complexKeywords = ['cascade', 'both', 'then', 'intermittent', 'all', 'recovering'];

  constructor(private healthService: HealthMonitorService) {}

  ngOnInit(): void {
    this.loadStatus();
  }

  loadStatus(): void {
    this.healthService.getSimulationStatus().subscribe(s => {
      this.status = s;
      // Categorize scenarios
      this.emailScenarios = s.scenarios.filter(sc =>
        sc.affectedServices.some(svc => ['mailgun', 'ses'].includes(svc)) &&
        !this.isComplex(sc.id)
      );
      this.smsScenarios = s.scenarios.filter(sc =>
        sc.affectedServices.some(svc => svc === 'twilio') &&
        !this.isComplex(sc.id)
      );
      this.complexScenarios = s.scenarios.filter(sc => this.isComplex(sc.id));
    });
  }

  isComplex(id: string): boolean {
    const lower = id.toLowerCase();
    return lower.includes('cascade') || lower.includes('both') || lower.includes('then') ||
           lower.includes('intermittent') || lower.includes('all') || lower.includes('recovering');
  }

  selectScenario(s: SimulationScenarioInfo): void {
    this.selectedScenario = s.id;
    this.selected = s;
    this.resultMessage = null;
  }

  activateScenario(): void {
    if (!this.selected) return;
    this.loading = true;
    this.healthService.activateScenario(this.selected.id, this.triggerAgent).subscribe({
      next: (res) => {
        this.loading = false;
        this.resultMessage = `Scenario "${this.selected?.name}" activated. Agent evaluated: ${res.agentDecision?.decision ?? 'pending'}`;
        this.loadStatus();
      },
      error: () => { this.loading = false; }
    });
  }

  clearScenario(): void {
    this.loading = true;
    this.healthService.clearSimulation().subscribe({
      next: () => {
        this.loading = false;
        this.resultMessage = 'Simulation cleared. All services returning to nominal state.';
        this.selectedScenario = null;
        this.selected = null;
        this.loadStatus();
      },
      error: () => { this.loading = false; }
    });
  }

  formatScenarioName(s: string): string {
    return s.replace(/([A-Z])/g, ' $1').trim();
  }
}

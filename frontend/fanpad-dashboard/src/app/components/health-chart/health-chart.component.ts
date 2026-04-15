import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  TimeScale,
  Filler,
  Tooltip,
  Legend,
  CategoryScale,
  ChartDataset,
} from 'chart.js';
import 'chartjs-adapter-date-fns';
import { HealthMonitorService } from '../../services/health-monitor.service';
import { SignalRService } from '../../services/signalr.service';
import { HealthCheckRecord, Provider } from '../../models';

// Register only what we need (tree-shaking friendly)
Chart.register(
  LineController, LineElement, PointElement,
  LinearScale, TimeScale, CategoryScale,
  Filler, Tooltip, Legend
);

type Metric = 'latencyMs' | 'successRate' | 'errorRate';

const PROVIDER_COLORS: Record<Provider, string> = {
  mailgun: '#3b82f6',
  ses:     '#8b5cf6',
  twilio:  '#10b981',
  sns:     '#f59e0b',
};

const PROVIDER_LABELS: Record<Provider, string> = {
  mailgun: 'Mailgun',
  ses:     'AWS SES',
  twilio:  'Twilio',
  sns:     'SNS',
};

@Component({
  selector: 'app-health-chart',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="chart-panel">
      <div class="panel-header">
        <h2>Health History</h2>
        <div class="controls">
          <div class="control-group">
            <label>Metric</label>
            <select [(ngModel)]="selectedMetric" (ngModelChange)="onMetricChange()">
              <option value="latencyMs">Latency (ms)</option>
              <option value="successRate">Success Rate (%)</option>
              <option value="errorRate">Error Rate (%)</option>
            </select>
          </div>
          <div class="control-group">
            <label>Window</label>
            <select [(ngModel)]="selectedMinutes" (ngModelChange)="loadData()">
              <option [value]="15">15 min</option>
              <option [value]="30">30 min</option>
              <option [value]="60">1 hour</option>
              <option [value]="180">3 hours</option>
            </select>
          </div>
          <div class="provider-toggles">
            <button
              *ngFor="let p of allProviders"
              class="provider-btn"
              [class.active]="selectedProviders.has(p)"
              [style.--color]="PROVIDER_COLORS[p]"
              (click)="toggleProvider(p)"
            >
              {{ PROVIDER_LABELS[p] }}
            </button>
          </div>
        </div>
      </div>

      <!-- Threshold legend -->
      <div class="threshold-legend" *ngIf="selectedMetric !== 'latencyMs'">
        <span class="thr-item warn">⚠ Warn ({{ selectedMetric === 'successRate' ? '< 90%' : '> 10%' }})</span>
        <span class="thr-item crit">🔴 Critical ({{ selectedMetric === 'successRate' ? '< 70%' : '> 30%' }})</span>
      </div>
      <div class="threshold-legend" *ngIf="selectedMetric === 'latencyMs'">
        <span class="thr-item warn">⚠ Warn (&gt; 2000ms)</span>
        <span class="thr-item crit">🔴 Critical (&gt; 5000ms)</span>
      </div>

      <!-- Chart -->
      <div class="chart-wrapper">
        <canvas #chartCanvas></canvas>
        <div class="loading-overlay" *ngIf="loading">
          <div class="spinner"></div>
        </div>
        <div class="no-data" *ngIf="!loading && noData">
          No data for the selected window. Trigger an evaluation to generate data.
        </div>
      </div>

      <!-- Simulation notice -->
      <div class="sim-notice" *ngIf="hasSimulatedData">
        🧪 Some data points are simulated (shown with dashed outline on markers).
      </div>
    </div>
  `,
  styles: [`
    .chart-panel { padding: 16px; }

    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; flex-wrap: wrap; gap: 12px; }
    .panel-header h2 { margin: 0; font-size: 20px; font-weight: 600; }

    .controls { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }

    .control-group { display: flex; flex-direction: column; gap: 2px; }
    .control-group label { font-size: 11px; font-weight: 600; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.5px; }
    .control-group select { padding: 5px 10px; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 13px; background: white; color: #374151; cursor: pointer; }
    .control-group select:focus { outline: none; border-color: #3b82f6; }

    .provider-toggles { display: flex; gap: 6px; align-items: center; }
    .provider-btn {
      padding: 5px 12px; border-radius: 20px; border: 2px solid var(--color, #ccc);
      background: white; color: #374151; font-size: 12px; font-weight: 600;
      cursor: pointer; transition: all 0.15s; opacity: 0.4;
    }
    .provider-btn.active { background: var(--color); color: white; opacity: 1; }

    .threshold-legend { display: flex; gap: 16px; margin-bottom: 12px; font-size: 12px; }
    .thr-item { font-weight: 500; }
    .thr-item.warn { color: #d97706; }
    .thr-item.crit { color: #dc2626; }

    .chart-wrapper {
      position: relative; background: white; border-radius: 12px;
      padding: 20px; box-shadow: 0 2px 8px rgba(0,0,0,0.06);
      height: 320px;
    }
    .chart-wrapper canvas { width: 100% !important; height: 100% !important; }

    .loading-overlay {
      position: absolute; inset: 0; background: rgba(255,255,255,0.8);
      display: flex; align-items: center; justify-content: center;
      border-radius: 12px;
    }
    .spinner {
      width: 32px; height: 32px; border: 3px solid #e2e8f0;
      border-top-color: #3b82f6; border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    .no-data {
      position: absolute; inset: 0; display: flex; align-items: center;
      justify-content: center; color: #94a3b8; font-size: 14px;
    }

    .sim-notice { margin-top: 10px; font-size: 12px; color: #6b7280; padding: 6px 12px; background: #ede9fe; border-radius: 6px; }

    @keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  `]
})
export class HealthChartComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('chartCanvas') canvasRef!: ElementRef<HTMLCanvasElement>;

  readonly PROVIDER_COLORS = PROVIDER_COLORS;
  readonly PROVIDER_LABELS = PROVIDER_LABELS;
  readonly allProviders: Provider[] = ['mailgun', 'ses', 'twilio'];

  selectedMetric: Metric = 'latencyMs';
  selectedMinutes = 60;
  selectedProviders = new Set<Provider>(['mailgun', 'ses', 'twilio']);

  loading = false;
  noData = false;
  hasSimulatedData = false;

  private chart: Chart | null = null;
  private rawData: HealthCheckRecord[] = [];
  private subs = new Subscription();

  constructor(
    private healthService: HealthMonitorService,
    private signalR: SignalRService
  ) {}

  ngOnInit(): void {
    this.subs.add(this.signalR.agentDecision$.subscribe(() => this.loadData()));
    this.subs.add(this.signalR.simulationActivated$.subscribe(() => this.loadData()));
    this.subs.add(this.signalR.simulationCleared$.subscribe(() => this.loadData()));
  }

  ngAfterViewInit(): void {
    this.initChart();
    this.loadData();
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
    this.subs.unsubscribe();
  }

  private initChart(): void {
    const ctx = this.canvasRef.nativeElement.getContext('2d')!;

    this.chart = new Chart(ctx, {
      type: 'line',
      data: { datasets: [] },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 300 },
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: {
            position: 'top',
            labels: { font: { size: 12 }, usePointStyle: true, padding: 16 }
          },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const val = ctx.parsed.y;
                if (val == null) return '';
                const suffix = this.selectedMetric === 'latencyMs' ? 'ms' : '%';
                return ` ${ctx.dataset.label}: ${val.toFixed(0)}${suffix}`;
              }
            }
          }
        },
        scales: {
          x: {
            type: 'time',
            time: { unit: 'minute', displayFormats: { minute: 'HH:mm' } },
            grid: { color: '#f1f5f9' },
            ticks: { font: { size: 11 }, color: '#94a3b8', maxTicksLimit: 8 }
          },
          y: {
            grid: { color: '#f1f5f9' },
            ticks: {
              font: { size: 11 }, color: '#94a3b8',
              callback: (v) => `${v}${this.selectedMetric === 'latencyMs' ? 'ms' : '%'}`
            }
          }
        }
      }
    });
  }

  loadData(): void {
    this.loading = true;
    this.healthService.getHistory(undefined, this.selectedMinutes).subscribe({
      next: data => {
        this.rawData = data;
        this.hasSimulatedData = data.some(d => d.isSimulated);
        this.updateChart();
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  onMetricChange(): void {
    this.updateChart();
  }

  toggleProvider(p: Provider): void {
    if (this.selectedProviders.has(p)) {
      if (this.selectedProviders.size > 1) this.selectedProviders.delete(p);
    } else {
      this.selectedProviders.add(p);
    }
    this.updateChart();
  }

  private updateChart(): void {
    if (!this.chart) return;

    const datasets: ChartDataset<'line'>[] = [];

    for (const provider of this.allProviders) {
      if (!this.selectedProviders.has(provider)) continue;

      const records = this.rawData
        .filter(r => r.provider === provider)
        .sort((a, b) => new Date(a.checkedAt).getTime() - new Date(b.checkedAt).getTime());

      if (!records.length) continue;

      const color = PROVIDER_COLORS[provider];
      const data = records
        .map(r => ({ x: new Date(r.checkedAt).getTime(), y: r[this.selectedMetric] ?? null }))
        .filter(p => p.y !== null) as { x: number; y: number }[];

      datasets.push({
        label: PROVIDER_LABELS[provider],
        data,
        borderColor: color,
        backgroundColor: `${color}18`,
        pointBackgroundColor: records.map(r => r.isSimulated ? 'white' : color),
        pointBorderColor: color,
        pointBorderWidth: records.map(r => r.isSimulated ? 2 : 1),
        pointRadius: 4,
        tension: 0.3,
        fill: false,
      });
    }

    this.noData = datasets.length === 0 || datasets.every(d => !d.data.length);
    this.chart.data.datasets = datasets;

    // Add threshold annotations via y-scale
    const yMax = this.getYMax(datasets);
    this.chart.options.scales!['y']!.max = yMax || undefined;
    this.addThresholdLines();
    this.chart.update();
  }

  private addThresholdLines(): void {
    if (!this.chart) return;
    // Threshold lines rendered as dashed reference lines via plugin annotation
    // Simplified: set sensible y-axis range per metric
    const scale = this.chart.options.scales!['y']!;
    if (this.selectedMetric === 'latencyMs') {
      scale.min = 0;
      scale.suggestedMax = 8000;
    } else {
      scale.min = 0;
      scale.max = 100;
    }
  }

  private getYMax(datasets: ChartDataset<'line'>[]): number {
    let max = 0;
    for (const ds of datasets) {
      for (const pt of ds.data as { x: number; y: number }[]) {
        if (pt.y > max) max = pt.y;
      }
    }
    return max * 1.15;
  }
}

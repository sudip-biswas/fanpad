import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import {
  AgentDecisionEvent,
  CampaignGateChangedEvent,
  FailoverExecutedEvent,
  SimulationActivatedEvent,
} from '../models';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hub!: signalR.HubConnection;

  readonly connectionState$ = new BehaviorSubject<signalR.HubConnectionState>(
    signalR.HubConnectionState.Disconnected
  );

  // ── Event streams ─────────────────────────────────────────────────────────
  readonly agentDecision$        = new Subject<AgentDecisionEvent>();
  readonly incidentOpened$       = new Subject<any>();
  readonly incidentResolved$     = new Subject<any>();
  readonly failoverRecommended$  = new Subject<any>();
  readonly failoverExecuted$     = new Subject<FailoverExecutedEvent>();
  readonly campaignGateChanged$  = new Subject<CampaignGateChangedEvent>();
  readonly simulationActivated$  = new Subject<SimulationActivatedEvent>();
  readonly simulationCleared$    = new Subject<{ previousScenario: string }>();

  start(hubUrl: string): void {
    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.hub.onreconnecting(() => this.connectionState$.next(signalR.HubConnectionState.Reconnecting));
    this.hub.onreconnected(() => this.connectionState$.next(signalR.HubConnectionState.Connected));
    this.hub.onclose(() => this.connectionState$.next(signalR.HubConnectionState.Disconnected));

    this.hub.on('ServiceHealthUpdate',  (d) => this.agentDecision$.next(d));
    this.hub.on('AgentDecisionLogged',  (d) => this.agentDecision$.next(d));
    this.hub.on('IncidentOpened',       (d) => this.incidentOpened$.next(d));
    this.hub.on('IncidentResolved',     (d) => this.incidentResolved$.next(d));
    this.hub.on('FailoverRecommended',  (d) => this.failoverRecommended$.next(d));
    this.hub.on('FailoverExecuted',     (d) => this.failoverExecuted$.next(d));
    this.hub.on('CampaignGateChanged',  (d) => this.campaignGateChanged$.next(d));
    this.hub.on('SimulationActivated',  (d) => this.simulationActivated$.next(d));
    this.hub.on('SimulationCleared',    (d) => this.simulationCleared$.next(d));

    this.hub.start()
      .then(() => this.connectionState$.next(signalR.HubConnectionState.Connected))
      .catch(err => console.error('SignalR connection failed:', err));
  }

  ngOnDestroy(): void {
    this.hub?.stop();
  }
}

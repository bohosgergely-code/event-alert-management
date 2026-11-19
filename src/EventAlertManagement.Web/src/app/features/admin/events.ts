import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { CATEGORIES, Category, EventSummary, InjectResult } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';
import { Pager } from '../../shared/pager';

@Component({
  selector: 'app-admin-events',
  imports: [FormsModule, DatePipe, Notice, Pager],
  template: `
    <h1>Events</h1>

    <section class="card">
      <h2 style="margin-top:0">Create a test event</h2>
      <p class="muted small">The event goes through the real pipeline: it is matched against everyone's rules and delivered like any other. It is marked as simulated.</p>
      <app-notice [errors]="injectErrors()" />
      @if (injected(); as r) {
        <div class="notice ok" role="status">“{{ injectedTitle() }}” created. {{ r.notificationsCreated }} alert(s) queued, {{ r.notificationsSuppressed }} suppressed.</div>
      }
      <form (ngSubmit)="inject()" class="stack">
        <div class="row">
          <div><label for="cat">Category</label>
            <select id="cat" name="cat" [(ngModel)]="f.category">@for (c of categories; track c) { <option [value]="c">{{ c }}</option> }</select></div>
          <div><label for="sev">Severity</label>
            <select id="sev" name="sev" [(ngModel)]="f.severity">@for (s of [1,2,3,4,5]; track s) { <option [ngValue]="s">{{ s }}</option> }</select></div>
          <div class="grow"><label for="title">Title</label><input id="title" name="title" [(ngModel)]="f.title" required maxlength="300" /></div>
        </div>
        <div class="row">
          <div class="grow"><label for="summary">Summary</label><input id="summary" name="summary" [(ngModel)]="f.summary" /></div>
          <div class="grow"><label for="loc">Location</label><input id="loc" name="loc" [(ngModel)]="f.location" /></div>
          <div class="grow"><label for="url">Link</label><input id="url" name="url" type="url" [(ngModel)]="f.url" placeholder="https://" /></div>
        </div>
        @if (f.category === 'Disasters') {
          <div class="row">
            <div><label for="mag">Magnitude</label><input id="mag" name="mag" type="number" step="0.1" [(ngModel)]="f.magnitude" /></div>
            <div><label for="et">Event type</label><input id="et" name="et" [(ngModel)]="f.eventType" placeholder="earthquake" /></div>
          </div>
        }
        @if (f.category === 'Markets') {
          <div class="row">
            <div><label for="tk">Ticker</label><input id="tk" name="tk" [(ngModel)]="f.ticker" placeholder="BTC" /></div>
            <div><label for="chg">Change (%)</label><input id="chg" name="chg" type="number" step="0.1" [(ngModel)]="f.changePercent" /></div>
          </div>
        }
        <button type="submit" class="primary" [disabled]="injecting() || !f.title.trim()">Create event</button>
      </form>
    </section>

    <h2>Stored events</h2>
    <div class="row" style="margin-bottom:1rem">
      <div><label for="fc">Category</label>
        <select id="fc" name="fc" [(ngModel)]="category" (ngModelChange)="load(1)"><option value="">All</option>@for (c of categories; track c) { <option [value]="c">{{ c }}</option> }</select></div>
      <div><label for="fs">Origin</label>
        <select id="fs" name="fs" [(ngModel)]="simulated" (ngModelChange)="load(1)"><option value="">All</option><option value="false">Real sources</option><option value="true">Simulated</option></select></div>
    </div>
    <app-notice [errors]="errors()" />
    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (items().length === 0) { <div class="card empty">No events.</div> }
    @else {
      <div class="scroll"><table>
        <thead><tr><th>Occurred</th><th>Category</th><th>Sev.</th><th>Title</th><th>Source</th></tr></thead>
        <tbody>@for (e of items(); track e.id) {
          <tr><td class="small">{{ e.occurredAt | date: 'medium' }}</td><td>{{ e.category }}</td><td>{{ e.severity }}</td>
            <td>{{ e.title }}@if (e.location) { <div class="small muted">{{ e.location }}</div> }</td>
            <td>{{ e.sourceKey }} @if (e.isSimulated) { <span class="badge Pending">simulated</span> }</td></tr>
        }</tbody>
      </table></div>
      <app-pager [page]="page()" [pageSize]="20" [total]="total()" (go)="load($event)" />
    }
  `,
})
export class AdminEvents implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly categories = CATEGORIES;
  protected category: Category | '' = '';
  protected simulated: '' | 'true' | 'false' = '';
  protected f = { category: 'Disasters' as Category, severity: 3, title: '', summary: '', location: '', url: '', magnitude: null as number | null, eventType: '', ticker: '', changePercent: null as number | null };

  protected readonly items = signal<EventSummary[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly errors = signal<string[]>([]);
  protected readonly injectErrors = signal<string[]>([]);
  protected readonly injected = signal<InjectResult | null>(null);
  protected readonly injectedTitle = signal('');
  protected readonly injecting = signal(false);

  ngOnInit(): void { this.load(1); }

  protected load(page: number): void {
    this.loading.set(true);
    this.api.adminEvents({ category: this.category, simulated: this.simulated, page }).subscribe({
      next: (r) => { this.items.set(r.items); this.page.set(r.page); this.total.set(r.total); this.loading.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }

  protected inject(): void {
    this.injecting.set(true); this.injectErrors.set([]); this.injected.set(null);
    const f = this.f;
    this.api.inject({
      category: f.category, severity: f.severity, title: f.title.trim(),
      summary: f.summary || null, url: f.url || null, location: f.location || null,
      magnitude: f.category === 'Disasters' ? f.magnitude : null, eventType: f.category === 'Disasters' ? f.eventType || null : null,
      ticker: f.category === 'Markets' ? f.ticker || null : null, changePercent: f.category === 'Markets' ? f.changePercent : null,
    }).subscribe({
      next: (r) => { this.injectedTitle.set(f.title.trim()); this.injected.set(r); this.injecting.set(false); this.f.title = ''; this.load(1); },
      error: (e: unknown) => { this.injectErrors.set(describeError(e)); this.injecting.set(false); },
    });
  }
}

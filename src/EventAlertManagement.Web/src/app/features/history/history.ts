import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { Notification, NotificationStatus, STATUSES } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';
import { Pager } from '../../shared/pager';
import { autoRefresh } from '../../shared/auto-refresh';

@Component({
  selector: 'app-history',
  imports: [FormsModule, DatePipe, Notice, Pager],
  template: `
    <h1>History</h1>
    <p class="muted">Every alert your rules produced, and what happened to it.</p>
    <div class="row" style="margin-bottom:1rem">
      <div>
        <label for="status">Show</label>
        <select id="status" name="status" [(ngModel)]="status" (ngModelChange)="load(1)">
          <option value="">All</option>
          @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
        </select>
      </div>
    </div>
    <app-notice [errors]="errors()" />

    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (items().length === 0) { <div class="card empty">Nothing here yet. Alerts appear once an event matches one of your rules.</div> }
    @else {
      <div class="scroll"><table>
        <thead><tr><th>When</th><th>Event</th><th>Rule</th><th>Sent to</th><th>Status</th></tr></thead>
        <tbody>
          @for (n of items(); track n.id) {
            <tr>
              <td class="small">{{ n.createdAt | date: 'medium' }}</td>
              <td>{{ n.eventTitle }}<div class="small muted">Severity {{ n.eventSeverity }}</div></td>
              <td>{{ n.ruleName }}</td>
              <td>{{ n.channelName }} <span class="muted small">({{ n.channelType }})</span></td>
              <td>
                <span [class]="'badge ' + n.status">{{ n.status }}</span>
                @if (n.suppressedReason) { <div class="small muted">{{ n.suppressedReason }}</div> }
                @if (n.status === 'Retrying' && n.nextAttemptAt) { <div class="small muted">Next try {{ n.nextAttemptAt | date: 'shortTime' }}</div> }
                @if (n.lastError && (n.status === 'Failed' || n.status === 'Retrying')) { <div class="small muted">{{ n.lastError }}</div> }
              </td>
            </tr>
          }
        </tbody>
      </table></div>
      <app-pager [page]="page()" [pageSize]="pageSize()" [total]="total()" (go)="load($event)" />
    }
  `,
})
export class History implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly statuses = STATUSES;
  protected status: NotificationStatus | '' = '';
  protected readonly items = signal<Notification[]>([]);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly errors = signal<string[]>([]);

  constructor() { autoRefresh(() => this.load(this.page(), true), 10000); }

  ngOnInit(): void { this.load(1); }

  protected load(page: number, quiet = false): void {
    if (!quiet) { this.loading.set(true); this.errors.set([]); }
    this.api.notifications(this.status, page, this.pageSize()).subscribe({
      next: (r) => { this.items.set(r.items); this.page.set(r.page); this.pageSize.set(r.pageSize); this.total.set(r.total); this.loading.set(false); },
      error: (e: unknown) => { if (!quiet) this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }
}

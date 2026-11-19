import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AdminNotification, NotificationStatus, STATUSES } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';
import { Pager } from '../../shared/pager';
import { autoRefresh } from '../../shared/auto-refresh';

@Component({
  selector: 'app-admin-notifications',
  imports: [FormsModule, DatePipe, Notice, Pager],
  template: `
    <h1>Deliveries</h1>
    <p class="muted">Every alert the system decided to send, with each attempt. Open a row to see why something did or did not arrive.</p>
    <form class="row" style="margin-bottom:1rem" (ngSubmit)="load(1)">
      <div><label for="status">Status</label>
        <select id="status" name="status" [(ngModel)]="status" (ngModelChange)="load(1)">
          <option value="">All</option>@for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
        </select></div>
      <div class="grow"><label for="user">User email</label><input id="user" name="user" [(ngModel)]="userEmail" placeholder="someone@example.com" /></div>
      <div><label for="ctype">Channel</label><input id="ctype" name="ctype" [(ngModel)]="channelType" placeholder="Email" /></div>
      <button type="submit">Filter</button>
      <button type="button" class="right" (click)="load(page(), true)">Refresh</button>
    </form>
    <p class="small muted">This list refreshes by itself every few seconds.</p>
    <app-notice [errors]="errors()" [message]="message()" />

    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (items().length === 0) { <div class="card empty">No deliveries match.</div> }
    @else {
      <div class="scroll"><table>
        <thead><tr><th>When</th><th>User</th><th>Event</th><th>Rule</th><th>Channel</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (n of items(); track n.id) {
            <tr>
              <td class="small">{{ n.createdAt | date: 'short' }}</td>
              <td>{{ n.userEmail }}</td>
              <td>{{ n.eventTitle }}</td>
              <td>{{ n.ruleName }}</td>
              <td>{{ n.channelType }}</td>
              <td><span [class]="'badge ' + n.status">{{ n.status }}</span>
                @if (n.suppressedReason) { <div class="small muted">{{ n.suppressedReason }}</div> }</td>
              <td style="white-space:nowrap">
                <button type="button" (click)="toggle(n.id)" [attr.aria-expanded]="open() === n.id">{{ open() === n.id ? 'Hide' : 'Attempts (' + n.attempts.length + ')' }}</button>
                @if (n.status === 'Failed') { <button type="button" class="primary" (click)="retry(n)">Retry</button> }
              </td>
            </tr>
            @if (open() === n.id) {
              <tr><td colspan="7">
                @if (n.attempts.length === 0) { <span class="muted small">No attempts were made.</span> }
                @else {
                  <table>
                    <thead><tr><th>#</th><th>Started</th><th>Took</th><th>Result</th><th>Message</th></tr></thead>
                    <tbody>@for (a of n.attempts; track a.attemptNumber) {
                      <tr><td>{{ a.attemptNumber }}</td><td class="small">{{ a.startedAt | date: 'medium' }}</td><td>{{ a.durationMs }} ms</td>
                        <td><span [class]="a.outcome === 'Success' ? 'badge Sent' : a.outcome === 'TransientFailure' ? 'badge Retrying' : 'badge Failed'">{{ a.outcome }}</span></td>
                        <td class="small">{{ a.errorMessage }}</td></tr>
                    }</tbody>
                  </table>
                }
              </td></tr>
            }
          }
        </tbody>
      </table></div>
      <app-pager [page]="page()" [pageSize]="pageSize()" [total]="total()" (go)="load($event)" />
    }
  `,
})
export class AdminNotifications implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly statuses = STATUSES;
  protected status: NotificationStatus | '' = '';
  protected userEmail = '';
  protected channelType = '';
  protected readonly items = signal<AdminNotification[]>([]);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly total = signal(0);
  protected readonly open = signal('');
  protected readonly loading = signal(true);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal('');

  constructor() { autoRefresh(() => this.load(this.page(), true)); }

  ngOnInit(): void { this.load(1); }

  protected toggle(id: string): void { this.open.update((o) => (o === id ? '' : id)); }

  /** <code>quiet</code> reloads without the "Loading…" flash and keeps an opened row open. */
  protected load(page: number, quiet = false): void {
    if (!quiet) { this.loading.set(true); this.errors.set([]); }
    this.api.adminNotifications({ status: this.status, userEmail: this.userEmail.trim(), channelType: this.channelType.trim(), page }).subscribe({
      next: (r) => { this.items.set(r.items); this.page.set(r.page); this.pageSize.set(r.pageSize); this.total.set(r.total); this.loading.set(false); },
      error: (e: unknown) => { if (!quiet) this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }

  protected retry(n: AdminNotification): void {
    this.errors.set([]); this.message.set('');
    this.api.retry(n.id).subscribe({
      next: () => { this.message.set('Queued for another attempt. It is sent within a few seconds.'); this.load(this.page()); },
      error: (e: unknown) => this.errors.set(describeError(e)),
    });
  }
}

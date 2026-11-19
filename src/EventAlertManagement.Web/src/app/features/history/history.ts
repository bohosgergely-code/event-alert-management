import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { Notification, NotificationStatus, STATUSES } from '../../core/models';
import { Notice } from '../../shared/notice';
import { Pager } from '../../shared/pager';
import { PagedLoader } from '../../shared/paged-loader';

@Component({
  selector: 'app-history',
  imports: [FormsModule, DatePipe, Notice, Pager],
  template: `
    <h1>History</h1>
    <p class="muted">Every alert your rules produced, and what happened to it.</p>
    <div class="row" style="margin-bottom:1rem">
      <div>
        <label for="status">Show</label>
        <select id="status" name="status" [(ngModel)]="status" (ngModelChange)="list.load(1)">
          <option value="">All</option>
          @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
        </select>
      </div>
    </div>
    <app-notice [errors]="list.errors()" />

    @if (list.loading()) { <p class="muted">Loading…</p> }
    @else if (list.items().length === 0) { <div class="card empty">Nothing here yet. Alerts appear once an event matches one of your rules.</div> }
    @else {
      <div class="scroll"><table>
        <thead><tr><th scope="col">When</th><th scope="col">Event</th><th scope="col">Rule</th><th scope="col">Sent to</th><th scope="col">Status</th></tr></thead>
        <tbody>
          @for (n of list.items(); track n.id) {
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
    }
    <app-pager [page]="list.page()" [pageSize]="list.pageSize()" [total]="list.total()" (go)="list.load($event)" />
  `,
})
export class History implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly statuses = STATUSES;
  protected status: NotificationStatus | '' = '';
  protected readonly list = new PagedLoader<Notification>(
    (page, pageSize) => this.api.notifications(this.status, page, pageSize), inject(DestroyRef), 10_000);

  ngOnInit(): void { this.list.load(1); }
}

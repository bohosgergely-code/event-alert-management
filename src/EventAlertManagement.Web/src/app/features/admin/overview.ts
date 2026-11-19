import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Overview, STATUSES } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-admin-overview',
  imports: [DatePipe, RouterLink, Notice],
  template: `
    <h1>Overview</h1>
    <app-notice [errors]="errors()" />
    @if (data(); as o) {
      <div class="stats">
        <div class="stat"><span class="muted small">Users</span><b>{{ o.users }}</b></div>
        <div class="stat"><span class="muted small">Rules</span><b>{{ o.rules }}</b></div>
        <div class="stat"><span class="muted small">Events stored</span><b>{{ o.events }}</b></div>
        <div class="stat"><span class="muted small">Failed deliveries</span><b>{{ o.failedNotifications }}</b></div>
        <div class="stat"><span class="muted small">Sources with problems</span><b>{{ o.unhealthySources }}</b></div>
      </div>

      <h2>Last 24 hours</h2>
      <div class="row">
        @for (s of statuses; track s) {
          <div class="stat" style="min-width:8rem"><span [class]="'badge ' + s">{{ s }}</span><b>{{ o.notificationsLast24h[s] ?? 0 }}</b></div>
        }
      </div>

      <h2>Recent failures</h2>
      @if (o.recentFailures.length === 0) { <div class="card empty">No failed deliveries. </div> }
      @else {
        <div class="scroll"><table>
          <thead><tr><th>When</th><th>User</th><th>Event</th><th>Channel</th><th>Last error</th></tr></thead>
          <tbody>
            @for (n of o.recentFailures; track n.id) {
              <tr>
                <td class="small">{{ n.createdAt | date: 'short' }}</td>
                <td>{{ n.userEmail }}</td>
                <td>{{ n.eventTitle }}</td>
                <td>{{ n.channelType }}</td>
                <td class="small">{{ n.attempts.length ? n.attempts[n.attempts.length - 1].errorMessage : '' }}</td>
              </tr>
            }
          </tbody>
        </table></div>
        <p><a routerLink="/admin/notifications">Open deliveries to retry them</a></p>
      }
    }
  `,
})
export class AdminOverview implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly statuses = STATUSES;
  protected readonly data = signal<Overview | null>(null);
  protected readonly errors = signal<string[]>([]);

  ngOnInit(): void {
    this.api.overview().subscribe({ next: (o) => this.data.set(o), error: (e: unknown) => this.errors.set(describeError(e)) });
  }
}

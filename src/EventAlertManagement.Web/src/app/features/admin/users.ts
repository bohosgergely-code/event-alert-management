import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { AdminUser } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, Notice],
  template: `
    <h1>Users</h1>
    <p class="muted">Everyone who has signed in, and how much they have set up. To see what happened to someone's alerts, filter the deliveries by their email.</p>
    <app-notice [errors]="errors()" />
    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (users().length === 0) { <div class="card empty">No users yet.</div> }
    @else {
      <div class="scroll"><table>
        <caption class="sr-only">Users</caption>
        <thead><tr><th scope="col">Email</th><th scope="col">Joined</th><th scope="col">Rules</th><th scope="col">Channels</th></tr></thead>
        <tbody>
          @for (u of users(); track u.id) {
            <tr><td>{{ u.email }}</td><td class="small">{{ u.createdAt | date: 'medium' }}</td><td>{{ u.rules }}</td>
              <td>{{ u.channels }}@if (u.rules > 0 && u.channels === 0) { <span class="small" style="color:var(--warn)"> (rules but no channel)</span> }</td></tr>
          }
        </tbody>
      </table></div>
    }
  `,
})
export class AdminUsers implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly loading = signal(true);
  protected readonly errors = signal<string[]>([]);

  ngOnInit(): void {
    this.api.adminUsers().subscribe({
      next: (u) => { this.users.set(u); this.loading.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }
}

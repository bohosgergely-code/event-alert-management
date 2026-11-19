import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { AdminRule } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-admin-rules',
  imports: [DatePipe, Notice],
  template: `
    <h1>Rules</h1>
    <p class="muted">Every rule in the system. A rule that is on but has never produced an alert is the first place to look when someone says "I got nothing".</p>
    <app-notice [errors]="errors()" />
    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (rules().length === 0) { <div class="card empty">No rules yet.</div> }
    @else {
      <div class="scroll"><table>
        <caption class="sr-only">Rules</caption>
        <thead><tr><th scope="col">User</th><th scope="col">Rule</th><th scope="col">Watches</th><th scope="col">Min. severity</th><th scope="col">Cooldown</th><th scope="col">Channels</th><th scope="col">Last alert</th><th scope="col">Status</th></tr></thead>
        <tbody>
          @for (r of rules(); track r.id) {
            <tr>
              <td>{{ r.userEmail }}</td><td>{{ r.name }}</td><td>{{ r.category }}</td><td>{{ r.minSeverity }}</td>
              <td>{{ r.cooldownSeconds === 0 ? 'None' : r.cooldownSeconds / 60 + ' min' }}</td>
              <td>{{ r.channelCount }}</td>
              <td class="small">{{ r.lastNotifiedAt ? (r.lastNotifiedAt | date: 'medium') : 'Never' }}</td>
              <td><span [class]="r.isEnabled ? 'badge Sent' : 'badge Disabled'">{{ r.isEnabled ? 'On' : 'Paused' }}</span></td>
            </tr>
          }
        </tbody>
      </table></div>
    }
  `,
})
export class AdminRules implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly rules = signal<AdminRule[]>([]);
  protected readonly loading = signal(true);
  protected readonly errors = signal<string[]>([]);

  ngOnInit(): void {
    this.api.adminRules().subscribe({
      next: (r) => { this.rules.set(r); this.loading.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }
}

import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { Source } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-admin-sources',
  imports: [FormsModule, DatePipe, Notice],
  template: `
    <h1>Sources</h1>
    <p class="muted">Where events come from, and whether each source is working.</p>
    <app-notice [errors]="errors()" [message]="message()" />
    @if (loading()) { <p class="muted">Loading…</p> }
    @else {
      <div class="scroll"><table>
        <thead><tr><th>Source</th><th>Health</th><th>Last success</th><th>Problem</th><th>Check every</th><th></th></tr></thead>
        <tbody>
          @for (s of sources(); track s.id) {
            <tr>
              <td>{{ s.name }}<div class="small muted mono">{{ s.key }}</div></td>
              <td><span [class]="'badge ' + s.health">{{ s.health }}</span>@if (s.consecutiveFailures > 0) { <div class="small muted">{{ s.consecutiveFailures }} failed in a row</div> }</td>
              <td class="small">{{ s.lastSuccessAt ? (s.lastSuccessAt | date: 'medium') : 'Never' }}</td>
              <td class="small">{{ s.lastError }}</td>
              <td style="min-width:9rem">
                <div class="row" style="flex-wrap:nowrap; gap:.4rem; align-items:center">
                  <input type="number" min="10" max="86400" [name]="'iv-' + s.id" [attr.aria-label]="'Seconds between checks for ' + s.name" [ngModel]="s.pollIntervalSeconds" (ngModelChange)="draft[s.id] = $event" style="width:6rem" /> s
                  <button type="button" (click)="saveInterval(s)">Save</button>
                </div>
              </td>
              <td style="white-space:nowrap">
                <button type="button" (click)="poll(s)" [disabled]="polling() === s.id">{{ polling() === s.id ? 'Checking…' : 'Check now' }}</button>
                <button type="button" (click)="toggle(s)">{{ s.isEnabled ? 'Turn off' : 'Turn on' }}</button>
              </td>
            </tr>
          }
        </tbody>
      </table></div>
    }
  `,
})
export class AdminSources implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly sources = signal<Source[]>([]);
  protected readonly loading = signal(true);
  protected readonly polling = signal('');
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal('');
  protected draft: Record<string, number> = {};

  ngOnInit(): void { this.reload(); }

  private reload(): void {
    this.api.sources().subscribe({
      next: (s) => { this.sources.set(s); this.loading.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }

  protected poll(s: Source): void {
    this.begin(); this.polling.set(s.id);
    this.api.pollSource(s.id).subscribe({
      next: (r) => {
        this.polling.set('');
        if (r.success) this.message.set(`${s.name}: checked, ${r.newEvents} new event(s).`); else this.errors.set([`${s.name}: ${r.error ?? 'the check failed.'}`]);
        this.reload();
      },
      error: (e: unknown) => { this.polling.set(''); this.errors.set(describeError(e)); },
    });
  }

  protected toggle(s: Source): void {
    this.begin();
    this.api.updateSource(s.id, { isEnabled: !s.isEnabled }).subscribe({ next: () => this.reload(), error: (e: unknown) => this.errors.set(describeError(e)) });
  }

  protected saveInterval(s: Source): void {
    const seconds = this.draft[s.id];
    if (seconds === undefined) return;
    this.begin();
    this.api.updateSource(s.id, { pollIntervalSeconds: Number(seconds) }).subscribe({
      next: () => { delete this.draft[s.id]; this.message.set(`${s.name}: interval saved.`); this.reload(); },
      error: (e: unknown) => this.errors.set(describeError(e)),
    });
  }

  private begin(): void { this.errors.set([]); this.message.set(''); }
}

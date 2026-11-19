import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Channel, Rule } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-rules',
  imports: [RouterLink, DatePipe, Notice],
  template: `
    <div class="row" style="align-items:center; margin-bottom:.5rem">
      <h1 style="margin:0">Alert rules</h1>
      <button type="button" class="primary right" routerLink="/rules/new">New rule</button>
    </div>
    <p class="muted">A rule says what you care about and where to send it.</p>
    @if (confirming()) { <p class="notice info small" role="note">Deleting a rule also removes the history of the alerts it produced.</p> }
    <app-notice [errors]="errors()" [message]="message()" />

    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (rules().length === 0) {
      <div class="card empty">You have no rules yet. <a routerLink="/rules/new">Create your first rule</a> — you can start from a ready-made example.</div>
    } @else {
      <div class="scroll"><table>
        <thead><tr><th scope="col">Name</th><th scope="col">Watches</th><th scope="col">Min. severity</th><th scope="col">Cooldown</th><th scope="col">Channels</th><th scope="col">Last alert</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
        <tbody>
          @for (r of rules(); track r.id) {
            <tr>
              <td><a [routerLink]="['/rules', r.id]">{{ r.name }}</a></td>
              <td>{{ r.category }}</td>
              <td>{{ r.minSeverity }}</td>
              <td>{{ r.cooldownSeconds === 0 ? 'None' : r.cooldownSeconds / 60 + ' min' }}</td>
              <td>{{ r.channelIds.length }}</td>
              <td class="small">{{ r.lastNotifiedAt ? (r.lastNotifiedAt | date: 'medium') : 'Never' }}</td>
              <td>
                <span [class]="r.isEnabled ? 'badge Sent' : 'badge Disabled'">{{ r.isEnabled ? 'On' : 'Paused' }}</span>
                @if (r.isEnabled && !hasUsableChannel(r)) {
                  <div class="small" style="color:var(--warn)" role="note">No active channel: this rule cannot reach anyone. <a routerLink="/channels">Check your channels</a>.</div>
                }
              </td>
              <td style="white-space:nowrap">
                <button type="button" [routerLink]="['/rules', r.id]" [attr.aria-label]="'Edit rule ' + r.name">Edit</button>
                @if (confirming() === r.id) {
                  <button type="button" class="danger" (click)="remove(r)">Yes, delete</button>
                  <button type="button" (click)="confirming.set('')">Cancel</button>
                } @else { <button type="button" class="danger" (click)="confirming.set(r.id)" [attr.aria-label]="'Delete rule ' + r.name">Delete</button> }
              </td>
            </tr>
          }
        </tbody>
      </table></div>
    }
  `,
})
export class Rules implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly rules = signal<Rule[]>([]);
  protected readonly loading = signal(true);
  protected readonly channels = signal<Channel[]>([]);
  protected readonly enabledTypes = signal<string[] | null>(null);
  protected readonly confirming = signal('');
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal('');

  ngOnInit(): void {
    this.reload();
    // Needed to warn about rules that can no longer reach anyone. If these fail, the page simply shows no warnings.
    this.api.channels().subscribe({ next: (c) => this.channels.set(c), error: () => undefined });
    this.api.channelTypes().subscribe({ next: (t) => this.enabledTypes.set(t), error: () => undefined });
  }

  /** A rule is only useful if at least one of its channels is switched on and its type has not been turned off by an administrator. */
  protected hasUsableChannel(r: Rule): boolean {
    const all = this.channels();
    if (all.length === 0) return true; // not loaded yet, or failed: say nothing rather than guess
    const types = this.enabledTypes();
    return r.channelIds.some((id) => {
      const c = all.find((x) => x.id === id);
      return !!c && c.isEnabled && (types === null || types.some((t) => t.toLowerCase() === c.type.toLowerCase()));
    });
  }

  private reload(): void {
    this.api.rules().subscribe({
      next: (r) => { this.rules.set(r); this.loading.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }

  protected remove(r: Rule): void {
    this.confirming.set('');
    this.api.deleteRule(r.id).subscribe({
      next: () => { this.message.set(`Rule “${r.name}” deleted.`); this.reload(); },
      error: (e: unknown) => this.errors.set(describeError(e)),
    });
  }
}

import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Rule } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-rules',
  imports: [RouterLink, Notice],
  template: `
    <div class="row" style="align-items:center; margin-bottom:.5rem">
      <h1 style="margin:0">Alert rules</h1>
      <a class="right" routerLink="/rules/new"><button type="button" class="primary">New rule</button></a>
    </div>
    <p class="muted">A rule says what you care about and where to send it.</p>
    <app-notice [errors]="errors()" [message]="message()" />

    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (rules().length === 0) {
      <div class="card empty">You have no rules yet. <a routerLink="/rules/new">Create your first rule</a> — you can start from a ready-made example.</div>
    } @else {
      <div class="scroll"><table>
        <thead><tr><th>Name</th><th>Watches</th><th>Min. severity</th><th>Cooldown</th><th>Channels</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (r of rules(); track r.id) {
            <tr>
              <td><a [routerLink]="['/rules', r.id]">{{ r.name }}</a></td>
              <td>{{ r.category }}</td>
              <td>{{ r.minSeverity }}</td>
              <td>{{ r.cooldownSeconds === 0 ? 'None' : r.cooldownSeconds / 60 + ' min' }}</td>
              <td>{{ r.channelIds.length }}</td>
              <td><span [class]="r.isEnabled ? 'badge Sent' : 'badge Disabled'">{{ r.isEnabled ? 'On' : 'Paused' }}</span></td>
              <td style="white-space:nowrap">
                <a [routerLink]="['/rules', r.id]"><button type="button">Edit</button></a>
                @if (confirming() === r.id) {
                  <button type="button" class="danger" (click)="remove(r)">Yes, delete</button>
                  <button type="button" (click)="confirming.set('')">Cancel</button>
                } @else { <button type="button" class="danger" (click)="confirming.set(r.id)">Delete</button> }
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
  protected readonly confirming = signal('');
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal('');

  ngOnInit(): void { this.reload(); }

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

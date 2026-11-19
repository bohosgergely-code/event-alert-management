import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { Channel, TestResult } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

interface Field { key: string; label: string; type: string; hint: string; placeholder: string; autocomplete?: string; }

/** Form fields per channel type. A type not listed here falls back to raw JSON, so a new channel works without frontend changes. */
const FIELDS: Record<string, Field[]> = {
  Email: [{ key: 'address', label: 'Email address', type: 'email', hint: 'Alerts are sent here.', placeholder: 'you@example.com' }],
  Slack: [{ key: 'webhookUrl', label: 'Slack webhook URL', type: 'url', hint: 'Create an Incoming Webhook in Slack. It must start with https://hooks.slack.com/services/', placeholder: 'https://hooks.slack.com/services/…' }],
  Webhook: [
    { key: 'url', label: 'Webhook URL', type: 'url', hint: 'A public https address that accepts POST requests. Addresses inside a private network are not allowed.', placeholder: 'https://example.com/hooks/alerts' },
    { key: 'secret', label: 'Signing secret', type: 'password', autocomplete: 'new-password', hint: 'At least 16 characters. Every message is signed with it so your server can check where it came from.', placeholder: '' },
  ],
};

@Component({
  selector: 'app-channels',
  imports: [FormsModule, DatePipe, Notice],
  template: `
    <h1>Channels</h1>
    <p class="muted">Channels are where your alerts are delivered. Add one, then use it in a rule.</p>
    <app-notice [errors]="errors()" [message]="message()" />

    @if (loading()) { <p class="muted">Loading…</p> }
    @else if (channels().length === 0) { <div class="card empty">You have no channels yet. Add one below.</div> }
    @else {
      <div class="scroll"><table>
        <thead><tr><th scope="col">Name</th><th scope="col">Type</th><th scope="col">Destination</th><th scope="col">Status</th><th scope="col">Last test</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
        <tbody>
          @for (c of channels(); track c.id) {
            <tr>
              <td>{{ c.name }}</td>
              <td>{{ c.type }}</td>
              <td class="mono">{{ c.settingsMasked }}</td>
              <td>
                <span [class]="c.isEnabled ? 'badge Sent' : 'badge Disabled'">{{ c.isEnabled ? 'On' : 'Paused' }}</span>
                <button type="button" class="link" (click)="toggle(c)" [disabled]="busy()" [attr.aria-label]="(c.isEnabled ? 'Pause ' : 'Resume ') + c.name">{{ c.isEnabled ? 'Pause' : 'Resume' }}</button>
              </td>
              <td class="small">
                @if (tests()[c.id]; as t) {
                  <span [class]="t.success ? 'badge Sent' : 'badge Failed'">{{ t.success ? 'Works' : 'Failed' }}</span>
                  @if (t.error) { <div class="muted">{{ t.error }}</div> }
                } @else if (c.lastTestedAt) {
                  {{ c.lastTestedAt | date: 'medium' }}<br /><span class="muted">{{ c.lastTestResult }}</span>
                } @else { <span class="muted">Never</span> }
              </td>
              <td style="white-space:nowrap">
                <button type="button" (click)="test(c)" [disabled]="testing() === c.id" [attr.aria-label]="'Send a test message to ' + c.name">{{ testing() === c.id ? 'Sending…' : 'Send test' }}</button>
                @if (confirming() === c.id) {
                  <button type="button" class="danger" (click)="remove(c)">Yes, delete</button>
                  <button type="button" (click)="confirming.set('')">Cancel</button>
                } @else {
                  <button type="button" class="danger" (click)="confirming.set(c.id)" [attr.aria-label]="'Delete ' + c.name">Delete</button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table></div>
    }

    <section class="card" style="margin-top:1rem">
      <h2 style="margin-top:0">Add a channel</h2>
      <form (ngSubmit)="add()" class="stack">
        <div class="row">
          <div class="grow">
            <label for="type">Type</label>
            <select id="type" name="type" [(ngModel)]="type" (ngModelChange)="values = {}; json = '{}'">
              @for (t of types(); track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </div>
          <div class="grow">
            <label for="name">Name</label>
            <input id="name" name="name" [(ngModel)]="name" required maxlength="100" placeholder="For example: My work inbox" />
          </div>
        </div>
        @for (f of fields(); track f.key) {
          <div>
            <label [for]="'f-' + f.key">{{ f.label }}</label>
            <input [id]="'f-' + f.key" [name]="'f-' + f.key" [type]="f.type" [placeholder]="f.placeholder" [attr.autocomplete]="f.autocomplete ?? null" [(ngModel)]="values[f.key]" required />
            <div class="small muted">{{ f.hint }}</div>
          </div>
        }
        @if (fields().length === 0 && type) {
          <div>
            <label for="json">Settings (JSON)</label>
            <textarea id="json" name="json" rows="3" class="mono" [(ngModel)]="json"></textarea>
          </div>
        }
        <button type="submit" class="primary" [disabled]="busy() || !type || !name.trim()">Add channel</button>
      </form>
    </section>
  `,
})
export class Channels implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly channels = signal<Channel[]>([]);
  protected readonly types = signal<string[]>([]);
  protected readonly tests = signal<Record<string, TestResult>>({});
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly testing = signal('');
  protected readonly confirming = signal('');
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal('');

  protected type = '';
  protected name = '';
  protected values: Record<string, string> = {};
  protected json = '{}';

  protected fields = () => FIELDS[this.type] ?? [];

  ngOnInit(): void {
    this.api.channelTypes().subscribe({
      next: (t) => { this.types.set(t); this.type = t[0] ?? ''; },
      error: (e: unknown) => this.errors.set(describeError(e)),
    });
    this.reload();
  }

  private reload(): void {
    this.api.channels().subscribe({
      next: (c) => { this.channels.set(c); this.loading.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
    });
  }

  protected add(): void {
    let settings: Record<string, unknown>;
    if (this.fields().length > 0) settings = Object.fromEntries(this.fields().map((f) => [f.key, (this.values[f.key] ?? '').trim()]));
    else {
      try { settings = JSON.parse(this.json) as Record<string, unknown>; }
      catch { this.errors.set(['The settings are not valid JSON.']); return; }
    }
    this.begin();
    this.api.createChannel({ type: this.type, name: this.name.trim(), settings }).subscribe({
      next: (c) => {
        this.message.set(`Channel “${c.name}” added. Use “Send test” to check it works.`);
        // Nothing typed stays on screen, in particular no secret.
        this.name = ''; this.values = {}; this.json = '{}'; this.busy.set(false);
        this.reload();
      },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.busy.set(false); },
    });
  }

  protected toggle(c: Channel): void {
    this.begin();
    this.api.updateChannel(c.id, { isEnabled: !c.isEnabled }).subscribe({
      next: () => { this.busy.set(false); this.reload(); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.busy.set(false); this.reload(); },
    });
  }

  protected test(c: Channel): void {
    this.begin();
    this.testing.set(c.id);
    this.api.testChannel(c.id).subscribe({
      next: (r) => { this.tests.update((t) => ({ ...t, [c.id]: r })); this.testing.set(''); this.busy.set(false); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.testing.set(''); this.busy.set(false); },
    });
  }

  protected remove(c: Channel): void {
    this.begin();
    this.confirming.set('');
    this.api.deleteChannel(c.id).subscribe({
      next: () => { this.message.set(`Channel “${c.name}” deleted.`); this.busy.set(false); this.reload(); },
      error: (e: unknown) => { this.errors.set(describeError(e)); this.busy.set(false); },
    });
  }

  private begin(): void { this.busy.set(true); this.errors.set([]); this.message.set(''); }
}

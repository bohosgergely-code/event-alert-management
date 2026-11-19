import { Component, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { ChannelType } from '../../core/models';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-admin-channel-types',
  imports: [Notice],
  template: `
    <h1>Channel types</h1>
    <p class="muted">Switch a whole delivery method off for everyone, for example during an incident. New matches are recorded as suppressed, and queued sends are held back until it is switched on again.</p>
    <app-notice [errors]="errors()" [message]="message()" />
    <div class="scroll"><table>
      <thead><tr><th scope="col">Type</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
      <tbody>
        @for (t of types(); track t.type) {
          <tr>
            <td>{{ t.type }}@if (!t.isRegistered) { <span class="small muted"> (not installed)</span> }</td>
            <td><span [class]="t.isEnabled ? 'badge Sent' : 'badge Failed'">{{ t.isEnabled ? 'On' : 'Off' }}</span></td>
            <td>@if (t.isRegistered) { <button type="button" (click)="set(t, !t.isEnabled)">{{ t.isEnabled ? 'Switch off' : 'Switch on' }}</button> }</td>
          </tr>
        }
      </tbody>
    </table></div>
  `,
})
export class AdminChannelTypes implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly types = signal<ChannelType[]>([]);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal('');

  ngOnInit(): void { this.reload(); }
  private reload(): void { this.api.adminChannelTypes().subscribe({ next: (t) => this.types.set(t), error: (e: unknown) => this.errors.set(describeError(e)) }); }

  protected set(t: ChannelType, enabled: boolean): void {
    this.errors.set([]); this.message.set('');
    this.api.setChannelType(t.type, enabled).subscribe({
      next: () => { this.message.set(`${t.type} is now ${enabled ? 'on' : 'off'}.`); this.reload(); },
      error: (e: unknown) => this.errors.set(describeError(e)),
    });
  }
}

import { ChangeDetectorRef, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { CATEGORIES, Channel, Preset, Preview } from '../../core/models';
import { describeError } from '../../core/errors';
import { RuleFormModel, applyPreset, check, emptyForm, fromRule, toRequest } from '../../core/rule-form';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-rule-editor',
  imports: [FormsModule, RouterLink, DatePipe, Notice],
  template: `
    <h1>{{ id() ? 'Edit rule' : 'New rule' }}</h1>
    <app-notice [errors]="errors()" />

    @if (loading()) { <p class="muted">Loading…</p> }
    @else {
      @if (!id() && presets().length > 0) {
        <section class="card">
          <label for="preset">Start from an example <span class="hint">(optional)</span></label>
          <select id="preset" name="preset" [ngModel]="''" (ngModelChange)="usePreset($event)">
            <option value="">Choose…</option>
            @for (p of presets(); track p.name) { <option [value]="p.name">{{ p.name }} — {{ p.description }}</option> }
          </select>
        </section>
      }

      <form (ngSubmit)="save()" class="card stack">
        <div class="row">
          <div class="grow">
            <label for="name">Name</label>
            <input id="name" name="name" [(ngModel)]="m.name" required maxlength="100" placeholder="For example: Big earthquakes" />
          </div>
          <div>
            <label for="category">Watch for</label>
            <select id="category" name="category" [(ngModel)]="m.category">
              @for (c of categories; track c) { <option [value]="c">{{ labels[c] }}</option> }
            </select>
          </div>
        </div>

        @if (m.category !== 'Disasters') {
          <p class="notice info small" role="note" style="margin:0">
            No live {{ m.category === 'News' ? 'news' : 'market' }} feed is connected in this version, so this rule reacts to test events created by an administrator.
            Real events are available for natural disasters.
          </p>
        }

        <fieldset class="stack" style="border:1px solid var(--border); border-radius:8px">
          <legend class="small muted">Only alert me when…</legend>
          @switch (m.category) {
            @case ('News') {
              <div><label for="inc">Mentions any of these words <span class="hint">(comma separated; leave empty for all news)</span></label>
                <input id="inc" name="inc" [(ngModel)]="m.includeKeywords" placeholder="evacuation, outbreak" /></div>
              <div><label for="exc">…but not these words</label>
                <input id="exc" name="exc" [(ngModel)]="m.excludeKeywords" placeholder="sports, celebrity" /></div>
              <p class="small muted">News items do not carry a reliable importance level, so words are the best filter here.</p>
            }
            @case ('Disasters') {
              <div class="row">
                <div class="grow"><label for="mag">Magnitude at least</label>
                  <input id="mag" name="mag" type="number" step="0.1" min="0" max="10" [(ngModel)]="m.minMagnitude" placeholder="6.0" /></div>
                <div class="grow"><label for="region">Place contains</label>
                  <input id="region" name="region" [(ngModel)]="m.region" placeholder="Japan" /></div>
                <div class="grow"><label for="types">Event types <span class="hint">(comma separated)</span></label>
                  <input id="types" name="types" [(ngModel)]="m.eventTypes" placeholder="earthquake" /></div>
              </div>
            }
            @case ('Markets') {
              <div class="row">
                <div class="grow"><label for="tick">Tickers <span class="hint">(comma separated)</span></label>
                  <input id="tick" name="tick" [(ngModel)]="m.tickers" placeholder="BTC, ETH" /></div>
                <div class="grow"><label for="pct">Moves at least (%)</label>
                  <input id="pct" name="pct" type="number" step="0.5" min="0" [(ngModel)]="m.minPercentMove" placeholder="5" /></div>
                <div class="grow"><label for="dir">Direction</label>
                  <select id="dir" name="dir" [(ngModel)]="m.direction">
                    <option value="Either">Up or down</option><option value="Up">Up only</option><option value="Down">Down only</option>
                  </select></div>
              </div>
            }
          }
        </fieldset>

        <div class="row">
          <div class="grow"><label for="sev">Minimum severity</label>
            <select id="sev" name="sev" [(ngModel)]="m.minSeverity">
              <option [ngValue]="1">1 — everything</option><option [ngValue]="2">2</option><option [ngValue]="3">3 — notable</option>
              <option [ngValue]="4">4</option><option [ngValue]="5">5 — most serious only</option>
            </select></div>
          <div class="grow"><label for="cool">Wait between alerts (minutes)</label>
            <input id="cool" name="cool" type="number" min="0" step="1" [(ngModel)]="m.cooldownMinutes" />
            <div class="small muted">0 = alert every time.</div></div>
          <div class="grow"><label class="check" style="margin-top:1.4rem"><input type="checkbox" name="enabled" [(ngModel)]="m.isEnabled" /> Rule is on</label></div>
        </div>

        <div>
          <label>Send alerts to</label>
          @if (channels().length === 0) {
            <p class="notice info">You have no channels yet. <a routerLink="/channels">Add a channel</a> first, then come back.</p>
          }
          @for (c of channels(); track c.id) {
            <label class="check"><input type="checkbox" [name]="'ch-' + c.id" [checked]="m.channelIds.includes(c.id)" (change)="toggleChannel(c.id)" />
              {{ c.name }} <span class="muted small">({{ c.type }}, {{ c.settingsMasked }})</span></label>
          }
        </div>

        <div class="row">
          <button type="submit" class="primary" [disabled]="busy()">{{ id() ? 'Save changes' : 'Create rule' }}</button>
          <button type="button" (click)="preview()" [disabled]="busy()">Preview matches</button>
          <a routerLink="/rules"><button type="button">Cancel</button></a>
        </div>
      </form>

      @if (result(); as r) {
        <section class="card" aria-live="polite">
          <h2 style="margin-top:0">Preview</h2>
          <p>Of the last <b>{{ r.eventsChecked }}</b> {{ nouns[m.category] }} from the past week, <b>{{ r.matches }}</b> would have triggered this rule.</p>
          @if (r.samples.length > 0) {
            <ul class="small">
              @for (s of r.samples; track s.id) { <li>{{ s.title }} <span class="muted">— {{ s.occurredAt | date: 'medium' }}</span></li> }
            </ul>
          }
        </section>
      }
    }
  `,
})
export class RuleEditor implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Bound from the route (/rules/:id). Empty on /rules/new. */
  readonly id = input<string>();

  protected readonly categories = CATEGORIES;
  protected readonly labels = { News: 'News', Markets: 'Markets', Disasters: 'Natural disasters' } as const;
  protected readonly nouns = { News: 'news items', Markets: 'market events', Disasters: 'disaster events' } as const;
  protected m: RuleFormModel = emptyForm();
  protected readonly channels = signal<Channel[]>([]);
  protected readonly presets = signal<Preset[]>([]);
  protected readonly result = signal<Preview | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  ngOnInit(): void {
    this.api.channels().subscribe({ next: (c) => this.channels.set(c), error: (e: unknown) => this.errors.set(describeError(e)) });
    const id = this.id();
    if (id) {
      this.api.rule(id).subscribe({
        next: (r) => { this.m = fromRule(r); this.loading.set(false); this.cdr.markForCheck(); },
        error: (e: unknown) => { this.errors.set(describeError(e)); this.loading.set(false); },
      });
    } else {
      this.api.presets().subscribe({ next: (p) => this.presets.set(p) });
      this.loading.set(false);
    }
  }

  protected usePreset(name: string): void {
    const p = this.presets().find((x) => x.name === name);
    if (p) { this.m = applyPreset(this.m, p); this.result.set(null); }
  }

  protected toggleChannel(id: string): void {
    this.m.channelIds = this.m.channelIds.includes(id) ? this.m.channelIds.filter((c) => c !== id) : [...this.m.channelIds, id];
  }

  protected preview(): void {
    const problems = check(this.m, false);
    if (problems.length > 0) { this.errors.set(problems); return; }
    this.begin();
    this.api.previewRule(toRequest(this.m)).subscribe({
      next: (r) => { this.result.set(r); this.busy.set(false); },
      error: (e: unknown) => this.fail(e),
    });
  }

  protected save(): void {
    const problems = check(this.m, true);
    if (problems.length > 0) { this.errors.set(problems); return; }
    this.begin();
    const body = toRequest(this.m);
    const id = this.id();
    (id ? this.api.updateRule(id, body) : this.api.createRule(body)).subscribe({
      next: () => void this.router.navigateByUrl('/rules'),
      error: (e: unknown) => this.fail(e),
    });
  }

  private begin(): void { this.busy.set(true); this.errors.set([]); }
  private fail(e: unknown): void { this.errors.set(describeError(e)); this.busy.set(false); }
}

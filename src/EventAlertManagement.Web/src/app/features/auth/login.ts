import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink, Notice],
  template: `
    <section class="card narrow">
      <h1>Sign in</h1>
      <p class="muted small">Enter your email address. If you are new, an account is created for you.</p>
      @if (auth.notice()) { <p class="notice info" role="status">{{ auth.notice() }}</p> }
      <app-notice [errors]="errors()" />
      <form (ngSubmit)="submit()" class="stack">
        <div>
          <label for="email">Email address</label>
          <input id="email" name="email" type="email" autocomplete="email" required [(ngModel)]="email" />
        </div>
        <button type="submit" class="primary" [disabled]="busy() || !email.trim()">Continue</button>
      </form>
      <p class="small muted">Administrator? <a routerLink="/admin/login">Sign in here</a>.</p>
    </section>
  `,
})
export class Login {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected email = '';
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected submit(): void {
    this.busy.set(true);
    this.errors.set([]);
    this.auth.login(this.email.trim()).subscribe({
      next: () => void this.router.navigateByUrl('/rules'),
      error: (e: unknown) => { this.errors.set(describeError(e)); this.busy.set(false); },
    });
  }
}

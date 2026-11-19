import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/errors';
import { Notice } from '../../shared/notice';

@Component({
  selector: 'app-admin-login',
  imports: [FormsModule, RouterLink, Notice],
  template: `
    <section class="card narrow">
      <h1>Administrator sign-in</h1>
      @if (auth.notice()) { <p class="notice info" role="status">{{ auth.notice() }}</p> }
      <app-notice [errors]="errors()" />
      <form (ngSubmit)="submit()" class="stack">
        <div>
          <label for="key">Admin key</label>
          <input id="key" name="key" type="password" autocomplete="off" required [(ngModel)]="key" />
        </div>
        <button type="submit" class="primary" [disabled]="busy() || !key">Sign in</button>
      </form>
      <p class="small muted"><a routerLink="/login">Back to user sign-in</a></p>
    </section>
  `,
})
export class AdminLogin {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected key = '';
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected submit(): void {
    this.busy.set(true);
    this.errors.set([]);
    this.auth.adminLogin(this.key).subscribe({
      next: () => void this.router.navigateByUrl('/admin'),
      error: (e: unknown) => {
        this.key = '';
        this.errors.set(describeError(e));
        this.busy.set(false);
      },
    });
  }
}

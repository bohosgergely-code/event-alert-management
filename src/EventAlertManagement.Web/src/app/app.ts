import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="bar">
      <a class="brand" routerLink="/">EventAlert</a>
      <nav aria-label="Main">
        @if (auth.role() === 'User') {
          <a routerLink="/rules" routerLinkActive="active">Rules</a>
          <a routerLink="/channels" routerLinkActive="active">Channels</a>
          <a routerLink="/history" routerLinkActive="active">History</a>
        }
        @if (auth.role() === 'Admin') {
          <a routerLink="/admin" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Overview</a>
          <a routerLink="/admin/notifications" routerLinkActive="active">Deliveries</a>
          <a routerLink="/admin/events" routerLinkActive="active">Events</a>
          <a routerLink="/admin/sources" routerLinkActive="active">Sources</a>
          <a routerLink="/admin/channel-types" routerLinkActive="active">Channel types</a>
        }
      </nav>
      <span class="spacer"></span>
      @if (auth.isSignedIn()) {
        <span class="who">{{ auth.role() === 'Admin' ? 'Administrator' : 'Signed in' }}</span>
        <button type="button" class="link" (click)="auth.logout()">Sign out</button>
      } @else {
        <a routerLink="/login">Sign in</a>
        <a routerLink="/admin/login">Admin</a>
      }
    </header>
    <main><router-outlet /></main>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
}

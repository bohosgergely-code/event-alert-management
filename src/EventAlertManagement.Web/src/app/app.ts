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
          <a routerLink="/rules" routerLinkActive="active" ariaCurrentWhenActive="page">Rules</a>
          <a routerLink="/channels" routerLinkActive="active" ariaCurrentWhenActive="page">Channels</a>
          <a routerLink="/history" routerLinkActive="active" ariaCurrentWhenActive="page">History</a>
        }
        @if (auth.role() === 'Admin') {
          <a routerLink="/admin" routerLinkActive="active" ariaCurrentWhenActive="page" [routerLinkActiveOptions]="{ exact: true }">Overview</a>
          <a routerLink="/admin/notifications" routerLinkActive="active" ariaCurrentWhenActive="page">Deliveries</a>
          <a routerLink="/admin/events" routerLinkActive="active" ariaCurrentWhenActive="page">Events</a>
          <a routerLink="/admin/users" routerLinkActive="active" ariaCurrentWhenActive="page">Users</a>
          <a routerLink="/admin/rules" routerLinkActive="active" ariaCurrentWhenActive="page">Rules</a>
          <a routerLink="/admin/sources" routerLinkActive="active" ariaCurrentWhenActive="page">Sources</a>
          <a routerLink="/admin/channel-types" routerLinkActive="active" ariaCurrentWhenActive="page">Channel types</a>
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

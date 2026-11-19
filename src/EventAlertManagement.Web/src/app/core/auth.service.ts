import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { TokenResponse } from './models';

export type Role = 'User' | 'Admin';
interface Session { token: string; role: Role; expiresAt: string; }

const STORAGE_KEY = 'eam.session';

/**
 * Holds the signed-in session. It lives in sessionStorage, so closing the tab signs you out.
 * (A token in browser storage can be read by any script on the page; acceptable for this scope, see ADR-016.)
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(this.load());

  readonly role = computed(() => this.valid(this.session())?.role ?? null);
  readonly isSignedIn = computed(() => this.role() !== null);
  readonly isAdmin = computed(() => this.role() === 'Admin');

  /** Shown once on the sign-in page, for example after the server ended the session. */
  readonly notice = signal('');

  get token(): string | null { return this.valid(this.session())?.token ?? null; }

  login(email: string): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/api/auth/login', { email }).pipe(tap((r) => this.store(r)));
  }

  adminLogin(key: string): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/api/auth/admin-login', { key }).pipe(tap((r) => this.store(r)));
  }

  logout(redirect = true, notice = ''): void {
    const wasAdmin = this.isAdmin();
    this.session.set(null);
    this.notice.set(notice);
    try { sessionStorage.removeItem(STORAGE_KEY); } catch { /* storage may be unavailable */ }
    if (redirect) void this.router.navigateByUrl(wasAdmin ? '/admin/login' : '/login');
  }

  private store(r: TokenResponse): void {
    const s: Session = { token: r.token, role: r.role === 'Admin' ? 'Admin' : 'User', expiresAt: r.expiresAt };
    this.session.set(s);
    this.notice.set('');
    try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s)); } catch { /* keep working in memory */ }
  }

  private load(): Session | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const s = JSON.parse(raw) as Session;
      return typeof s.token === 'string' && (s.role === 'User' || s.role === 'Admin') ? s : null;
    } catch { return null; }
  }

  /** A session past its expiry counts as signed out, even before the server says so. */
  private valid(s: Session | null): Session | null {
    return s && Date.parse(s.expiresAt) > Date.now() ? s : null;
  }
}

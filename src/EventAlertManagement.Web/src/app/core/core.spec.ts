import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { adminGuard, userGuard } from './guards';
import { describeError } from './errors';
import { query } from './api.service';

const future = () => new Date(Date.now() + 3600_000).toISOString();
const past = () => new Date(Date.now() - 3600_000).toISOString();

function setup() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
  });
  return {
    auth: TestBed.inject(AuthService),
    http: TestBed.inject(HttpClient),
    ctrl: TestBed.inject(HttpTestingController),
    router: TestBed.inject(Router),
  };
}

beforeEach(() => sessionStorage.clear());

describe('AuthService', () => {
  it('starts signed out', () => {
    const { auth } = setup();
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token).toBeNull();
  });

  it('signs in as a user and keeps the session for this tab', () => {
    const { auth, ctrl } = setup();
    auth.login('a@b.com').subscribe();
    const req = ctrl.expectOne('/api/auth/login');
    expect(req.request.body).toEqual({ email: 'a@b.com' });
    req.flush({ token: 't1', role: 'User', expiresAt: future() });

    expect(auth.role()).toBe('User');
    expect(auth.token).toBe('t1');
    expect(JSON.parse(sessionStorage.getItem('eam.session')!).token).toBe('t1');
  });

  it('signs in as admin', () => {
    const { auth, ctrl } = setup();
    auth.adminLogin('key').subscribe();
    ctrl.expectOne('/api/auth/admin-login').flush({ token: 'adm', role: 'Admin', expiresAt: future() });
    expect(auth.isAdmin()).toBe(true);
  });

  it('a failed sign-in leaves the user signed out', () => {
    const { auth, ctrl } = setup();
    auth.login('a@b.com').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/auth/login').flush({ title: 'no' }, { status: 400, statusText: 'Bad Request' });
    expect(auth.isSignedIn()).toBe(false);
  });

  it('restores a stored session after a reload', () => {
    sessionStorage.setItem('eam.session', JSON.stringify({ token: 'kept', role: 'User', expiresAt: future() }));
    const { auth } = setup();
    expect(auth.token).toBe('kept');
  });

  it('treats an expired session as signed out', () => {
    sessionStorage.setItem('eam.session', JSON.stringify({ token: 'old', role: 'Admin', expiresAt: past() }));
    const { auth } = setup();
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token).toBeNull();
  });

  it('ignores damaged or tampered stored data', () => {
    for (const junk of ['not json', '{"token":1}', '{"token":"t","role":"Root","expiresAt":"2099-01-01"}', 'null']) {
      sessionStorage.setItem('eam.session', junk);
      TestBed.resetTestingModule();
      expect(setup().auth.isSignedIn()).toBe(false);
    }
  });

  it('logout clears memory and storage, and goes to the matching sign-in page', async () => {
    const { auth, ctrl, router } = setup();
    auth.adminLogin('k').subscribe();
    ctrl.expectOne('/api/auth/admin-login').flush({ token: 'adm', role: 'Admin', expiresAt: future() });
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    auth.logout();

    expect(auth.isSignedIn()).toBe(false);
    expect(sessionStorage.getItem('eam.session')).toBeNull();
    expect(nav).toHaveBeenCalledWith('/admin/login');
  });
});

describe('authInterceptor', () => {
  function signedIn(role: 'User' | 'Admin' = 'User') {
    sessionStorage.setItem('eam.session', JSON.stringify({ token: 'tok', role, expiresAt: future() }));
    return setup();
  }

  it('adds the token to API calls', () => {
    const { http, ctrl } = signedIn();
    http.get('/api/rules').subscribe();
    expect(ctrl.expectOne('/api/rules').request.headers.get('Authorization')).toBe('Bearer tok');
  });

  it('does not send the token to sign-in calls', () => {
    const { http, ctrl } = signedIn();
    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });
    expect(ctrl.expectOne('/api/auth/login').request.headers.has('Authorization')).toBe(false);
  });

  it('never sends the token to anything outside the API', () => {
    const { http, ctrl } = signedIn();
    http.get('https://example.com/steal').subscribe();
    expect(ctrl.expectOne('https://example.com/steal').request.headers.has('Authorization')).toBe(false);
  });

  it('sends nothing extra when signed out', () => {
    const { http, ctrl } = setup();
    http.get('/api/rules').subscribe({ error: () => undefined });
    expect(ctrl.expectOne('/api/rules').request.headers.has('Authorization')).toBe(false);
  });

  it('signs the user out when the server rejects the token', () => {
    const { http, ctrl, auth, router } = signedIn();
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    http.get('/api/rules').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/rules').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isSignedIn()).toBe(false);
  });

  it('a wrong password on the sign-in page does not sign anyone out or redirect', () => {
    const { http, ctrl, router } = signedIn();
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    http.post('/api/auth/admin-login', {}).subscribe({ error: () => undefined });
    ctrl.expectOne('/api/auth/admin-login').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(nav).not.toHaveBeenCalled();
  });

  it('other errors keep the session', () => {
    const { http, ctrl, auth } = signedIn();
    http.get('/api/rules').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/rules').flush({}, { status: 500, statusText: 'Server Error' });
    expect(auth.isSignedIn()).toBe(true);
  });
});

describe('guards', () => {
  const run = (guard: typeof userGuard) => TestBed.runInInjectionContext(() => guard({} as never, {} as never));
  const asText = (r: unknown) => (typeof r === 'boolean' ? r : String(r));

  it('user pages: users in, admins and visitors redirected', () => {
    setup();
    expect(asText(run(userGuard))).toBe('/login');

    sessionStorage.setItem('eam.session', JSON.stringify({ token: 't', role: 'User', expiresAt: future() }));
    TestBed.resetTestingModule(); setup();
    expect(run(userGuard)).toBe(true);

    sessionStorage.setItem('eam.session', JSON.stringify({ token: 't', role: 'Admin', expiresAt: future() }));
    TestBed.resetTestingModule(); setup();
    expect(asText(run(userGuard))).toBe('/admin');
  });

  it('admin pages: only admins in', () => {
    setup();
    expect(asText(run(adminGuard))).toBe('/admin/login');

    sessionStorage.setItem('eam.session', JSON.stringify({ token: 't', role: 'User', expiresAt: future() }));
    TestBed.resetTestingModule(); setup();
    expect(asText(run(adminGuard))).toBe('/rules');

    sessionStorage.setItem('eam.session', JSON.stringify({ token: 't', role: 'Admin', expiresAt: future() }));
    TestBed.resetTestingModule(); setup();
    expect(run(adminGuard)).toBe(true);
  });
});

describe('describeError', () => {
  const http = (status: number, error?: unknown) => new HttpErrorResponse({ status, error });

  it('lists every validation message from the API', () => {
    const e = http(400, { title: 'One or more validation errors occurred.', errors: { settings: ['Bad host.'], name: ['Too long.'] } });
    expect(describeError(e)).toEqual(['Bad host.', 'Too long.']);
  });
  it('falls back to the problem title', () => expect(describeError(http(409, { title: 'This channel is used by a rule.' }))).toEqual(['This channel is used by a rule.']));
  it('explains a missing server', () => expect(describeError(http(0))[0]).toMatch(/cannot reach/i));
  it('explains rate limiting', () => expect(describeError(http(429))[0]).toMatch(/too many/i));
  it('never shows raw server error content', () => expect(describeError(http(500, 'Stack trace at Foo.Bar()'))[0]).not.toMatch(/stack/i));
  it('handles things that are not HTTP errors', () => expect(describeError(new Error('boom'))).toEqual(['Something unexpected went wrong.']));
  it('handles an empty body', () => expect(describeError(http(400, null))).toEqual(['The request was not accepted.']));
});

describe('query', () => {
  it('leaves out empty filters but keeps zero and false', () => {
    const p = query({ status: '', page: 1, flag: false, zero: 0, none: null, missing: undefined, text: 'a b' });
    expect(p.keys().sort()).toEqual(['flag', 'page', 'text', 'zero']);
    expect(p.get('flag')).toBe('false');
    expect(p.get('zero')).toBe('0');
  });
});

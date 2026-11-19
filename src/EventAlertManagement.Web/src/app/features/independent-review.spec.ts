import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpClient } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { History } from './history/history';
import { Channels } from './channels/channels';
import { RuleEditor } from './rules/rule-editor';
import { Rules } from './rules/rules';
import { AdminUsers } from './admin/users';
import { AdminRules } from './admin/rules';
import { Login } from './auth/login';
import { AuthService } from '../core/auth.service';
import { authInterceptor } from '../core/auth.interceptor';
import { routes } from '../app.routes';
import { adminGuard, userGuard } from '../core/guards';

/**
 * Each test pins a problem an independent reviewer reported and that was reproduced before it was fixed
 * (evidence/independent-review). They failed on the code as it was.
 */
function setup() {
  sessionStorage.clear();
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] });
  return TestBed.inject(HttpTestingController);
}
const page = (items: unknown[], p = 1, total = items.length) => ({ items, page: p, pageSize: 20, total });
const row = (title: string, status: string) => ({ id: title, status, createdAt: '2026-06-01T10:00:00Z', attemptCount: 1, ruleId: 'r', ruleName: 'R', channelId: 'c', channelName: 'C', channelType: 'Email', eventId: 'e', eventTitle: title, eventSeverity: 3 });
const future = () => new Date(Date.now() + 3600_000).toISOString();

describe('History: answers that arrive out of order', () => {
  it('the table always matches the filter that is selected', async () => {
    const ctrl = setup();
    const f = TestBed.createComponent(History); f.detectChanges();
    ctrl.expectOne((r) => r.url === '/api/notifications').flush(page([]));
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const sel = el.querySelector('#status') as HTMLSelectElement;

    sel.value = 'Failed'; sel.dispatchEvent(new Event('change')); await f.whenStable();
    sel.value = 'Sent'; sel.dispatchEvent(new Event('change')); await f.whenStable();
    const pending = ctrl.match((r) => r.url === '/api/notifications').filter((r) => !r.cancelled);
    expect(pending.map((r) => r.request.params.get('status'))).toEqual(['Sent']); // the superseded request was cancelled
    pending[0].flush(page([row('SENT-ROW', 'Sent')]));
    await f.whenStable();

    expect(sel.value).toBe('Sent');
    expect(el.querySelector('tbody')?.textContent).toContain('SENT-ROW');
    expect(el.querySelector('tbody')?.textContent).not.toContain('FAILED-ROW');
  });

  it('on a page that no longer exists the user is taken back, and the pager stays usable', async () => {
    const ctrl = setup();
    const f = TestBed.createComponent(History); f.detectChanges();
    ctrl.expectOne((r) => r.url === '/api/notifications').flush(page([row('x', 'Sent')], 3, 41));
    await f.whenStable();
    (f.componentInstance as unknown as { list: { load(p: number): void } }).list.load(3);
    ctrl.expectOne((r) => r.url === '/api/notifications').flush(page([], 3, 21)); // shrank to two pages
    await f.whenStable();
    const back = ctrl.expectOne((r) => r.url === '/api/notifications');
    expect(back.request.params.get('page')).toBe('2');
    back.flush(page([row('y', 'Sent')], 2, 21));
    await f.whenStable();
    expect(f.nativeElement.textContent).toContain('Page 2 of 2');
  });
});

describe('Channels: pausing a channel', () => {
  async function open() {
    const ctrl = setup();
    const f = TestBed.createComponent(Channels); f.detectChanges();
    ctrl.expectOne('/api/channels/types').flush(['Email']);
    const ch = { id: '1', type: 'Email', name: 'Mine', isEnabled: true, settingsMasked: 'a***@b.com' };
    ctrl.expectOne('/api/channels').flush([ch]); await f.whenStable();
    return { ctrl, f, ch, el: f.nativeElement as HTMLElement };
  }
  const pauseButton = (el: HTMLElement) => el.querySelector('tbody button[aria-label^="Pause"], tbody button[aria-label^="Resume"]') as HTMLButtonElement;

  it('after a failed request the screen still shows the state the server really has', async () => {
    const { ctrl, f, ch, el } = await open();
    expect(el.querySelector('tbody .badge')?.textContent?.trim()).toBe('On');

    pauseButton(el).click(); await f.whenStable();
    ctrl.expectOne((r) => r.method === 'PUT').flush({}, { status: 500, statusText: 'x' }); await f.whenStable();
    ctrl.expectOne('/api/channels').flush([ch]); await f.whenStable();

    expect(el.querySelector('tbody .badge')?.textContent?.trim()).toBe('On');
    expect(pauseButton(el).textContent?.trim()).toBe('Pause');   // button and server agree: still on
    expect(el.querySelector('[role=alert]')).not.toBeNull();       // and the user is told it did not work
  });

  it('the control cannot be pressed again while a change is in flight, and says what it will do', async () => {
    const { ctrl, f, ch, el } = await open();
    expect(pauseButton(el).getAttribute('aria-label')).toBe('Pause Mine');
    pauseButton(el).click(); await f.whenStable();
    expect(pauseButton(el).disabled).toBe(true);
    ctrl.expectOne((r) => r.method === 'PUT').flush({ ...ch, isEnabled: false });
    ctrl.expectOne('/api/channels').flush([{ ...ch, isEnabled: false }]); await f.whenStable();
    expect(pauseButton(el).textContent?.trim()).toBe('Resume');
    expect(el.querySelector('tbody .badge')?.textContent?.trim()).toBe('Paused');
  });
});

describe('Rule editor', () => {
  it('does not offer a form that can be saved when the rule could not be loaded', async () => {
    const ctrl = setup();
    const f = TestBed.createComponent(RuleEditor); f.componentRef.setInput('id', 'gone'); f.detectChanges();
    ctrl.expectOne('/api/channels').flush([]);
    ctrl.expectOne('/api/rules/gone').flush({}, { status: 404, statusText: 'nf' }); await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[role=alert]')?.textContent).toContain('not found');
    expect(el.querySelector('form')).toBeNull();
    expect(el.querySelector('button[type=submit]')).toBeNull();
  });

  async function openNew() {
    const ctrl = setup();
    const f = TestBed.createComponent(RuleEditor); f.detectChanges();
    ctrl.expectOne('/api/channels').flush([]);
    ctrl.expectOne('/api/rules/presets').flush([]);
    await f.whenStable();
    return { ctrl, f, el: f.nativeElement as HTMLElement };
  }

  it('a preview is shown only while the form still matches what was previewed', async () => {
    const { ctrl, f, el } = await openNew();
    const name = el.querySelector('#name') as HTMLInputElement;
    name.value = 'Quakes'; name.dispatchEvent(new Event('input')); await f.whenStable();

    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Preview matches')) as HTMLButtonElement).click(); await f.whenStable();
    ctrl.expectOne('/api/rules/preview').flush({ eventsChecked: 10, matches: 2, samples: [] }); await f.whenStable();
    expect(el.textContent).toContain('would have triggered this rule');
    expect(el.textContent).toContain('disaster events');

    // the user switches the category without previewing again
    const select = el.querySelector('#category') as HTMLSelectElement;
    select.value = 'Markets'; select.dispatchEvent(new Event('change')); await f.whenStable();
    expect(el.textContent).not.toContain('would have triggered this rule');          // no old numbers under a new rule
    expect(el.textContent).toContain('Preview again');                                // and a clear instruction instead
    expect(el.textContent).not.toContain('market events from the past week');
  });
});

describe('Rules list: can the rule reach anyone?', () => {
  const rule = (id: string, name: string, channelIds: string[], isEnabled = true) => ({ id, name, category: 'Disasters', minSeverity: 1, filters: {}, cooldownSeconds: 0, isEnabled, channelIds, lastNotifiedAt: null });
  const ch = (id: string, type: string, isEnabled: boolean) => ({ id, type, name: id, isEnabled, settingsMasked: '' });

  async function open(rules: unknown[], channels: unknown[], types: string[]) {
    const ctrl = setup();
    const f = TestBed.createComponent(Rules); f.detectChanges();
    ctrl.expectOne('/api/rules').flush(rules);
    ctrl.expectOne('/api/channels').flush(channels);
    ctrl.expectOne('/api/channels/types').flush(types);
    await f.whenStable();
    return Object.assign(f.nativeElement as HTMLElement, { fixture: f });
  }

  it('warns when every channel of an active rule is paused', async () => {
    const el = await open([rule('r1', 'Quakes', ['c1'])], [ch('c1', 'Email', false)], ['Email']);
    expect(el.querySelector('[role=note]')?.textContent).toContain('No active channel');
  });

  it('warns when an administrator switched the channel type off', async () => {
    const el = await open([rule('r1', 'Quakes', ['c1'])], [ch('c1', 'Slack', true)], ['Email']);
    expect(el.querySelector('[role=note]')?.textContent).toContain('No active channel');
  });

  it('is quiet when at least one channel works, and for rules that are paused anyway', async () => {
    const el = await open([rule('r1', 'Ok', ['c1', 'c2']), rule('r2', 'Paused', ['c2'], false)], [ch('c1', 'Email', true), ch('c2', 'Email', false)], ['Email']);
    expect(el.querySelector('[role=note]')).toBeNull();
  });

  it('shows when a rule last produced an alert, and gives every action a name that says what it acts on', async () => {
    const r = { ...rule('r1', 'Quakes', ['c1']), lastNotifiedAt: '2026-06-01T10:00:00Z' };
    const el = await open([r], [ch('c1', 'Email', true)], ['Email']);
    expect(el.querySelector('tbody')?.textContent).not.toContain('Never');
    expect(el.querySelector('button[aria-label="Delete rule Quakes"]')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Edit rule Quakes"]')).not.toBeNull();
    expect(el.querySelector('a button')).toBeNull(); // no button nested inside a link (two tab stops, invalid markup)
  });

  it('says what deleting a rule does before the user confirms', async () => {
    const el = await open([rule('r1', 'Quakes', ['c1'])], [ch('c1', 'Email', true)], ['Email']);
    expect(el.textContent).not.toContain('removes the history');
    (el.querySelector('button[aria-label="Delete rule Quakes"]') as HTMLButtonElement).click();
    await el.fixture.whenStable();
    expect(el.textContent).toContain('also removes the history of the alerts it produced');
    expect(el.textContent).toContain('Yes, delete');
  });
});

describe('Session that ended while the user was working', () => {
  it('goes to the sign-in page with an explanation, which disappears after the next sign-in', async () => {
    sessionStorage.setItem('eam.session', JSON.stringify({ token: 't', role: 'User', expiresAt: future() }));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] });
    const ctrl = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    expect(auth.isSignedIn()).toBe(true);

    TestBed.inject(HttpClient).get('/api/rules').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/rules').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.notice()).toContain('session has ended');

    const f = TestBed.createComponent(Login); f.detectChanges(); await f.whenStable();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Your session has ended');

    auth.login('a@b.com').subscribe();
    ctrl.expectOne('/api/auth/login').flush({ token: 't2', role: 'User', expiresAt: future() });
    expect(auth.notice()).toBe('');
  });

  it('a wrong admin key is not mistaken for an ended session', async () => {
    const ctrl = setup();
    const auth = TestBed.inject(AuthService);
    auth.adminLogin('wrong').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/auth/admin-login').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.notice()).toBe('');
  });
});

describe('Admin screens that were missing', () => {
  it('users: lists everyone and points out someone with rules but no channel', async () => {
    const ctrl = setup();
    const f = TestBed.createComponent(AdminUsers); f.detectChanges();
    ctrl.expectOne('/api/admin/users').flush([
      { id: '1', email: 'a@example.com', createdAt: '2026-06-01T10:00:00Z', rules: 2, channels: 0 },
      { id: '2', email: 'b@example.com', createdAt: '2026-06-01T10:00:00Z', rules: 1, channels: 1 }]);
    await f.whenStable();
    const text = (f.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('a@example.com');
    expect(text).toContain('rules but no channel');
    expect(text.match(/rules but no channel/g)?.length).toBe(1);
  });

  it('rules: shows every rule with its owner and when it last produced an alert', async () => {
    const ctrl = setup();
    const f = TestBed.createComponent(AdminRules); f.detectChanges();
    ctrl.expectOne('/api/admin/rules').flush([
      { id: '1', name: 'Quakes', userEmail: 'a@example.com', category: 'Disasters', minSeverity: 2, isEnabled: true, cooldownSeconds: 600, lastNotifiedAt: null, channelCount: 1 }]);
    await f.whenStable();
    const text = (f.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('a@example.com');
    expect(text).toContain('Quakes');
    expect(text).toContain('Never');
    expect(text).toContain('10 min');
  });
});

describe('Routes: nothing is left unguarded', () => {
  const flat = routes.filter((r) => r.path !== undefined && r.path !== '**' && r.path !== '');

  it('every admin page needs the administrator guard', () => {
    const admin = flat.filter((r) => r.path!.startsWith('admin/') && r.path !== 'admin/login' || r.path === 'admin');
    expect(admin.length).toBeGreaterThanOrEqual(7);
    for (const r of admin) expect(r.canActivate, `route ${r.path}`).toEqual([adminGuard]);
  });

  it('every user page needs the user guard', () => {
    const user = flat.filter((r) => !r.path!.startsWith('admin') && r.path !== 'login');
    expect(user.length).toBeGreaterThanOrEqual(5);
    for (const r of user) expect(r.canActivate, `route ${r.path}`).toEqual([userGuard]);
  });

  it('only the two sign-in pages are open', () => {
    const open = flat.filter((r) => !r.canActivate).map((r) => r.path);
    expect(open.sort()).toEqual(['admin/login', 'login']);
  });

  it('every page has a title, so browser tabs and screen readers can tell them apart', () => {
    for (const r of flat) expect(r.title, `route ${r.path}`).toBeTruthy();
  });
});

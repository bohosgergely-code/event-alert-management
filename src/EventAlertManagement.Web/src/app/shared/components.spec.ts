import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { Login } from '../features/auth/login';
import { AdminLogin } from '../features/auth/admin-login';
import { Notice } from './notice';
import { Pager } from './pager';
import { authInterceptor } from '../core/auth.interceptor';

function configure() {
  sessionStorage.clear();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
  });
  return { ctrl: TestBed.inject(HttpTestingController), router: TestBed.inject(Router) };
}

const future = () => new Date(Date.now() + 3600_000).toISOString();

async function type(root: HTMLElement, selector: string, value: string) {
  const input = root.querySelector(selector) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('Login screen', () => {
  it('signs in with the typed address and moves on to the rules', async () => {
    const { ctrl, router } = configure();
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    await type(el, '#email', '  me@example.com ');
    await fixture.whenStable();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const req = ctrl.expectOne('/api/auth/login');
    expect(req.request.body).toEqual({ email: 'me@example.com' }); // trimmed
    req.flush({ token: 't', role: 'User', expiresAt: future() });

    expect(nav).toHaveBeenCalledWith('/rules');
  });

  it('shows what went wrong and lets the user try again', async () => {
    const { ctrl } = configure();
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    await type(el, '#email', 'bad');
    await fixture.whenStable();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    ctrl.expectOne('/api/auth/login').flush({ title: 'x', errors: { email: ['A valid email address is required.'] } }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('A valid email address is required.');
    expect((el.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('cannot be submitted while the address is empty', async () => {
    configure();
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    expect((fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
  });
});

describe('Admin sign-in screen', () => {
  it('uses a password field and clears the key after a refusal', async () => {
    const { ctrl } = configure();
    const fixture = TestBed.createComponent(AdminLogin);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect((el.querySelector('#key') as HTMLInputElement).type).toBe('password');

    await type(el, '#key', 'wrong');
    await fixture.whenStable();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    ctrl.expectOne('/api/auth/admin-login').flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();

    expect((el.querySelector('#key') as HTMLInputElement).value).toBe('');
    expect(el.querySelector('[role=alert]')).not.toBeNull();
  });
});

describe('Notice', () => {
  async function render(inputs: { errors?: string[]; message?: string }) {
    const f = TestBed.createComponent(Notice);
    if (inputs.errors) f.componentRef.setInput('errors', inputs.errors);
    if (inputs.message) f.componentRef.setInput('message', inputs.message);
    await f.whenStable();
    return f.nativeElement as HTMLElement;
  }

  it('shows nothing when there is nothing to say', async () => {
    expect((await render({})).querySelector('.notice')).toBeNull();
  });
  it('shows a single error as a sentence', async () => {
    const el = await render({ errors: ['One.'] });
    expect(el.querySelector('[role=alert]')?.textContent).toContain('One.');
    expect(el.querySelector('li')).toBeNull();
  });
  it('shows several errors as a list', async () => {
    expect((await render({ errors: ['One.', 'Two.'] })).querySelectorAll('li').length).toBe(2);
  });
  it('announces success politely', async () => {
    expect((await render({ message: 'Saved.' })).querySelector('[role=status]')?.textContent).toContain('Saved.');
  });
});

describe('Pager', () => {
  async function render(page: number, total: number) {
    const f = TestBed.createComponent(Pager);
    f.componentRef.setInput('page', page);
    f.componentRef.setInput('pageSize', 20);
    f.componentRef.setInput('total', total);
    const went: number[] = [];
    f.componentInstance.go.subscribe((p) => went.push(p));
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    return { el, went, buttons: Array.from(el.querySelectorAll('button')) as HTMLButtonElement[] };
  }

  it('is hidden when everything fits on one page', async () => {
    expect((await render(1, 20)).buttons.length).toBe(0);
  });
  it('counts pages by rounding up and disables the ends', async () => {
    const { el, buttons } = await render(1, 45);
    expect(el.textContent).toContain('Page 1 of 3');
    expect(buttons[0].disabled).toBe(true);
    expect(buttons[1].disabled).toBe(false);
  });
  it('asks for the neighbouring page', async () => {
    const { buttons, went } = await render(2, 45);
    buttons[1].click();
    buttons[0].click();
    expect(went).toEqual([3, 1]);
  });
  it('cannot go past the last page', async () => {
    expect((await render(3, 45)).buttons[1].disabled).toBe(true);
  });
});

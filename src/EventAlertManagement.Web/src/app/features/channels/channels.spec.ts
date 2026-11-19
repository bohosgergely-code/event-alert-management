import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Channels } from './channels';
import { authInterceptor } from '../../core/auth.interceptor';

async function setup(types: string[] = ['Email', 'Slack', 'Webhook']) {
  sessionStorage.clear();
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] });
  const ctrl = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(Channels);
  fixture.detectChanges();
  ctrl.expectOne('/api/channels/types').flush(types);
  ctrl.expectOne('/api/channels').flush([]);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  return { ctrl, fixture, el };
}

async function choose(el: HTMLElement, fixture: { whenStable(): Promise<unknown> }, type: string) {
  const select = el.querySelector('#type') as HTMLSelectElement;
  select.value = type;
  select.dispatchEvent(new Event('change'));
  await fixture.whenStable();
}

async function type(el: HTMLElement, fixture: { whenStable(): Promise<unknown> }, selector: string, value: string) {
  const input = el.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  await fixture.whenStable();
}

describe('Channels screen: add form', () => {
  it('offers the types the API offers, the first one selected', async () => {
    const { el } = await setup();
    const options = Array.from(el.querySelectorAll('#type option')).map((o) => o.textContent?.trim());
    expect(options).toEqual(['Email', 'Slack', 'Webhook']);
  });

  it('shows a URL field and a masked secret field for the webhook', async () => {
    const { el, fixture } = await setup();
    await choose(el, fixture, 'Webhook');
    expect((el.querySelector('#f-url') as HTMLInputElement).type).toBe('url');
    const secret = el.querySelector('#f-secret') as HTMLInputElement;
    expect(secret.type).toBe('password');
    expect(secret.getAttribute('autocomplete')).toBe('new-password');
    expect(el.querySelector('#json')).toBeNull();
  });

  it('falls back to a raw settings box for a channel type it has no form for', async () => {
    const { el, fixture } = await setup(['Email', 'Carrier-Pigeon']);
    await choose(el, fixture, 'Carrier-Pigeon');
    expect(el.querySelector('#json')).not.toBeNull();
  });

  it('sends the typed webhook settings and then leaves nothing typed on screen, secret included', async () => {
    const { ctrl, el, fixture } = await setup();
    await choose(el, fixture, 'Webhook');
    await type(el, fixture, '#name', 'Team hook');
    await type(el, fixture, '#f-url', ' https://example.com/h ');
    await type(el, fixture, '#f-secret', 'a-long-enough-secret');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = ctrl.expectOne((r) => r.method === 'POST' && r.url === '/api/channels');
    expect(post.request.body).toEqual({ type: 'Webhook', name: 'Team hook', settings: { url: 'https://example.com/h', secret: 'a-long-enough-secret' } });
    post.flush({ id: '1', type: 'Webhook', name: 'Team hook', isEnabled: true, settingsMasked: 'https://example.com/… (signed)' });
    ctrl.expectOne('/api/channels').flush([]);
    await fixture.whenStable();

    expect((el.querySelector('#f-secret') as HTMLInputElement).value).toBe('');
    expect((el.querySelector('#f-url') as HTMLInputElement).value).toBe('');
    expect((el.querySelector('#name') as HTMLInputElement).value).toBe('');
  });

  it('shows the API explanation when a destination is refused and keeps what was typed so it can be corrected', async () => {
    const { ctrl, el, fixture } = await setup();
    await choose(el, fixture, 'Webhook');
    await type(el, fixture, '#name', 'Bad');
    await type(el, fixture, '#f-url', 'https://127.0.0.1/x');
    await type(el, fixture, '#f-secret', 'a-long-enough-secret');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    ctrl.expectOne('/api/channels').flush(
      { title: 'x', errors: { settings: ['The webhook URL points to a private or internal address.'] } }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('private or internal');
    expect((el.querySelector('#f-url') as HTMLInputElement).value).toBe('https://127.0.0.1/x');
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RuleEditor } from './rule-editor';
import { authInterceptor } from '../../core/auth.interceptor';

async function open() {
  sessionStorage.clear();
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] });
  const ctrl = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(RuleEditor);
  fixture.detectChanges();
  ctrl.expectOne('/api/channels').flush([]);
  ctrl.expectOne('/api/rules/presets').flush([]);
  await fixture.whenStable();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

async function pick(el: HTMLElement, fixture: { whenStable(): Promise<unknown> }, category: string) {
  const select = el.querySelector('#category') as HTMLSelectElement;
  select.value = category;
  select.dispatchEvent(new Event('change'));
  await fixture.whenStable();
}

describe('Rule editor: being honest about what has real data', () => {
  it('says nothing extra for natural disasters, which have a live source', async () => {
    const { el } = await open();
    expect(el.querySelector('[role=note]')).toBeNull();
  });

  it('warns that news and markets only react to test events', async () => {
    const { el, fixture } = await open();
    await pick(el, fixture, 'News');
    expect(el.querySelector('[role=note]')?.textContent).toMatch(/no live news feed.*test events/is);
    await pick(el, fixture, 'Markets');
    expect(el.querySelector('[role=note]')?.textContent).toMatch(/no live market feed.*test events/is);
    await pick(el, fixture, 'Disasters');
    expect(el.querySelector('[role=note]')).toBeNull();
  });

  it('shows the filter fields that belong to the chosen category only', async () => {
    const { el, fixture } = await open();
    expect(el.querySelector('#mag')).not.toBeNull();
    expect(el.querySelector('#tick')).toBeNull();
    await pick(el, fixture, 'Markets');
    expect(el.querySelector('#tick')).not.toBeNull();
    expect(el.querySelector('#mag')).toBeNull();
  });

  it('will not save a rule without a name or a channel, and says why, without calling the server', async () => {
    const { el, fixture } = await open();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    const alert = el.querySelector('[role=alert]')?.textContent ?? '';
    expect(alert).toContain('Give the rule a name.');
    expect(alert).toContain('Choose at least one channel');
    TestBed.inject(HttpTestingController).expectNone('/api/rules');
  });
});

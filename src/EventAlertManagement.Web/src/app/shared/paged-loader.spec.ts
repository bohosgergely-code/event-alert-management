import { DestroyRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { Paged } from '../core/models';
import { PagedLoader } from './paged-loader';

type Row = { id: string };
const page = (ids: string[], p = 1, total = ids.length, pageSize = 20): Paged<Row> => ({ items: ids.map((id) => ({ id })), page: p, pageSize, total });

/** A fetch whose answers the test releases by hand, in any order. */
function controlled() {
  const calls: { page: number; answer: Subject<Paged<Row>> }[] = [];
  const fetch = (p: number) => { const answer = new Subject<Paged<Row>>(); calls.push({ page: p, answer }); return answer; };
  const make = (refreshMs?: number) => TestBed.runInInjectionContext(() => new PagedLoader<Row>(fetch, inject(DestroyRef), refreshMs));
  return { calls, make };
}

describe('PagedLoader', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it('shows what the newest request returned even if an older answer arrives later', () => {
    const { calls, make } = controlled();
    const list = make();
    list.load(1);   // e.g. filter "Failed"
    list.load(1);   // then quickly "Sent"
    calls[1].answer.next(page(['SENT']));
    calls[1].answer.complete();
    calls[0].answer.next(page(['FAILED'])); // the older one answers last: it was cancelled and must be ignored
    expect(list.items().map((r) => r.id)).toEqual(['SENT']);
  });

  it('the refresh timer reloads the page the user asked for, not the one shown before they clicked', () => {
    vi.useFakeTimers();
    try {
      const { calls, make } = controlled();
      const list = make(10_000);
      list.load(1);
      calls[0].answer.next(page(['a'], 1, 60)); calls[0].answer.complete();

      list.load(2);                       // the user clicks "Next"
      vi.advanceTimersByTime(10_000);     // and the timer fires before the answer arrives
      const refresh = calls[calls.length - 1];
      expect(refresh.page).toBe(2);       // it asks for page 2, not page 1
    } finally { vi.useRealTimers(); }
  });

  it('steps back to the last real page when the page the user is on has disappeared', () => {
    const { calls, make } = controlled();
    const list = make();
    list.load(3);
    calls[0].answer.next(page([], 3, 41)); calls[0].answer.complete(); // 41 rows = 3 pages of 20, but page 3 is now empty? total says pages exist
    // total 41 with pageSize 20 gives 3 pages, so page 3 empty is possible only if rows shrank: simulate shrink to 21 rows
    list.load(3);
    calls[calls.length - 1].answer.next(page([], 3, 21));
    const back = calls[calls.length - 1];
    expect(back.page).toBe(2);
    back.answer.next(page(['x'], 2, 21));
    expect(list.page()).toBe(2);
    expect(list.items().map((r) => r.id)).toEqual(['x']);
  });

  it('a genuinely empty list stays empty and does not loop', () => {
    const { calls, make } = controlled();
    const list = make();
    list.load(1);
    calls[0].answer.next(page([], 1, 0));
    expect(calls.length).toBe(1);
    expect(list.items()).toEqual([]);
    expect(list.loading()).toBe(false);
  });

  it('a failed load is explained, a failed background refresh stays quiet', () => {
    const { calls, make } = controlled();
    const list = make();
    list.load(1);
    calls[0].answer.error({ status: 500 });
    expect(list.errors().length).toBe(1);

    list.load(1, true);
    list.errors.set([]);
    calls[1].answer.error({ status: 500 });
    expect(list.errors()).toEqual([]);
  });

  it('shows "loading" only for loads the user asked for', () => {
    const { calls, make } = controlled();
    const list = make();
    list.load(1);
    expect(list.loading()).toBe(true);
    calls[0].answer.next(page(['a']));
    expect(list.loading()).toBe(false);
    list.load(1, true);
    expect(list.loading()).toBe(false);
  });
});

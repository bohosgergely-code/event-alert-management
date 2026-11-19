import { DestroyRef, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, of, switchMap, timer } from 'rxjs';
import { Paged } from '../core/models';
import { describeError } from '../core/errors';

/**
 * Loads a filtered, paged list and keeps the screen consistent while things change underneath it.
 *
 * - Only the NEWEST request counts. If the user changes a filter twice quickly, or the refresh timer fires in the
 *   middle of a page change, an older answer arriving late can no longer overwrite what is on screen.
 * - The page the user asked for is remembered, so the refresh timer reloads that page and not the old one.
 * - If the list shrinks and the page the user is on no longer exists, it steps back to the last page that does.
 *
 * Create it as a field of a component (it needs the injection context to stop its timer on its own).
 */
export class PagedLoader<T> {
  readonly items = signal<T[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly errors = signal<string[]>([]);

  private readonly requests = new Subject<{ page: number; quiet: boolean }>();
  private requestedPage = 1;

  constructor(
    private readonly fetch: (page: number, pageSize: number) => Observable<Paged<T>>,
    destroyRef: DestroyRef,
    refreshEveryMs?: number,
  ) {
    this.requests
      .pipe(
        switchMap((r) =>
          this.fetch(r.page, this.pageSize()).pipe(
            map((result) => ({ r, result, error: null as unknown })),
            catchError((error: unknown) => of({ r, result: null, error })),
          ),
        ),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe(({ r, result, error }) => {
        this.loading.set(false);
        if (result === null) {
          if (!r.quiet) this.errors.set(describeError(error)); // a failed background refresh stays quiet
          return;
        }
        this.items.set(result.items);
        this.page.set(result.page);
        this.pageSize.set(result.pageSize);
        this.total.set(result.total);

        const lastPage = Math.max(1, Math.ceil(result.total / result.pageSize));
        if (result.items.length === 0 && result.total > 0 && result.page > lastPage) this.load(lastPage, true);
      });

    if (refreshEveryMs) timer(refreshEveryMs, refreshEveryMs).pipe(takeUntilDestroyed(destroyRef)).subscribe(() => this.load(this.requestedPage, true));
  }

  /** <code>quiet</code> reloads without the "Loading…" state and without showing errors, for background refreshes. */
  load(page = 1, quiet = false): void {
    this.requestedPage = page;
    if (!quiet) { this.loading.set(true); this.errors.set([]); }
    this.requests.next({ page, quiet });
  }
}

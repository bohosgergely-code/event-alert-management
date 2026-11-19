import { DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';

/**
 * Calls <code>action</code> every few seconds for as long as the screen is open.
 * Used where data changes by itself (deliveries move from pending to sent) so nobody has to reload by hand.
 * Must be called from a constructor or field initializer (it needs the injection context to stop on its own).
 */
export function autoRefresh(action: () => void, everyMs = 5000): void {
  const destroyRef = inject(DestroyRef);
  timer(everyMs, everyMs).pipe(takeUntilDestroyed(destroyRef)).subscribe(() => action());
}

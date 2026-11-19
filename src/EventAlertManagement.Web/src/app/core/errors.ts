import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns any failed request into readable sentences for the screen.
 * The API answers with problem details: a title, and for validation errors a map of field to messages.
 */
export function describeError(error: unknown): string[] {
  if (!(error instanceof HttpErrorResponse)) return ['Something unexpected went wrong.'];

  if (error.status === 0) return ['Cannot reach the server. Check that the API is running.'];
  if (error.status === 429) return ['Too many requests. Please wait a moment and try again.'];
  if (error.status === 401) return ['Your session has ended or the details were not accepted.'];
  if (error.status === 403) return ['You do not have access to this.'];
  if (error.status === 404) return ['That item was not found.'];
  if (error.status >= 500) return ['The server had a problem. Please try again.'];

  const body = error.error as { title?: string; errors?: Record<string, string[]> } | null;
  const fieldMessages = Object.values(body?.errors ?? {}).flat().filter((m) => !!m);
  if (fieldMessages.length > 0) return fieldMessages;
  if (body?.title) return [body.title];
  return ['The request was not accepted.'];
}

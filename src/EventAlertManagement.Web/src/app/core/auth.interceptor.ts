import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const isSignIn = (url: string) => url.startsWith('/api/auth/');

/** Adds the token to API calls, and signs the user out when the server says the token is no longer good. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const isApi = req.url.startsWith('/api/');
  const token = auth.token;

  const outgoing = isApi && !isSignIn(req.url) && token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && isApi && !isSignIn(req.url)) auth.logout();
      return throwError(() => error);
    }),
  );
};

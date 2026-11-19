import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Pages for signed-in users. Anyone else goes to the right sign-in page. */
export const userGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.role() === 'User') return true;
  return router.parseUrl(auth.isAdmin() ? '/admin' : '/login');
};

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.role() === 'Admin') return true;
  return router.parseUrl(auth.role() === 'User' ? '/rules' : '/admin/login');
};

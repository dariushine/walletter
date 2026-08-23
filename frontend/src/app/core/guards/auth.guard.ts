import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from '../services/auth-store';

/**
 * Guard de rutas autenticadas. Si la API tiene auth habilitada y aún no hay
 * sesión confirmada, redirige a /login.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  if (!auth.authenticationRequired) return true; // API abierta
  if (auth.authenticated) return true;

  return router.createUrlTree(['/login']);
};

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, take } from 'rxjs/operators';
import { AuthStore } from '../services/auth-store';

/**
 * Guard de rutas autenticadas. Inicializa el estado de sesión si hace falta
 * (consulta /auth/status + /auth/session) y, si la API exige autenticación y no
 * hay sesión válida, redirige a /login.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  // Inicializa (idempotente) y espera a que el estado esté listo antes de decidir.
  return auth.initialize().pipe(
    take(1),
    map(() => {
      if (!auth.authenticationRequired) return true; // API abierta
      if (auth.isAuthenticated) return true;
      return router.createUrlTree(['/login']);
    })
  );
};

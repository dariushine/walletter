import { HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, throwError, BehaviorSubject } from 'rxjs';
import { catchError, filter, switchMap, take } from 'rxjs/operators';
import { NotificationService } from '../services/notification.service';
import { AuthStore } from '../services/auth-store';
import { WalletterApiService } from '../services/walletter-api.service';

/**
 * Interceptor global HTTP:
 * - Adjunta withCredentials para que las cookies httpOnly (access_token / refresh_token) viajen.
 * - Parsea errores del backend (BusinessException/NotFoundException) a un mensaje legible.
 * - Refresh automático: si una request autenticada devuelve 401, intenta renovar el
 *   access token con la cookie httpOnly refresh_token (POST /auth/refresh). Si el
 *   refresh es exitoso, reintenta la request original. Si falla, la sesión caducó de
 *   verdad → marca no autenticado (el guard redirige a /login).
 */
export const httpInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  // Los inject() deben ir aquí (contexto de inyección del interceptor), no dentro
  // de callbacks asíncronos (switchMap/catchError) donde inject() falla en runtime.
  const notifier = inject(NotificationService);
  const auth = inject(AuthStore);
  const api = inject(WalletterApiService);

  const isApi = req.url.startsWith('/api');
  const apiReq = isApi ? req.clone({ withCredentials: true }) : req;

  return next(apiReq).pipe(
    catchError((error: HttpErrorResponse) => {
      // 401 en una request autenticada (no de auth) → intentar renovar sesión.
      if (isApi && error.status === 401 && !isAuthRequest(req.url)) {
        return handleUnauthorized(req, next, api, auth, notifier);
      }
      if (isApi && error.status !== 401) {
        notifier.error(extractErrorMessage(error));
      }
      return throwError(() => error);
    })
  );
};

/** Requests de auth que no deben entrar en el ciclo de refresh (evita recursión). */
function isAuthRequest(url: string): boolean {
  return url.includes('/auth/login') || url.includes('/auth/refresh') || url.includes('/auth/status');
}

// Estado compartido: mientras se refresca el token, las demás requests 401 esperan.
let refreshing = false;
const refreshSubject = new BehaviorSubject<boolean | null>(null);

function handleUnauthorized(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  api: WalletterApiService,
  auth: AuthStore,
  notifier: NotificationService
): Observable<HttpEvent<unknown>> {
  if (!refreshing) {
    refreshing = true;
    refreshSubject.next(null);
    return api.refresh().pipe(
      switchMap(() => {
        refreshing = false;
        refreshSubject.next(true);
        return next(req.clone({ withCredentials: true })).pipe(
          catchError((e: HttpErrorResponse) => {
            if (e.status === 401) {
              auth.setAuthenticated(false);
              notifier.error('Tu sesión expiró. Vuelve a iniciar sesión.');
            }
            return throwError(() => e);
          })
        );
      }),
      catchError((e) => {
        refreshing = false;
        refreshSubject.next(false);
        auth.setAuthenticated(false);
        if (e?.status !== 401) notifier.error(extractErrorMessage(e));
        return throwError(() => e);
      })
    );
  }

  // Ya hay un refresh en curso: espera a que termine y reintenta.
  return refreshSubject.pipe(
    filter((v) => v !== null),
    take(1),
    switchMap((ok) => {
      if (ok) return next(req.clone({ withCredentials: true }));
      return throwError(() => new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' }));
    })
  );
}

function extractErrorMessage(error: HttpErrorResponse): string {
  if (error.error?.error && typeof error.error.error === 'string') {
    return error.error.error;
  }
  if (typeof error.error?.message === 'string') {
    return error.error.message;
  }
  if (error.error && typeof error.error === 'string') {
    return error.error;
  }
  switch (error.status) {
    case 400:
      return 'Solicitud inválida';
    case 401:
      return 'No autenticado';
    case 403:
      return 'No autorizado';
    case 404:
      return 'No encontrado';
    case 409:
      return 'Conflicto con los datos actuales';
    case 500:
      return 'Error interno del servidor';
    default:
      return 'Error de conexión con el servidor';
  }
}

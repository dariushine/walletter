import { HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest, HttpResponse, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { NotificationService } from '../services/notification.service';

/**
 * Interceptor global HTTP:
 * - Adjunta withCredentials para que las cookies httpOnly (access_token / refresh_token) viajen.
 * - Parsea errores del backend (BusinessException/NotFoundException) a un mensaje legible.
 * - No hace refresh automático 401 en el cliente: las cookies httpOnly ya rotan
 *   el access token vía el endpoint /auth/refresh cuando expira (proxy same-origin).
 */
export const httpInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  // Solo adjuntar credenciales a peticiones a nuestra API.
  const isApi = req.url.startsWith('/api');
  const apiReq = isApi ? req.clone({ withCredentials: true }) : req;

  return next(apiReq).pipe(
    catchError((error: HttpErrorResponse) => {
      const message = extractErrorMessage(error);
      const notifier = inject(NotificationService);
      if (isApi && error.status !== 401) {
        // 401 (no autenticado) se maneja en el guard/rutas, no como toast ruidoso.
        notifier.error(message);
      }
      return throwError(() => error);
    })
  );
};

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

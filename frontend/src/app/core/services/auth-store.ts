import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { filter, take } from 'rxjs/operators';
import { WalletterApiService } from './walletter-api.service';

/**
 * Estado de autenticación del lado del cliente.
 * Las credenciales son cookies httpOnly gestionadas por el backend; aquí solo
 * guardamos si la API está "abierta" (auth deshabilitada) o si hay una sesión
 * activa. Se inicializa al arrancar consultando /auth/status + /auth/session,
 * de modo que el guard de rutas pueda redirigir a /login cuando no hay sesión.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly authenticated$ = new BehaviorSubject<boolean>(false);
  private readonly authEnabled$ = new BehaviorSubject<boolean>(true);
  private readonly initialized$ = new BehaviorSubject<boolean>(false);

  /** Estado síncrono (para el guard). */
  private _authenticated = false;
  private _authEnabled = true;
  private _initialized = false;
  private _initStarted = false;

  constructor(private readonly api: WalletterApiService) {}

  readonly authenticated = this.authenticated$.asObservable();
  readonly authEnabled = this.authEnabled$.asObservable();
  /** Emite true cuando ya se consultó status/session (estado inicial listo). */
  readonly initialized = this.initialized$.asObservable();

  /** true: hay que mostrar login. false: API abierta, no hace falta. */
  get authenticationRequired(): boolean {
    return this._authEnabled;
  }

  /** true si hay una sesión confirmada (solo tras initialize()). */
  get isAuthenticated(): boolean {
    return this._authenticated;
  }

  get isInitialized(): boolean {
    return this._initialized;
  }

  /**
   * Inicializa el estado consultando /auth/status + /auth/session (una sola vez)
   * y devuelve un observable que emite true cuando el estado inicial está listo.
   */
  initialize(): Observable<boolean> {
    if (!this._initStarted) {
      this._initStarted = true;
      this.api.authStatus().subscribe({
        next: (status) => {
          this._authEnabled = !!status?.enabled;
          this.authEnabled$.next(this._authEnabled);
          // /auth/session es [Authorize]: 200 si hay sesión, 401 si no.
          this.api.authSession().subscribe({
            next: () => {
              this._authenticated = true;
              this.authenticated$.next(true);
              this.finishInit();
            },
            error: () => {
              // Sin sesión válida. Con auth deshabilitada igual dejamos pasar.
              this._authenticated = !this._authEnabled;
              this.authenticated$.next(this._authenticated);
              this.finishInit();
            },
          });
        },
        error: () => {
          this.finishInit();
        },
      });
    }
    // Emite true solo cuando ya esté inicializado (evita decidir antes de tiempo).
    return this.initialized$.pipe(filter(Boolean), take(1));
  }

  private finishInit(): void {
    this._initialized = true;
    this.initialized$.next(true);
  }

  setAuthenticated(v: boolean): void {
    this._authenticated = v;
    this.authenticated$.next(v);
  }

  setAuthEnabled(v: boolean): void {
    this._authEnabled = v;
    this.authEnabled$.next(v);
  }
}

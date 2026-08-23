import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

/**
 * Estado de autenticación del lado del cliente.
 * Las credenciales son cookies httpOnly gestionadas por el backend; aquí solo
 * guardamos si la API está "abierta" (auth deshabilitada) o si hay una sesión
 * activa (en memoria, se revalida con /auth/session y /auth/status al iniciar).
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly authenticated$ = new BehaviorSubject<boolean>(false);
  private readonly authEnabled$ = new BehaviorSubject<boolean>(true);

  readonly authenticated = this.authenticated$.asObservable();
  readonly authEnabled = this.authEnabled$.asObservable();

  /** true: hay que mostrar login. false: API abierta, no hace falta. */
  get authenticationRequired(): boolean {
    return this.authEnabled$.value;
  }

  setAuthenticated(v: boolean): void {
    this.authenticated$.next(v);
  }

  setAuthEnabled(v: boolean): void {
    this.authEnabled$.next(v);
  }
}

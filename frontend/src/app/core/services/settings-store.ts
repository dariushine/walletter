import { Injectable, signal } from '@angular/core';
import { tap } from 'rxjs/operators';
import { WalletterApiService } from './walletter-api.service';

/**
 * Estado global de zona horaria del usuario (proyecta las fechas UTC).
 * Se carga al arrancar desde GET /api/settings y se puede cambiar.
 */
@Injectable({ providedIn: 'root' })
export class SettingsStore {
  private readonly _timezone = signal<string>('America/Caracas');
  private readonly _loaded = signal<boolean>(false);

  /** Signal con la zona horaria actual. */
  readonly timezone = this._timezone.asReadonly();
  readonly loaded = this._loaded.asReadonly();

  constructor(private readonly api: WalletterApiService) {}

  loadTimezone(): void {
    if (this._loaded()) return;
    this.api
      .settings()
      .pipe(
        tap((s) => {
          this._timezone.set(s.timezone || 'America/Caracas');
          this._loaded.set(true);
        })
      )
      .subscribe({ error: () => this._loaded.set(true) });
  }

  setTimezone(tz: string) {
    return this.api.setTimeZone(tz).pipe(tap(() => this._timezone.set(tz)));
  }
}

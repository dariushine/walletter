import { Injectable, signal } from '@angular/core';

/**
 * Preferencias de UI del usuario, persistidas en localStorage.
 * - hideBalances: oculta/descubre los saldos en dashboard y lista de billeteras.
 * - decimalSeparator: ',' (coma) o '.' (punto) para cifras decimales.
 * Se usa un mensaje de evento para sincronizar componentes en vivo.
 */
export type DecimalSeparator = ',' | '.';

const HIDE_KEY = 'walletter.ocultarSaldos';
const SEP_KEY = 'walletter.separadorDecimal';
const EVENT_KEY = 'walletter:uiPrefs:change';

@Injectable({ providedIn: 'root' })
export class UiPreferenceStore {
  readonly hideBalances = signal<boolean>(this.read(HIDE_KEY) === '1');
  readonly decimalSeparator = signal<DecimalSeparator>(this.read(SEP_KEY) === '.' ? '.' : ',');

  setHideBalances(value: boolean): void {
    try {
      if (value) localStorage.setItem(HIDE_KEY, '1');
      else localStorage.removeItem(HIDE_KEY);
    } catch {
      /* silencioso */
    }
    this.hideBalances.set(value);
    this.emit();
  }

  setDecimalSeparator(value: DecimalSeparator): void {
    try {
      localStorage.setItem(SEP_KEY, value);
    } catch {
      /* silencioso */
    }
    this.decimalSeparator.set(value);
    this.emit();
  }

  private read(key: string): string | null {
    try {
      return typeof window !== 'undefined' ? localStorage.getItem(key) : null;
    } catch {
      return null;
    }
  }

  private emit(): void {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(new CustomEvent(EVENT_KEY));
    }
  }
}

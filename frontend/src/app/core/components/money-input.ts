import { Component, forwardRef, inject, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { UiPreferenceStore } from '../services/ui-preference.store';
import { formatNumber } from '../utils/money';

/**
 * Campo de monto estilo banca (ControlValueAccessor).
 *
 * Comportamiento:
 *  - Los dígitos "entran" desde los decimales: empieza en 0,00; escribir 1 → 0,01,
 *    0 → 0,10, 0 → 1,00, 5 → 10,05.
 *  - Backspace borra SOLO el último dígito (nunca se vacía ni borra de a dos).
 *  - El cursor SIEMPRE va al final tras cada cambio.
 *  - Pegar interpreta el valor completo tomando el último separador [,.] seguido de
 *    1-2 dígitos como decimal; si no hay decimal final, todo es entero.
 *    Ej: '1.000.000,50' → 50 decimales · '1,005.1' → 1.005,10 · '1142'/'1.142' → 1.142,00.
 *  - Sufijo de moneda al final: muestra `currency` si existe, si no `USD/VES`.
 *
 * El valor de form es un número en unidades (e.g. 10.05).
 */
@Component({
  selector: 'app-money-input',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => MoneyInput), multi: true }],
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <mat-form-field appearance="outline" [class.full]="full" class="money-field">
      <mat-label>{{ label }}</mat-label>
      <input
        matInput
        type="text"
        [value]="display"
        (input)="onInput($event)"
        (keydown)="onKeyDown($event)"
        (keydown.backspace)="onBackspace($event)"
        (paste)="onPaste($event)"
        (focus)="forceEnd()"
        (click)="forceEnd()"
        inputmode="decimal"
        autocomplete="off"
        [disabled]="disabled"
      />
      <span matSuffix class="cur-suffix">{{ currency || 'USD/VES' }}</span>
    </mat-form-field>
  `,
  styles: [
    `
      :host { display: block; } // para que flex:1 funcione dentro de .form-row.two
      .money-field { width: 100%; }
      .full { width: 100%; }
      .cur-suffix {
        font-size: 0.8rem;
        color: rgba(0, 0, 0, 0.6);
        padding-right: 14px;
        letter-spacing: 0.02em;
      }
    `,
  ],
})
export class MoneyInput implements ControlValueAccessor {
  private readonly prefs = inject(UiPreferenceStore);

  @Input() label = 'Monto';
  @Input() full = true;
  /** Código de moneda (USD/VES) que se muestra como sufijo; si falta → 'USD/VES'. */
  @Input() currency: string | null | undefined = null;

  // Representación interna en centavos (entero) = el "buffer" del teclado bancario.
  private cents = 0;

  disabled = false;
  display = '';

  private onChange: (v: number | null) => void = () => {};
  private onTouched: () => void = () => {};

  constructor() {
    this.render();
  }

  // --- ControlValueAccessor ---
  writeValue(value: number | null | undefined): void {
    const n = typeof value === 'number' && Number.isFinite(value) ? value : 0;
    this.cents = Math.round(n * 100);
    this.render();
  }

  registerOnChange(fn: (v: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  // --- Render / helpers ---
  private render(): void {
    const sep = this.prefs.decimalSeparator();
    this.display = formatNumber(this.cents / 100, 2, sep);
  }

  private commit(newCents: number): void {
    this.cents = newCents;
    this.render();
    this.onChange(this.cents / 100);
    this.onTouched();
  }

  forceEnd(): void {
    const el = document.activeElement as HTMLInputElement | null;
    if (el && el.tagName === 'INPUT') {
      const len = el.value.length;
      el.setSelectionRange(len, len);
    }
  }

  // --- Digit entry (bank-style) ---
  onInput(e: Event): void {
    const input = e.target as HTMLInputElement;
    const digits = input.value.replace(/\D/g, '').slice(0, 12);
    const next = digits ? Math.min(Number(digits), 999999999999) : 0;
    this.commit(next);
    // Tras el re-render, devolver el caret al final.
    queueMicrotask(() => this.forceEnd());
  }

  // --- Teclado: bloquear letras/símbolos, permitir solo dígitos + control ---
  onKeyDown(e: KeyboardEvent): void {
    // No interferir con atajos (copiar/pegar/seleccionar todo).
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    // Teclas de edición/navegación permitidas.
    const nav = ['Backspace', 'Tab', 'Delete', 'Enter', 'ArrowLeft', 'ArrowRight', 'Home', 'End'];
    if (nav.includes(e.key)) return;
    // Bloquear cualquier tecla que NO sea un dígito (letras, espacio, símbolos).
    // 'e.key.length === 1' = tecla imprimible; los .length>1 son teclas especiales.
    if (e.key.length === 1 && !/[0-9]/.test(e.key)) {
      e.preventDefault();
    }
  }

  // --- Backspace: borra solo el último dígito ---
  onBackspace(e: Event): void {
    e.preventDefault();
    this.commit(Math.floor(this.cents / 10));
    this.forceEnd();
  }

  // --- Paste: interpreta el valor completo (no dígito a dígito) ---
  onPaste(e: ClipboardEvent): void {
    e.preventDefault();
    const raw = e.clipboardData?.getData('text') ?? '';
    this.commit(this.parseToCents(raw));
    this.forceEnd();
  }

  /** Convierte un texto pegado a centavos. Ver docs del componente para los casos. */
  private parseToCents(raw: string): number {
    let s = (raw ?? '').trim();
    if (!s) return 0;
    const m = s.match(/([.,])(\d{1,2})$/);
    if (m && m.index !== undefined) {
      const intPart = s.slice(0, m.index).replace(/[.,]/g, '');
      s = intPart + '.' + m[2];
    } else {
      s = s.replace(/[.,]/g, '');
    }
    const num = parseFloat(s);
    if (!Number.isFinite(num) || num < 0) return 0;
    return Math.round(num * 100);
  }
}

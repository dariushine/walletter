import { Component, Input, OnInit, OnChanges, SimpleChanges, signal, forwardRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, FormsModule } from '@angular/forms';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { WalletterApiService } from '../services/walletter-api.service';
import { Category } from '../../models/walletter.models';

/** Categorías de sistema que no se sugieren ni se pueden crear. */
const SYSTEM_CATEGORIES = ['fee', 'exchange_in', 'exchange_out'];

/** Estilos compartidos (para que el panel del autocomplete de Material respete colores). */
@Component({
  selector: 'app-category-autocomplete',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CategoryAutocomplete), multi: true }],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatIconModule,
  ],
  template: `
    <mat-form-field appearance="outline" [class.full]="full" class="cat-field">
      <mat-label>{{ label }}</mat-label>
      <input
        matInput
        [formControl]="control"
        [placeholder]="placeholder"
        [matAutocomplete]="catAuto"
      />
      @if (control.value) {
        <button matSuffix mat-icon-button type="button" (click)="clear()" aria-label="Limpiar">
          <mat-icon>close</mat-icon>
        </button>
      }
      <mat-autocomplete #catAuto="matAutocomplete" autoActiveFirstOption (optionSelected)="onSelect($event)">
        @for (c of filtered(); track c.id) {
          <mat-option [value]="c.name">
            <span class="cat-opt">
              <span class="cat-dot" [style.background]="c.color || '#3f51b5'"></span>
              {{ c.name }}
            </span>
          </mat-option>
        }
        @if (isNew()) {
          <mat-option [value]="control.value">
            <span class="cat-opt">
              {{ control.value }}
              <span class="new-chip">Nueva</span>
            </span>
          </mat-option>
        }
      </mat-autocomplete>
    </mat-form-field>
  `,
  styles: [`
    .cat-field { width: 100%; }
    .full { width: 100%; }
    .cat-opt { display: inline-flex; align-items: center; gap: 8px; }
    .cat-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .new-chip {
      margin-left: 8px; padding: 1px 8px; border-radius: 999px;
      font-size: 0.7rem; font-weight: 700; color: #fff; background: #3f51b5;
      text-transform: uppercase; letter-spacing: 0.04em;
    }
  `],
})
export class CategoryAutocomplete implements ControlValueAccessor, OnInit, OnChanges {
  private readonly api = inject(WalletterApiService);

  /** Tipo de transacción para filtrar las categorías (income | expense). */
  @Input() type: string | null | undefined = 'expense';
  @Input() label = 'Categoría *';
  @Input() placeholder = 'Ej: Comida';
  @Input() full = true;

  readonly control = new FormControl<string>('');
  readonly allCategories = signal<Category[]>([]);
  readonly filtered = signal<Category[]>([]);

  private onChange: (v: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  ngOnInit(): void {
    this.load();
    this.control.valueChanges.subscribe(() => this.filter());
  }

  ngOnChanges(changes: SimpleChanges): void {
    // Al cambiar el tipo (gasto/ingreso) recarga las categorías correspondientes.
    if (changes['type']) this.load();
  }

  /** Carga Categorías activas, filtra por tipo y excluye sistema. */
  private load(): void {
    const type = this.type ?? 'expense';
    this.api.categories().subscribe({
      next: (cats) => {
        this.allCategories.set(
          cats.filter((c) => c.isActive && c.type === type && !SYSTEM_CATEGORIES.includes(c.name))
        );
        this.filter();
      },
      error: () => undefined,
    });
  }

  private filter(): void {
    const q = (this.control.value ?? '').toLowerCase().trim();
    if (!q) {
      this.filtered.set(this.allCategories());
      return;
    }
    this.filtered.set(this.allCategories().filter((c) => c.name.toLowerCase().includes(q)));
  }

  /** true si lo escrito no existe y es creable → mostrar 'Nueva'. */
  isNew(): boolean {
    const q = (this.control.value ?? '').trim();
    if (!q) return false;
    if (SYSTEM_CATEGORIES.includes(q.toLowerCase())) return false;
    return !this.allCategories().some((c) => c.name.toLowerCase() === q.toLowerCase());
  }

  clear(): void {
    this.control.setValue('');
    this.filter();
  }

  onSelect(ev: any): void {
    this.control.setValue(ev.option.value, { emitEvent: true });
  }

  // ===== ControlValueAccessor =====
  writeValue(v: string | null): void {
    this.control.setValue(v ?? '', { emitEvent: false });
  }
  registerOnChange(fn: (v: string | null) => void): void {
    this.onChange = fn;
    this.control.valueChanges.subscribe((v) => this.onChange(v ?? ''));
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    isDisabled ? this.control.disable() : this.control.enable();
  }
}

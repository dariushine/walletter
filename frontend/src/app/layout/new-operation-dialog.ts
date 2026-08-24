import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogContent, MatDialogActions, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { MatRadioModule } from '@angular/material/radio';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { WalletterApiService } from '../core/services/walletter-api.service';
import { NotificationService } from '../core/services/notification.service';
import { Wallet, Category } from '../models/walletter.models';
import { todayInTimeZone } from '../core/utils/dates';
import { MoneyInput } from '../core/components/money-input';

export interface NewOperationDialogData {
  wallets: Wallet[];
  tz: string;
}

/** Categorías de sistema que no se sugieren ni se pueden crear desde el modal. */
const SYSTEM_CATEGORIES = ['fee', 'exchange_in', 'exchange_out'];


@Component({
  selector: 'app-new-operation-dialog',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    MatRadioModule,
    MatAutocompleteModule,
    MoneyInput,
  ],
  templateUrl: './new-operation-dialog.html',
  styleUrls: ['./new-operation-dialog.scss'],
})
export class NewOperationDialog implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<NewOperationDialog>);
  readonly data = inject<NewOperationDialogData>(MAT_DIALOG_DATA);

  loading = signal(false);
  today = todayInTimeZone(this.data.tz || 'America/Caracas');
  /** Tab activa: 0 = Transacción, 1 = Exchange. */
  readonly activeTab = signal(0);

  /** Todas las categorías activas del backend. */
  readonly allCategories = signal<Category[]>([]);
  /** Categorías que coinciden con lo escrito (filtradas por tipo). */
  readonly filteredCategories = signal<Category[]>([]);

  readonly txForm = this.fb.group({
    walletId: [this.data.wallets[0]?.id ?? null, Validators.required],
    type: ['expense', Validators.required],
    categoryName: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    description: [''],
    fee: [0],
    date: [this.today, Validators.required],
    time: ['12:00', Validators.required],
  });

  readonly exForm = this.fb.group({
    fromWalletId: [this.data.wallets[0]?.id ?? null, Validators.required],
    toWalletId: [this.data.wallets[1]?.id ?? null, Validators.required],
    fromAmount: [0, [Validators.required, Validators.min(0.01)]],
    toAmount: [0, [Validators.required, Validators.min(0.01)]],
    fee: [0],
    creditFee: [0],
    description: [''],
    date: [this.today, Validators.required],
    time: ['12:00', Validators.required],
  });

  setTab(tab: number): void {
    this.activeTab.set(tab);
  }

  /** Cambia el tipo (gasto/ingreso): recarga las categorías correctas y limpia el campo. */
  changeType(type: 'expense' | 'income'): void {
    if ((this.txForm.value.type as string) === type) return;
    this.txForm.patchValue({ type, categoryName: '' });
    this.loadCategories();
  }

  ngOnInit(): void {
    this.loadCategories();
    // Refiltra las sugerencias cuando cambia la escritura del campo categoría.
    this.txForm.controls.categoryName.valueChanges.subscribe(() => this.filterCategories());
  }

  /** Carga las categorías activas y filtra por tipo + excluye las de sistema. */
  private loadCategories(): void {
    const type = this.txForm.value.type as 'income' | 'expense';
    this.api.categories().subscribe({
      next: (cats) => {
        // El backend lista todas las activas; aquí filtro por tipo y excluyo sistema.
        this.allCategories.set(
          cats.filter((c) => c.isActive && c.type === type && !SYSTEM_CATEGORIES.includes(c.name))
        );
        this.filterCategories();
      },
      error: () => undefined,
    });
  }

  /** Filtra las categorías sugeridas por el texto escrito (case-insensitive). */
  filterCategories(): void {
    const q = (this.txForm.controls.categoryName.value ?? '').toLowerCase().trim();
    if (!q) {
      this.filteredCategories.set(this.allCategories());
      return;
    }
    this.filteredCategories.set(
      this.allCategories().filter((c) => c.name.toLowerCase().includes(q))
    );
  }

  /** true si la categoría escrita no existe aún y es creable → mostrar 'Nueva'. */
  isNewCategory(): boolean {
    const q = (this.txForm.controls.categoryName.value ?? '').trim();
    if (!q) return false;
    // Las categorías de sistema NO se pueden crear.
    if (SYSTEM_CATEGORIES.includes(q.toLowerCase())) return false;
    return !this.allCategories().some((c) => c.name.toLowerCase() === q.toLowerCase());
  }

  /** Selecciona una categoría existente. */
  selectCategory(cat: Category): void {
    this.txForm.controls.categoryName.setValue(cat.name);
  }

  save(): void {
    if (this.loading()) return;
    if (this.activeTab() === 0) {
      this.saveTransaction();
    } else {
      this.saveExchange();
    }
  }

  private saveTransaction(): void {
    if (this.txForm.invalid) {
      this.notifier.error('Completa los campos obligatorios');
      return;
    }
    if (SYSTEM_CATEGORIES.includes((this.txForm.value.categoryName ?? '').toLowerCase().trim())) {
      this.notifier.error('Esa categoría es de sistema y no puede usarse');
      return;
    }
    this.loading.set(true);
    const v = this.txForm.value;
    this.api
      .createTransaction({
        walletId: v.walletId!,
        categoryName: v.categoryName!,
        type: (v.type as 'income' | 'expense')!,
        amount: Number(v.amount) || 0,
        description: v.description || undefined,
        fee: Number(v.fee) || 0,
        date: v.date!,
        time: v.time!,
        tz: this.data.tz,
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.notifier.success('Transacción creada');
          this.dialogRef.close(true);
        },
        error: () => this.loading.set(false),
      });
  }

  private saveExchange(): void {
    if (this.exForm.invalid) {
      this.notifier.error('Completa los campos obligatorios');
      return;
    }
    if (this.exForm.value.fromWalletId === this.exForm.value.toWalletId) {
      this.notifier.error('Las billeteras origen y destino deben ser diferentes');
      return;
    }
    this.loading.set(true);
    const v = this.exForm.value;
    this.api
      .createExchange({
        fromWalletId: v.fromWalletId!,
        toWalletId: v.toWalletId!,
        fromAmount: Number(v.fromAmount) || 0,
        toAmount: Number(v.toAmount) || 0,
        fee: Number(v.fee) || 0,
        creditFee: Number(v.creditFee) || 0,
        description: v.description || undefined,
        date: v.date!,
        time: v.time!,
        tz: this.data.tz,
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.notifier.success('Exchange registrado');
          this.dialogRef.close(true);
        },
        error: () => this.loading.set(false),
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }

  /** Código de moneda según la billetera seleccionada en la tab transacción. */
  txCurrency(): string | null {
    const id = this.txForm.value.walletId;
    return this.data.wallets.find((w) => w.id === id)?.currency ?? null;
  }

  exFromCurrency(): string | null {
    const id = this.exForm.value.fromWalletId;
    return this.data.wallets.find((w) => w.id === id)?.currency ?? null;
  }

  exToCurrency(): string | null {
    const id = this.exForm.value.toWalletId;
    return this.data.wallets.find((w) => w.id === id)?.currency ?? null;
  }
}

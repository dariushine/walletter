import { Component, inject, input, OnInit, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatRadioModule } from '@angular/material/radio';
import { Router } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { TransactionDetail as TransactionDetailModel } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { todayInTimeZone } from '../../core/utils/dates';

@Component({
  selector: 'app-transaction-detail',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatMenuModule,
    MatDividerModule,
    MatRadioModule,
  ],
  templateUrl: './transaction-detail.html',
  styleUrls: ['./transaction-detail.scss'],
})
export class TransactionDetail implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly notifier = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  readonly id = input.required<number>();
  readonly timezone = this.settings.timezone;

  // Observable del id: creado en un inicializador de campo (contexto de
  // inyección) para poder recargar al navegar entre transacciones sin
  // depender de que ngOnInit vuelva a correr.
  private readonly id$ = toObservable(this.id);

  tx = signal<TransactionDetailModel | null>(null);
  loading = signal(true);
  editing = signal(false);
  tz = 'America/Caracas';
  busy = signal(false);

  readonly editForm = this.fb.group({
    description: [''],
    categoryName: [''],
    amount: [0],
    date: [''],
    time: [''],
  });

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.tz = this.settings.timezone();
    // Recarga cuando cambia el id (al navegar entre transacciones el
    // componente de ruta se reutiliza y ngOnInit no vuelve a correr).
    this.id$.subscribe(() => {
      this.loading.set(true);
      this.load();
    });
  }

  load(): void {
    this.api.transaction(this.id()).subscribe({
      next: (t) => {
        // Defensivo: si el backend desplegado aún no devuelve los campos nuevos,
        // proveer defaults para no romper el render (associated debe ser array).
        this.tx.set({
          ...t,
          resultingBalance: t.resultingBalance ?? 0,
          isExchange: t.isExchange ?? this.inferExchange(t),
          associated: t.associated ?? [],
        });
        this.editForm.patchValue({
          description: t.description || '',
          categoryName: t.category,
          amount: t.amount,
          date: t.date,
          time: t.time,
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  saveEdit(): void {
    this.busy.set(true);
    const v = this.editForm.value;
    this.api
      .updateTransaction(this.id(), {
        description: v.description || undefined,
        categoryName: v.categoryName || undefined,
        amount: Number(v.amount) || undefined,
        date: v.date || undefined,
        time: v.time || undefined,
        tz: this.tz,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.editing.set(false);
          this.notifier.success('Transacción actualizada');
          this.load();
        },
        error: () => this.busy.set(false),
      });
  }

  /** Si es una transacción de exchange, no se puede editar/eliminar/comisionar/asociar. */
  isExchange(): boolean {
    const t = this.tx();
    return !!t && !!t.isExchange;
  }

  /**
   * Inferencia local de si una transacción es de exchange, usada como fallback
   * cuando el backend desplegado todavía no devuelve el campo isExchange.
   */
  private inferExchange(t: TransactionDetailModel): boolean {
    const cat = (t.category || '').toLowerCase();
    if (cat === 'exchange_out' || cat === 'exchange_in') return true;
    // Comisión cuyo padre es débito/crédito de exchange.
    if (cat === 'fee' && t.parentTransactionId) {
      const p = t.associated?.length ? t.associated.find((a) => a.id === t.parentTransactionId) : undefined;
      const pcat = (p?.category || '').toLowerCase();
      if (pcat === 'exchange_out' || pcat === 'exchange_in') return true;
    }
    return false;
  }

  /** Si es una comisión (fee). */
  isFee(): boolean {
    const t = this.tx();
    return !!t && (t.category || '').toLowerCase() === 'fee';
  }

  /**
   * Menú > Opciones bloqueadas: muestra el mensaje correspondiente.
   * - Exchange: todo bloqueado → 'hazlo desde el exchange'.
   * - Fee: añadir comisión / asociada bloqueado.
   */
  blockedAction(action: 'edit' | 'delete' | 'fee' | 'associate'): void {
    if (this.isExchange()) {
      this.notifier.info('Esta transacción pertenece a un exchange. Edítala o elimínala desde el exchange.');
      return;
    }
    if (this.isFee() && (action === 'fee' || action === 'associate')) {
      this.notifier.info('No se pueden añadir comisiones o transacciones asociadas a una comisión.');
      return;
    }
    // Fallback: si es fee y se intenta editar/eliminar, se permite (el fee edit/delete sí se puede).
  }

  edit(): void {
    if (this.isExchange()) {
      this.blockedAction('edit');
      return;
    }
    this.editing.set(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  delete(): void {
    if (this.isExchange()) {
      this.blockedAction('delete');
      return;
    }
    if (!confirm('¿Eliminar esta transacción?')) return;
    this.api.deleteTransaction(this.id()).subscribe({
      next: () => {
        this.notifier.success('Transacción eliminada');
        this.router.navigate(['/transactions']);
      },
      error: () => undefined,
    });
  }

  addFee(): void {
    if (this.isExchange() || this.isFee()) {
      this.blockedAction('fee');
      return;
    }
    const t = this.tx();
    if (!t) return;
    const ref = this.dialog.open(AddFeeDialog, { width: '440px', data: { tz: this.tz } });
    ref.afterClosed().subscribe((res?: { amount: number; date: string; time: string }) => {
      if (!res) return;
      this.api.addTransactionFee(this.id(), { amount: res.amount, date: res.date, time: res.time, tz: this.tz }).subscribe({
        next: () => {
          this.notifier.success('Comisión añadida');
          this.load();
        },
        error: () => this.notifier.error('No se pudo añadir la comisión'),
      });
    });
  }

  addAssociated(): void {
    if (this.isExchange() || this.isFee()) {
      this.blockedAction('associate');
      return;
    }
    const t = this.tx();
    if (!t) return;
    const ref = this.dialog.open(AddAssociateDialog, {
      width: '440px',
      data: { tz: this.tz, walletCurrency: t.walletCurrency },
    });
    ref.afterClosed().subscribe((res?: { amount: number; type: 'income' | 'expense'; categoryName: string; description?: string; date: string; time: string }) => {
      if (!res) return;
      this.api
        .associateTransaction(this.id(), {
          amount: res.amount,
          type: res.type,
          categoryName: res.categoryName,
          description: res.description || undefined,
          date: res.date,
          time: res.time,
          tz: this.tz,
        })
        .subscribe({
          next: () => {
            this.notifier.success('Transacción asociada añadida');
            this.load();
          },
          error: () => this.notifier.error('No se pudo añadir la transacción asociada'),
        });
    });
  }

  goToTransaction(id: number): void {
    if (this.id() === id) return;
    this.router.navigate(['/transactions', id]);
  }

  goToExchange(id: number): void {
    this.router.navigate(['/exchanges', id]);
  }

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }

  /** Formatea YYYY-MM-DD a 'jueves, 20 de agosto de 2026'. */
  fmtFullDate(dateStr: string): string {
    if (!dateStr) return '—';
    const [y, m, d] = dateStr.split('-').map(Number);
    if (!y || !m || !d) return dateStr;
    return new Date(y, m - 1, d).toLocaleDateString('es-VE', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  }

  goBack(): void {
    this.router.navigate(['/transactions']);
  }
}

/** Diálogo para añadir una comisión (fee) a la transacción. */
@Component({
  selector: 'app-add-fee-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Añadir comisión</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="assoc-form">
        <mat-form-field appearance="outline" class="full">
          <mat-label>Monto de la comisión *</mat-label>
          <input matInput formControlName="amount" type="number" min="0" step="0.01" />
        </mat-form-field>
        <div class="row-two">
          <mat-form-field appearance="outline">
            <mat-label>Fecha</mat-label>
            <input matInput formControlName="date" type="date" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Hora</mat-label>
            <input matInput formControlName="time" type="time" />
          </mat-form-field>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="cancel()">Cancelar</button>
      <button mat-raised-button color="primary" type="button" (click)="save()" [disabled]="form.invalid || loading()">
        {{ loading() ? 'Guardando…' : 'Añadir' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .assoc-form { display: flex; flex-direction: column; padding-top: 8px; gap: 4px; }
    .full { width: 100%; }
    .row-two { display: flex; gap: 12px; }
    .row-two mat-form-field { flex: 1; }
  `],
})
export class AddFeeDialog {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AddFeeDialog>);
  readonly data = inject<{ tz: string }>(MAT_DIALOG_DATA);

  loading = signal(false);
  today = todayInTimeZone(this.data.tz);

  readonly form = this.fb.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    date: [this.today, Validators.required],
    time: ['12:00', Validators.required],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const v = this.form.value;
    this.dialogRef.close({ amount: Number(v.amount) || 0, date: v.date!, time: v.time! });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

/** Diálogo para añadir una transacción asociada. */
@Component({
  selector: 'app-add-associate-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogTitle,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatButtonModule,
  ],
  template: `
    <h2 mat-dialog-title>Añadir transacción asociada</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="assoc-form">
        <mat-radio-group formControlName="type" class="type-radio">
          <mat-radio-button value="expense" color="warn">Gasto</mat-radio-button>
          <mat-radio-button value="income" color="primary">Ingreso</mat-radio-button>
        </mat-radio-group>
        <div class="row-two">
          <mat-form-field appearance="outline">
            <mat-label>Categoría *</mat-label>
            <input matInput formControlName="categoryName" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Monto *</mat-label>
            <input matInput formControlName="amount" type="number" min="0" step="0.01" />
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full">
          <mat-label>Descripción</mat-label>
          <input matInput formControlName="description" />
        </mat-form-field>
        <div class="row-two">
          <mat-form-field appearance="outline">
            <mat-label>Fecha</mat-label>
            <input matInput formControlName="date" type="date" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Hora</mat-label>
            <input matInput formControlName="time" type="time" />
          </mat-form-field>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="cancel()">Cancelar</button>
      <button mat-raised-button color="primary" type="button" (click)="save()" [disabled]="form.invalid || loading()">
        {{ loading() ? 'Guardando…' : 'Añadir' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .assoc-form { display: flex; flex-direction: column; padding-top: 8px; gap: 4px; }
    .row-two { display: flex; gap: 12px; }
    .row-two mat-form-field { flex: 1; }
    .full { width: 100%; }
    .type-radio { display: flex; gap: 16px; margin-bottom: 12px; }
  `],
})
export class AddAssociateDialog {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AddAssociateDialog>);
  readonly data = inject<{ tz: string; walletCurrency?: string }>(MAT_DIALOG_DATA);

  loading = signal(false);
  today = todayInTimeZone(this.data.tz);

  readonly form = this.fb.group({
    type: ['expense', Validators.required],
    categoryName: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    description: [''],
    date: [this.today, Validators.required],
    time: ['12:00', Validators.required],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const v = this.form.value;
    this.dialogRef.close({
      amount: Number(v.amount) || 0,
      type: (v.type as 'income' | 'expense')!,
      categoryName: v.categoryName!,
      description: v.description || undefined,
      date: v.date!,
      time: v.time!,
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

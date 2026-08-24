import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogContent, MatDialogActions, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { TransactionDetail } from '../../models/walletter.models';
import { CategoryAutocomplete } from '../../core/components/category-autocomplete';
import { MoneyInput } from '../../core/components/money-input';

export interface EditTransactionDialogData {
  tx: TransactionDetail;
  tz: string;
}

@Component({
  selector: 'app-edit-transaction-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    CategoryAutocomplete,
    MoneyInput,
  ],
  template: `
    <div class="dlg">
      <div class="dlg-head">
        <h2 class="dlg-title">Editar transacción</h2>
        <button mat-icon-button class="dlg-close" (click)="cancel()" aria-label="Cerrar">
          <mat-icon>close</mat-icon>
        </button>
      </div>
      <mat-dialog-content class="dlg-content">
        <form [formGroup]="form" class="dlg-form">
          <app-category-autocomplete
            formControlName="categoryName"
            [type]="data.tx.type"
          />
          <app-money-input
            formControlName="amount"
            label="Monto"
            [currency]="data.tx.walletCurrency"
            [full]="true"
          />
          <mat-form-field appearance="outline" class="dlg-full">
            <mat-label>Descripción</mat-label>
            <input matInput formControlName="description" />
          </mat-form-field>
          <div class="dlg-row">
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
      <mat-dialog-actions align="end" class="dlg-action">
        <button mat-button (click)="cancel()">Cancelar</button>
        <button mat-flat-button color="primary" (click)="save()" [disabled]="form.invalid || loading()">
          {{ loading() ? 'Guardando…' : 'Guardar' }}
        </button>
      </mat-dialog-actions>
    </div>
  `,
})
export class EditTransactionDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<EditTransactionDialog>);
  readonly data = inject<EditTransactionDialogData>(MAT_DIALOG_DATA);

  loading = signal(false);

  readonly form = this.fb.group({
    description: [this.data.tx.description ?? ''],
    categoryName: [this.data.tx.category ?? '', Validators.required],
    amount: [this.data.tx.amount, [Validators.required, Validators.min(0.01)]],
    date: [this.data.tx.date, Validators.required],
    time: [this.data.tx.time, Validators.required],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const v = this.form.value;
    this.api
      .updateTransaction(this.data.tx.id, {
        description: v.description || undefined,
        categoryName: v.categoryName || undefined,
        amount: Number(v.amount) || undefined,
        date: v.date || undefined,
        time: v.time || undefined,
        tz: this.data.tz,
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.notifier.success('Transacción actualizada');
          this.dialogRef.close(true);
        },
        error: () => this.loading.set(false),
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

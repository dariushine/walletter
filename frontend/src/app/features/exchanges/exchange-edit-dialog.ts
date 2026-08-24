import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogContent, MatDialogActions, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ExchangeDetail } from '../../models/walletter.models';
import { MoneyInput } from '../../core/components/money-input';

export interface EditExchangeDialogData {
  ex: ExchangeDetail;
}

@Component({
  selector: 'app-edit-exchange-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MoneyInput,
  ],
  template: `
    <div class="dlg">
      <div class="dlg-head">
        <h2 class="dlg-title">Editar exchange</h2>
        <button mat-icon-button class="dlg-close" (click)="cancel()" aria-label="Cerrar">
          <mat-icon>close</mat-icon>
        </button>
      </div>
      <mat-dialog-content class="dlg-content">
        <form [formGroup]="form" class="dlg-form">
          <div class="dlg-row read-only">
            <mat-form-field appearance="outline">
              <mat-label>Billetera origen</mat-label>
              <input matInput [value]="data.ex.fromWalletName" disabled />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Billetera destino</mat-label>
              <input matInput [value]="data.ex.toWalletName" disabled />
            </mat-form-field>
          </div>
          <div class="dlg-row">
            <app-money-input
              formControlName="fromAmount"
              label="Monto enviado *"
              [currency]="data.ex.fromCurrency"
            />
            <app-money-input
              formControlName="toAmount"
              label="Monto recibido *"
              [currency]="data.ex.toCurrency"
            />
          </div>
          <mat-form-field appearance="outline" class="dlg-full">
            <mat-label>Descripción</mat-label>
            <input matInput formControlName="description" />
          </mat-form-field>
          <details class="dlg-details">
            <summary>Detalles</summary>
            <div class="dlg-details-body">
              <div class="dlg-row">
                <app-money-input
                  formControlName="fee"
                  label="Comisión débito"
                  [currency]="data.ex.fromCurrency"
                />
                <app-money-input
                  formControlName="creditFee"
                  label="Comisión crédito"
                  [currency]="data.ex.toCurrency"
                />
              </div>
            </div>
          </details>
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
export class EditExchangeDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<EditExchangeDialog>);
  readonly data = inject<EditExchangeDialogData>(MAT_DIALOG_DATA);

  loading = signal(false);

  readonly form = this.fb.group({
    fromAmount: [this.data.ex.fromAmount, [Validators.required, Validators.min(0.01)]],
    toAmount: [this.data.ex.toAmount, [Validators.required, Validators.min(0.01)]],
    fee: [this.data.ex.fee],
    creditFee: [this.data.ex.creditFee],
    description: [this.data.ex.description ?? ''],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const v = this.form.value;
    this.api
      .updateExchange(this.data.ex.id, {
        fromAmount: Number(v.fromAmount) || undefined,
        toAmount: Number(v.toAmount) || undefined,
        fee: Number(v.fee) || undefined,
        creditFee: Number(v.creditFee) || undefined,
        description: v.description || undefined,
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.notifier.success('Exchange actualizado');
          this.dialogRef.close(true);
        },
        error: () => this.loading.set(false),
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

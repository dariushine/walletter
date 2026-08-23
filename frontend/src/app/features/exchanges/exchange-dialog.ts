import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Wallet } from '../../models/walletter.models';
import { todayInTimeZone } from '../../core/utils/dates';

export interface ExchangeDialogData {
  wallets: Wallet[];
  tz?: string;
}

@Component({
  selector: 'app-exchange-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  templateUrl: './exchange-dialog.html',
  styleUrls: ['./exchange-dialog.scss'],
})
export class ExchangeDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<ExchangeDialog>);
  readonly data = inject<ExchangeDialogData>(MAT_DIALOG_DATA);

  loading = signal(false);
  today = todayInTimeZone(this.data.tz || 'America/Caracas');

  readonly form = this.fb.group({
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

  save(): void {
    if (this.form.invalid) return;
    if (this.form.value.fromWalletId === this.form.value.toWalletId) {
      this.notifier.error('Las billeteras origen y destino deben ser diferentes');
      return;
    }
    this.loading.set(true);
    const v = this.form.value;
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
        tz: this.data.tz || 'America/Caracas',
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
}

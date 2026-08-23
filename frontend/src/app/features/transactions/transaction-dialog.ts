import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatRadioModule } from '@angular/material/radio';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Wallet } from '../../models/walletter.models';
import { todayInTimeZone } from '../../core/utils/dates';

export interface TransactionDialogData {
  wallets: Wallet[];
  tz: string;
}

@Component({
  selector: 'app-transaction-dialog',
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule,
    MatDialogTitle,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatRadioModule,
    MatDatepickerModule,
  ],
  templateUrl: './transaction-dialog.html',
  styleUrls: ['./transaction-dialog.scss'],
})
export class TransactionDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<TransactionDialog>);
  readonly data = inject<TransactionDialogData>(MAT_DIALOG_DATA);

  loading = signal(false);
  today = todayInTimeZone(this.data.tz);

  readonly form = this.fb.group({
    walletId: [this.data.wallets[0]?.id ?? null, Validators.required],
    type: ['expense', Validators.required],
    categoryName: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    description: [''],
    fee: [0],
    date: [this.today, Validators.required],
    time: ['12:00', Validators.required],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const v = this.form.value;
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

  cancel(): void {
    this.dialogRef.close();
  }
}

import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatButtonModule } from '@angular/material/button';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Wallet } from '../../models/walletter.models';

export interface RecurringDialogData {
  wallets: Wallet[];
}

const CURRENCIES = ['USD', 'VES'];

@Component({
  selector: 'app-recurring-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatSelectModule, MatRadioModule, MatButtonModule],
  templateUrl: './recurring-dialog.html',
  styleUrls: ['./recurring-dialog.scss'],
})
export class RecurringDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<RecurringDialog>);
  readonly data = inject<RecurringDialogData>(MAT_DIALOG_DATA);

  readonly currencies = CURRENCIES;
  loading = false;

  readonly form = this.fb.group({
    name: ['', Validators.required],
    type: ['expense', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    fee: [0],
    currency: ['USD', Validators.required],
    categoryName: ['', Validators.required],
    walletId: [null as number | null],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const v = this.form.value;
    this.api
      .createRecurringPayment({
        name: v.name!,
        type: v.type as 'income' | 'expense',
        amount: Number(v.amount) || 0,
        fee: Number(v.fee) || 0,
        currency: v.currency!,
        categoryName: v.categoryName!,
        walletId: v.walletId ?? undefined,
      } as any)
      .subscribe({
        next: () => {
          this.loading = false;
          this.notifier.success('Pago recurrente creado');
          this.dialogRef.close(true);
        },
        error: () => (this.loading = false),
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

export interface RecurringExecuteData {
  item: { id: number; name: string };
  wallets: Wallet[];
}

@Component({
  selector: 'app-recurring-execute-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  templateUrl: './recurring-execute-dialog.html',
  styleUrls: ['./recurring-dialog.scss'],
})
export class RecurringExecuteDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<RecurringExecuteDialog>);
  readonly data = inject<RecurringExecuteData>(MAT_DIALOG_DATA);

  loading = false;
  today = new Date().toISOString().slice(0, 10);

  readonly form = this.fb.group({
    walletId: [this.data.wallets[0]?.id ?? null],
    date: [this.today],
    time: ['12:00'],
    description: [this.data.item.name],
  });

  save(): void {
    this.loading = true;
    const v = this.form.value;
    this.api
      .executeRecurringPayment(this.data.item.id, {
        walletId: v.walletId ?? undefined,
        overrideWalletId: v.walletId ?? undefined,
        date: v.date || this.today,
        time: v.time || '12:00',
        description: v.description || undefined,
      })
      .subscribe({
        next: () => {
          this.loading = false;
          this.notifier.success('Pago ejecutado');
          this.dialogRef.close(true);
        },
        error: () => (this.loading = false),
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

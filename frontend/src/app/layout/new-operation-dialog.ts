import { Component, inject, signal } from '@angular/core';
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
import { WalletterApiService } from '../core/services/walletter-api.service';
import { NotificationService } from '../core/services/notification.service';
import { Wallet } from '../models/walletter.models';
import { todayInTimeZone } from '../core/utils/dates';

export interface NewOperationDialogData {
  wallets: Wallet[];
  tz: string;
}

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
  ],
  templateUrl: './new-operation-dialog.html',
  styleUrls: ['./new-operation-dialog.scss'],
})
export class NewOperationDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<NewOperationDialog>);
  readonly data = inject<NewOperationDialogData>(MAT_DIALOG_DATA);

  loading = signal(false);
  today = todayInTimeZone(this.data.tz || 'America/Caracas');
  /** Tab activa: 0 = Transacción, 1 = Exchange. */
  readonly activeTab = signal(0);

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
}

import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Wallet } from '../../models/walletter.models';

const CURRENCIES = ['USD', 'VES'];
const TYPES = [
  { value: 'cash', label: 'Efectivo' },
  { value: 'bank', label: 'Banco' },
  { value: 'card', label: 'Tarjeta' },
  { value: 'crypto', label: 'Cripto' },
  { value: 'other', label: 'Otro' },
];
const COLORS = ['#3f51b5', '#2e7d32', '#c62828', '#e65100', '#6a1b9a', '#00838f', '#455a64'];
const ICONS = [
  { value: 'account_balance_wallet', label: 'Billetera' },
  { value: 'account_balance', label: 'Banco / edificio' },
  { value: 'payments', label: 'Efectivo' },
  { value: 'savings', label: 'Ahorros' },
  { value: 'credit_card', label: 'Tarjeta' },
  { value: 'currency_bitcoin', label: 'Bitcoin' },
  { value: 'currency_exchange', label: 'Cambio' },
  { value: 'attach_money', label: 'Dinero ($)' },
  { value: 'wallet', label: 'Monedero' },
  { value: 'savings_outlined', label: 'Alcancía' },
  { value: 'storefront', label: 'Tienda' },
];

@Component({
  selector: 'app-wallet-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogTitle,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './wallet-dialog.html',
  styleUrls: ['./wallet-dialog.scss'],
})
export class WalletDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<WalletDialog>);
  readonly data = inject<Partial<Wallet>>(MAT_DIALOG_DATA);

  readonly currencies = CURRENCIES;
  readonly types = TYPES;
  readonly colors = COLORS;
  readonly icons = ICONS;
  loading = false;
  isEdit = !!this.data?.id;

  readonly form = this.fb.group({
    name: [this.data?.name ?? '', Validators.required],
    alias: [this.data?.alias ?? ''],
    type: [this.data?.type ?? 'cash', Validators.required],
    currency: [this.data?.currency ?? 'USD', Validators.required],
    color: [this.data?.color ?? '#3f51b5'],
    icon: [this.data?.icon ?? ''],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const value = this.form.value;
    const payload = {
      name: value.name as string,
      alias: (value.alias as string) || undefined,
      type: value.type as string,
      currency: value.currency as string,
      balance: 0,
      color: (value.color as string) || undefined,
      icon: (value.icon as string) || undefined,
    };

    const request = this.isEdit
      ? this.api.updateWallet(this.data!.id!, { name: payload.name, alias: payload.alias, color: payload.color, icon: payload.icon })
      : this.api.createWallet(payload);

    request.subscribe({
      next: (w) => {
        this.loading = false;
        this.notifier.success(this.isEdit ? 'Billetera actualizada' : 'Billetera creada');
        this.dialogRef.close(w);
      },
      error: () => (this.loading = false),
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

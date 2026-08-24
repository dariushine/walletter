import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Wallet, RecurringPayment } from '../../models/walletter.models';
import { CategoryAutocomplete } from '../../core/components/category-autocomplete';
import { MoneyInput } from '../../core/components/money-input';

export interface RecurringDialogData {
  wallets: Wallet[];
  item?: RecurringPayment; // si viene, se edita
}

const CURRENCIES = ['USD', 'VES'];

@Component({
  selector: 'app-recurring-dialog',
  imports: [ReactiveFormsModule, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatSelectModule, MatRadioModule, MatButtonModule, MatIconModule, CategoryAutocomplete, MoneyInput],
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
  isEdit = !!this.data?.item;

  readonly form = this.fb.group({
    name: [this.data?.item?.name ?? '', Validators.required],
    type: [(this.data?.item?.type ?? 'expense') as 'expense' | 'income', Validators.required],
    amount: [this.data?.item?.amount ?? 0, [Validators.required, Validators.min(0.01)]],
    fee: [this.data?.item?.fee ?? 0],
    currency: [this.data?.item?.currency ?? 'USD', Validators.required],
    categoryName: [this.data?.item?.category ?? '', Validators.required],
    walletId: [this.data?.item?.walletId ?? null],
    description: [this.data?.item?.description ?? ''],
  });

  /** Cambia el tipo: limpia la categoría y recarga sugerencias (autocomplete recarga por [type]). */
  changeType(type: 'income' | 'expense'): void {
    if ((this.form.value.type as string) === type) return;
    this.form.patchValue({ type, categoryName: '' });
  }

  save(): void {
    if (this.form.invalid) return;
    const lower = (this.form.value.categoryName ?? '').toLowerCase().trim();
    if (['fee', 'exchange_in', 'exchange_out'].includes(lower)) {
      this.notifier.error('Esa categoría es de sistema y no puede usarse');
      return;
    }
    this.loading = true;
    const v = this.form.value;
    const payload = {
      name: v.name!,
      type: v.type as 'income' | 'expense',
      amount: Number(v.amount) || 0,
      fee: Number(v.fee) || 0,
      currency: v.currency!,
      categoryName: v.categoryName!,
      walletId: v.walletId ?? undefined,
      description: v.description || undefined,
    };

    const request = this.isEdit
      ? this.api.updateRecurringPayment(this.data!.item!.id, payload as any)
      : this.api.createRecurringPayment(payload as any);

    request.subscribe({
      next: () => {
        this.loading = false;
        this.notifier.success(this.isEdit ? 'Pago recurrente actualizado' : 'Pago recurrente creado');
        this.dialogRef.close(true);
      },
      error: () => (this.loading = false),
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }

  /** Código de moneda seleccionado en el form (o null → 'USD/VES'). */
  currencyValue(): string | null {
    const c = this.form.value.currency;
    return typeof c === 'string' && c ? c : null;
  }
}

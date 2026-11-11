import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Wallet, PendingPayment } from '../../models/walletter.models';
import { CategoryAutocomplete } from '../../core/components/category-autocomplete';
import { MoneyInput } from '../../core/components/money-input';
import { formatInTimeZone } from '../../core/utils/dates';

export interface PendingDialogData {
  wallets: Wallet[];
  tz: string;
  item?: PendingPayment; // si viene, se edita
}

const CURRENCIES = ['USD', 'VES'];

@Component({
  selector: 'app-pending-dialog',
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule,
    MatDialogContent,
    MatDialogActions,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatDatepickerModule,
    CategoryAutocomplete,
    MoneyInput,
  ],
  templateUrl: './pending-dialog.html',
  styleUrls: ['../../layout/new-operation-dialog.scss'],
})
export class PendingDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<PendingDialog>);
  readonly data = inject<PendingDialogData>(MAT_DIALOG_DATA);

  readonly currencies = CURRENCIES;
  loading = false;
  isEdit = !!this.data?.item;

  readonly form = this.fb.group({
    // NOTA: 'currency' puede quedar deshabilitado cuando hay billetera (su
    // moneda manda). Con el control deshabilitado, form.value.currency es
    // undefined → por eso save() y currencyValue() usan getRawValue().

    name: [this.data?.item?.name ?? '', Validators.required],
    type: [(this.data?.item?.type ?? 'expense') as 'expense' | 'income', Validators.required],
    amount: [this.data?.item?.amount ?? 0, [Validators.required, Validators.min(0.01)]],
    fee: [this.data?.item?.fee ?? 0],
    currency: [this.data?.item?.currency ?? 'USD', Validators.required],
    categoryName: [this.data?.item?.category ?? '', Validators.required],
    walletId: [this.data?.item?.walletId ?? null],
    dueDate: [this.data?.item?.dueDate ?? ''],
    description: [this.data?.item?.description ?? ''],
  });

  constructor() {
    // Estado inicial + reacción a cambios de billetera.
    this.syncCurrencyWithWallet((this.form.value.walletId as number | null) ?? null);
    this.form
      .get('walletId')!
      .valueChanges.subscribe((id) => this.syncCurrencyWithWallet((id as number | null) ?? null));
  }

  /**
   * Si hay billetera, la moneda del pendiente se deriva de ella y queda fija
   * (deshabilitada). Sin billetera, el usuario la elige a mano.
   */
  private syncCurrencyWithWallet(walletId: number | null): void {
    const currencyCtrl = this.form.get('currency')!;
    const wallet = walletId != null ? this.data.wallets.find((w) => w.id === walletId) : undefined;
    if (wallet) {
      currencyCtrl.setValue(wallet.currency);
      currencyCtrl.disable({ emitEvent: false });
    } else {
      currencyCtrl.enable({ emitEvent: false });
    }
  }

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
    // getRawValue() incluye controles deshabilitados (currency con billetera).
    const v = this.form.getRawValue();
    const dueDate = normalizeDueDate(v.dueDate, this.data.tz);
    const payload = {
      name: v.name!,
      type: v.type as 'income' | 'expense',
      amount: Number(v.amount) || 0,
      fee: Number(v.fee) || 0,
      currency: v.currency!,
      categoryName: v.categoryName!,
      walletId: v.walletId ?? undefined,
      dueDate: dueDate ?? undefined,
      description: v.description || undefined,
    };

    const request = this.isEdit
      ? this.api.updatePendingPayment(this.data!.item!.id, payload as any)
      : this.api.createPendingPayment(payload as any);

    request.subscribe({
      next: () => {
        this.loading = false;
        this.notifier.success(this.isEdit ? 'Pago pendiente actualizado' : 'Pago pendiente creado');
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
    const c = this.form.getRawValue().currency;
    return typeof c === 'string' && c ? c : null;
  }
}

/** Convierte el valor del datepicker a yyyy-MM-dd en la zona indicada (o null si vacío). */
function normalizeDueDate(value: string | Date | null | undefined, tz: string): string | null {
  if (!value) return null;
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return null;
  return formatInTimeZone(d, tz, 'yyyy-MM-dd');
}

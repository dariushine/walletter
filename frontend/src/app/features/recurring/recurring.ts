import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { RecurringPayment, Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { todayInTimeZone, formatInTimeZone } from '../../core/utils/dates';
import { RecurringDialog } from './recurring-dialog';
import { TransactionDialog } from '../transactions/transaction-dialog';
import { SettingsStore } from '../../core/services/settings-store';

@Component({
  selector: 'app-recurring',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './recurring.html',
  styleUrls: ['./recurring.scss'],
})
export class Recurring implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(NotificationService);
  private readonly settings = inject(SettingsStore);

  items = signal<RecurringPayment[]>([]);
  wallets = signal<Wallet[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.api.wallets().subscribe((w) => this.wallets.set(w));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.recurringPayments().subscribe({
      next: (r) => {
        this.items.set(r);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    const ref = this.dialog.open(RecurringDialog, { width: '440px', data: { wallets: this.wallets() } });
    ref.afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  edit(item: RecurringPayment): void {
    const ref = this.dialog.open(RecurringDialog, { width: '440px', data: { wallets: this.wallets(), item } });
    ref.afterClosed().subscribe((updated) => {
      if (updated) this.load();
    });
  }

  /**
   * Ejecutar un pago recurrente: abre el formulario de transacción NORMAL
   * prellenado desde el pago recurrente, pero editable (billetera, monto,
   * comisión, fecha/hora, descripción). El tipo queda fijado (gasto/ingreso).
   */
  execute(item: RecurringPayment): void {
    this.settings.loadTimezone();
    const tz = this.settings.timezone();
    const wallets = this.wallets();
    const preset = {
      type: item.type,
      walletId: item.walletId ?? null,
      categoryName: item.category ?? '',
      amount: item.amount,
      fee: item.fee ?? 0,
      description: item.description ?? item.name,
      date: todayInTimeZone(tz),
      time: formatInTimeZone(new Date(), tz, 'HH:mm'),
      title: `Registrar ${item.type === 'income' ? 'ingreso' : 'gasto'} recurrente`,
    };
    const ref = this.dialog.open(TransactionDialog, {
      width: '460px',
      data: { wallets, tz, preset },
    });
    ref.afterClosed().subscribe((ok) => {
      if (ok) this.notifier.success('Transacción registrada');
    });
  }

  delete(item: RecurringPayment): void {
    if (!confirm(`¿Eliminar "${item.name}"?`)) return;
    this.api.deleteRecurringPayment(item.id).subscribe({
      next: () => {
        this.notifier.success('Pago recurrente eliminado');
        this.load();
      },
      error: () => undefined,
    });
  }

  format(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  /**
   * Estado de una suscripción: días hasta el próximo cobro o vencimiento.
   * Basado en el día de cobro (billingDay), la última ejecución real
   * (lastExecutedAt, que se llena solo cuando se crea la transacción)
   * y la fecha de hoy en la zona del usuario.
   */
  subscriptionStatus(item: RecurringPayment): { label: string; days: number; overdue: boolean } | null {
    if (!item.isSubscription || !item.billingDay) return null;
    const tz = this.settings.timezone();
    const hoy = todayInTimeZone(tz);
    const [y, m, dHoy] = hoy.split('-').map(Number);
    const day = item.billingDay;

    // ¿Se ejecutó alguna vez? Y ¿cuándo fue la última ejecución (en la zona del usuario)?
    const lastStr = item.lastExecutedAt ? formatInTimeZone(item.lastExecutedAt, tz, 'yyyy-MM-dd') : null;
    const lastMonth = lastStr ? Number(lastStr.split('-')[1]) : null;
    const lastYear = lastStr ? Number(lastStr.split('-')[0]) : null;
    const ejecutadoEsteMes = lastStr != null && lastYear === y && lastMonth === m;

    // Suscripción recién creada (nunca ejecutada): aún no puede estar vencida.
    // lastExecutedAt se llena solo al ejecutar el pago; si es null, es nueva.
    const esNueva = lastStr == null;

    // Día de cobro de este mes (recortado a los días reales del mes).
    const daysInMonth = new Date(y, m, 0).getDate();
    const cobroEsteMes = Math.min(day, daysInMonth);

    // ¿Ya pasó el día de cobro de este mes sin haberse ejecutado?
    // Solo aplica a suscripciones con historial: una recién creada no puede
    // estar vencida el mismo día (su primer cobro es el próximo billingDay).
    if (!esNueva && dHoy > cobroEsteMes && !ejecutadoEsteMes) {
      const daysOverdue = dHoy - cobroEsteMes;
      return { label: `Vencido hace ${daysOverdue} día${daysOverdue === 1 ? '' : 's'}`, days: daysOverdue, overdue: true };
    }

    // Es hoy y aún no se ejecutó hoy (solo aplica a suscripciones con historial;
    // una recién creada cuyo billingDay cae hoy cobra en el próximo ciclo).
    if (!esNueva && dHoy === cobroEsteMes && !ejecutadoEsteMes) {
      return { label: 'Vence hoy', days: 0, overdue: false };
    }

    // Al día: siguiente cobro (este mes si no ha pasado, si no el que viene).
    let next: Date;
    if (cobroEsteMes >= dHoy) {
      next = new Date(y, m - 1, cobroEsteMes);
    } else {
      const nextMonth = new Date(y, m, 1);
      const nextDays = new Date(y, m + 1, 0).getDate();
      next = new Date(y, m, Math.min(day, nextDays));
    }
    const diff = Math.round((next.getTime() - new Date(y, m - 1, dHoy).getTime()) / 86400000);
    if (diff === 0) return { label: 'Vence hoy', days: 0, overdue: false };
    return { label: `Faltan ${diff} día${diff === 1 ? '' : 's'}`, days: diff, overdue: false };
  }
}

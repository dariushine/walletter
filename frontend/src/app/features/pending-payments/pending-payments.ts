import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { FormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { PendingPayment, Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { todayInTimeZone, formatInTimeZone } from '../../core/utils/dates';
import { PendingDialog } from './pending-dialog';
import { TransactionDialog } from '../transactions/transaction-dialog';
import { SettingsStore } from '../../core/services/settings-store';

const PENDING_PANEL = 'pending-dialog-panel';

@Component({
  selector: 'app-pending-payments',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatSlideToggleModule, FormsModule],
  templateUrl: './pending-payments.html',
  styleUrls: ['./pending-payments.scss'],
})
export class PendingPayments implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(NotificationService);
  private readonly settings = inject(SettingsStore);

  items = signal<PendingPayment[]>([]);
  wallets = signal<Wallet[]>([]);
  loading = signal(true);
  includePaid = signal(false);

  ngOnInit(): void {
    this.api.wallets().subscribe((w) => this.wallets.set(w));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.pendingPayments(this.includePaid()).subscribe({
      next: (r) => {
        this.items.set(r);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  toggleIncludePaid(): void {
    this.includePaid.set(!this.includePaid());
    this.load();
  }

  openCreate(): void {
    const ref = this.dialog.open(PendingDialog, {
      width: '440px',
      panelClass: PENDING_PANEL,
      data: { wallets: this.wallets() },
    });
    ref.afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  edit(item: PendingPayment): void {
    if (item.isPaid) return;
    const ref = this.dialog.open(PendingDialog, {
      width: '440px',
      panelClass: PENDING_PANEL,
      data: { wallets: this.wallets(), item },
    });
    ref.afterClosed().subscribe((updated) => {
      if (updated) this.load();
    });
  }

  /**
   * Pagar un pendiente: abre el formulario de transacción NORMAL prellenado
   * desde el pendiente, pero editable (billetera, monto, comisión, fecha/hora,
   * descripción). El tipo queda fijado (gasto/ingreso).
   */
  pay(item: PendingPayment): void {
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
      title: `Registrar pago de «${item.name}»`,
      pendingId: item.id,
    };
    const ref = this.dialog.open(TransactionDialog, {
      width: '460px',
      data: { wallets, tz, preset },
    });
    ref.afterClosed().subscribe((ok) => {
      if (ok) {
        this.notifier.success('Pago registrado');
        this.load();
      }
    });
  }

  delete(item: PendingPayment): void {
    if (item.isPaid) return;
    if (!confirm(`¿Eliminar "${item.name}"?`)) return;
    this.api.deletePendingPayment(item.id).subscribe({
      next: () => {
        this.notifier.success('Pago pendiente eliminado');
        this.load();
      },
      error: () => undefined,
    });
  }

  format(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  /**
   * Estado del vencimiento: días restantes o vencido. Basado en dueDate
   * (informativo; el pago es puntual y no se planifica solo).
   */
  dueStatus(item: PendingPayment): { label: string; overdue: boolean; urgent: boolean } | null {
    if (!item.dueDate) return null;
    const tz = this.settings.timezone();
    const hoy = todayInTimeZone(tz);
    if (hoy > item.dueDate) {
      const diff = daysBetween(item.dueDate, hoy);
      return { label: `Vencido hace ${diff} día${diff === 1 ? '' : 's'}`, overdue: true, urgent: true };
    }
    if (hoy === item.dueDate) {
      return { label: 'Vence hoy', overdue: false, urgent: true };
    }
    const diff = daysBetween(hoy, item.dueDate);
    return { label: `Vence en ${diff} día${diff === 1 ? '' : 's'}`, overdue: false, urgent: diff <= 3 };
  }

  /** Fecha legible del vencimiento en la zona del usuario. */
  dueLabel(item: PendingPayment): string | null {
    if (!item.dueDate) return null;
    return formatInTimeZone(`${item.dueDate}T12:00:00`, this.settings.timezone(), 'yyyy-MM-dd');
  }
}

/** Días entre dos fechas yyyy-MM-dd (a > b). */
function daysBetween(from: string, to: string): number {
  const [fy, fm, fd] = from.split('-').map(Number);
  const [ty, tm, td] = to.split('-').map(Number);
  const a = new Date(fy, fm - 1, fd).getTime();
  const b = new Date(ty, tm - 1, td).getTime();
  return Math.round((b - a) / 86400000);
}

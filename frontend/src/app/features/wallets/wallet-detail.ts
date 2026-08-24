import { Component, inject, input, OnInit, signal, computed } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatAccordion, MatExpansionModule } from '@angular/material/expansion';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { WalletReport } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { formatInTimeZone, todayInTimeZone } from '../../core/utils/dates';
import { WalletDialog } from './wallet-dialog';

@Component({
  selector: 'app-wallet-detail',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatListModule, MatProgressSpinnerModule, MatDividerModule, RouterLink, MatAccordion, MatExpansionModule],
  templateUrl: './wallet-detail.html',
  styleUrls: ['./wallet-detail.scss'],
})
export class WalletDetail implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly notifier = inject(NotificationService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  readonly id = input.required<number>();
  readonly timezone = this.settings.timezone;

  report = signal<WalletReport | null>(null);
  loading = signal(true);
  isHandset = signal<boolean>(false);

  /** Resumen de ingresos/egresos/neto calculado desde el reporte. */
  readonly summary = computed(() => {
    const r = this.report();
    if (!r) return null;
    let income = 0;
    let expense = 0;
    for (const t of r.transactions) {
      if (t.type === 'income') income += t.amount - t.fee;
      else expense += t.amount;
    }
    return { income, expense, net: income - expense };
  });

  constructor(breakpointObserver: BreakpointObserver) {
    breakpointObserver.observe('(max-width: 800px)').subscribe((state) => {
      this.isHandset.set(state.matches);
    });
  }

  /** Fecha relativa corta: 'Hoy', 'Ayer' o 'D-mesAbrev', + hora. */
  cuandoRelativo(utc: string, tz: string | null | undefined): string {
    const tzone = tz || 'America/Caracas';
    const fecha = formatInTimeZone(utc, tzone, 'yyyy-MM-dd');
    const hora = formatInTimeZone(utc, tzone, 'HH:mm');
    const hoy = todayInTimeZone(tzone);
    const ayer = this.addDays(hoy, -1);
    let dia = fecha;
    if (fecha === hoy) dia = 'Hoy';
    else if (fecha === ayer) dia = 'Ayer';
    else {
      const [y, m, d] = fecha.split('-');
      const meses = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
      dia = `${Number(d)}-${meses[Number(m) - 1]}`;
    }
    return `${dia} · ${hora}`;
  }

  private addDays(dateStr: string, days: number): string {
    const d = new Date(dateStr + 'T00:00:00');
    d.setDate(d.getDate() + days);
    return d.toISOString().slice(0, 10);
  }

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.load();
  }

  load(): void {
    this.api.walletReport(this.id()).subscribe({
      next: (r) => {
        this.report.set(r);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  format(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  fmtDate(utc: string, tz: string | null | undefined): string {
    return formatInTimeZone(utc, tz || 'America/Caracas', 'yyyy-MM-dd');
  }

  fmtTime(utc: string, tz: string | null | undefined): string {
    return formatInTimeZone(utc, tz || 'America/Caracas', 'HH:mm');
  }

  back(): void {
    this.router.navigate(['/wallets']);
  }

  edit(): void {
    const w = this.report()?.wallet;
    if (!w) return;
    const ref = this.dialog.open(WalletDialog, { width: '420px', data: w });
    ref.afterClosed().subscribe((updated) => {
      if (updated) this.load();
    });
  }

  remove(): void {
    if (!confirm('¿Eliminar esta billetera?')) return;
    this.api.deleteWallet(this.id()).subscribe({
      next: () => {
        this.notifier.success('Billetera eliminada');
        this.router.navigate(['/wallets']);
      },
      error: () => undefined,
    });
  }

  /** Monto con signo + moneda, p.ej. '-1.500,00 VES'. */
  montoTx(t: { type: string; amount: number }, currency: string): string {
    const sign = t.type === 'income' ? '+' : '-';
    const num = Math.abs(t.amount).toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${sign}${num} ${currency ?? ''}`.trim();
  }

  /** Fee con moneda, p.ej. '14,00 VES'. */
  feeTx(t: { fee: number }, currency: string): string {
    const num = t.fee.toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${num} ${currency ?? ''}`.trim();
  }
}

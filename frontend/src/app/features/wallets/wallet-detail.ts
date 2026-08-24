import { Component, inject, input, OnInit, signal, computed, AfterViewInit, ElementRef } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { FormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { MatAccordion, MatExpansionModule } from '@angular/material/expansion';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { WalletReport, WalletReportTransaction } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { formatInTimeZone, todayInTimeZone } from '../../core/utils/dates';
import { WalletDialog } from './wallet-dialog';

@Component({
  selector: 'app-wallet-detail',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatListModule, MatProgressSpinnerModule, MatDividerModule, RouterLink, MatAccordion, MatExpansionModule, MatFormFieldModule, MatSelectModule, FormsModule],
  templateUrl: './wallet-detail.html',
  styleUrls: ['./wallet-detail.scss'],
})
export class WalletDetail implements OnInit, AfterViewInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly notifier = inject(NotificationService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  readonly id = input.required<number>();
  readonly timezone = this.settings.timezone;

  report = signal<WalletReport | null>(null);
  loading = signal(true);
  /** true si el ancho del contenido es menor a 1024px → modo móvil (acordeón). */
  isHandset = signal<boolean>(false);
  private ro: ResizeObserver | null = null;

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

  /** Filtro de periodo seleccionado en la lista. */
  selectedPeriod = 'all';

  readonly periods: { value: string; label: string }[] = [
    { value: 'all', label: 'Todo' },
    { value: 'today', label: 'Hoy' },
    { value: 'week', label: 'Esta semana' },
    { value: 'month', label: 'Este mes' },
    { value: 'year', label: 'Este año' },
  ];

  /** Transacciones del reporte filtradas por el periodo activo. */
  readonly filteredTx = computed<WalletReportTransaction[]>(() => {
    const r = this.report();
    if (!r) return [];
    const tzone = this.settings.timezone();
    const today = todayInTimeZone(tzone);
    const { from, to } = this.rangeFor(this.selectedPeriod, today);
    return r.transactions.filter((t) => {
      const d = formatInTimeZone(t.datetimeUtc, tzone, 'yyyy-MM-dd');
      if (from && d < from) return false;
      if (to && d > to) return false;
      return true;
    });
  });

  private rangeFor(period: string, today: string): { from?: string; to?: string } {
    if (period === 'today') return { from: today, to: today };
    if (period === 'week') return { from: this.addDays(today, -6), to: today };
    if (period === 'month') return { from: today.slice(0, 8) + '01', to: today };
    if (period === 'year') return { from: today.slice(0, 4) + '-01-01', to: today };
    return {};
  }

  constructor(private readonly el: ElementRef<HTMLElement>) {}

  ngAfterViewInit(): void {
    // Mide el ancho REAL del contenido (.container), sin el menú lateral.
    // Modo desktop solo cuando supera 1024px.
    const content = this.el.nativeElement.querySelector<HTMLElement>('.container');
    if (!content) return;
    this.ro = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect?.width ?? 0;
      this.isHandset.set(w < 1024);
    });
    this.ro.observe(content);
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

  /** Exporta las transacciones filtradas de la billetera a CSV. */
  exportCSV(): void {
    const header = ['ID', 'Fecha', 'Hora', 'Categoria', 'Tipo', 'Credito', 'Debito', 'Moneda', 'Descripcion'];
    const currency = this.report()?.wallet.currency ?? '';
    const rows = this.filteredTx().map((t) => {
      const tzone = this.settings.timezone();
      const fecha = formatInTimeZone(t.datetimeUtc, tzone, 'yyyy-MM-dd');
      const hora = formatInTimeZone(t.datetimeUtc, tzone, 'HH:mm');
      return [
        t.id,
        fecha,
        hora,
        t.category ?? '',
        t.type === 'income' ? 'Ingreso' : 'Gasto',
        t.type === 'income' ? t.amount : '',
        t.type === 'expense' ? t.amount : '',
        currency,
        t.description ?? '',
      ];
    });

    const csv = [header, ...rows]
      .map((row) => row.map((cell) => this.csvCell(cell)).join(','))
      .join('\n');
    const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `billetera_${this.id()}_transacciones.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  private csvCell(value: unknown): string {
    const s = value === null || value === undefined ? '' : String(value);
    if (/[",\n]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
    return s;
  }
}

import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatAccordion, MatExpansionModule } from '@angular/material/expansion';
import { MatChipsModule } from '@angular/material/chips';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { Transaction, Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { todayInTimeZone } from '../../core/utils/dates';
import { TransactionDialog } from './transaction-dialog';

interface PeriodOption {
  value: string;
  label: string;
}

@Component({
  selector: 'app-transactions',
  imports: [
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatSelectModule,
    RouterLink,
    FormsModule,
    MatAccordion,
    MatExpansionModule,
    MatChipsModule,
  ],
  templateUrl: './transactions.html',
  styleUrls: ['./transactions.scss'],
})
export class Transactions implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly settings = inject(SettingsStore);

  readonly timezone = this.settings.timezone;
  isHandset = signal<boolean>(false);

  transactions = signal<Transaction[]>([]);
  wallets = signal<Wallet[]>([]);
  total = signal(0);
  loading = signal(true);

  page = 1;
  limit = 20;
  tz = 'America/Caracas';

  /** Filtro de periodo seleccionado en el desplegable. */
  selectedPeriod = 'all';
  /** Rango de fechas aplicado (from/to) deducido del periodo elegido. */
  private appliedFrom: string | undefined;
  private appliedTo: string | undefined;

  readonly periods: PeriodOption[] = [
    { value: 'all', label: 'Todo' },
    { value: 'today', label: 'Hoy' },
    { value: 'week', label: 'Esta semana' },
    { value: 'month', label: 'Este mes' },
    { value: 'year', label: 'Este año' },
  ];

  constructor(breakpointObserver: BreakpointObserver) {
    breakpointObserver.observe('(max-width: 800px)').subscribe((state) => {
      this.isHandset.set(state.matches);
    });
  }

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.tz = this.settings.timezone();
    this.api.wallets().subscribe((w) => this.wallets.set(w));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api
      .transactions({
        page: this.page,
        limit: this.limit,
        from: this.appliedFrom,
        to: this.appliedTo,
      })
      .subscribe({
        next: (res) => {
          this.transactions.set(res.data);
          this.total.set(res.total);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  /** Aplica el rango del periodo elegido y recarga desde la página 1. */
  applyPeriod(): void {
    const range = this.resolveRange(this.selectedPeriod);
    this.appliedFrom = range.from;
    this.appliedTo = range.to;
    this.page = 1;
    this.load();
  }

  private resolveRange(period: string): { from?: string; to?: string } {
    const today = todayInTimeZone(this.tz);
    if (period === 'today') return { from: today, to: today };
    if (period === 'week') {
      return { from: this.addDays(today, -6), to: today };
    }
    if (period === 'month') {
      return { from: today.slice(0, 8) + '01', to: today };
    }
    if (period === 'year') {
      return { from: today.slice(0, 4) + '-01-01', to: today };
    }
    return {}; // all
  }

  private addDays(dateStr: string, days: number): string {
    const d = new Date(dateStr + 'T00:00:00');
    d.setDate(d.getDate() + days);
    return d.toISOString().slice(0, 10);
  }

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.limit = e.pageSize;
    this.load();
  }

  openCreate(): void {
    const ref = this.dialog.open(TransactionDialog, { width: '460px', data: { wallets: this.wallets(), tz: this.tz } });
    ref.afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  /** Exporta las transacciones (filtradas por el periodo aplicado) a CSV. */
  exportCSV(): void {
    const header = ['ID', 'Fecha', 'Hora', 'Categoria', 'Tipo', 'Billetera', 'Credito', 'Debito', 'Moneda', 'Descripcion'];
    const rows = this.transactions().map((t) => [
      t.id,
      t.date,
      t.time,
      t.category,
      t.type === 'income' ? 'Ingreso' : 'Gasto',
      t.walletName ?? '',
      t.type === 'income' ? t.amount : '',
      t.type === 'expense' ? t.amount : '',
      t.walletCurrency ?? '',
      t.description ?? '',
    ]);

    const csv = [header, ...rows]
      .map((row) => row.map((cell) => this.csvCell(cell)).join(','))
      .join('\n');

    const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    const stamp = this.tz ? todayInTimeZone(this.tz).replace(/-/g, '') : '';
    a.href = url;
    a.download = `transacciones${stamp ? '_' + stamp : ''}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  private csvCell(value: unknown): string {
    const s = value === null || value === undefined ? '' : String(value);
    // Escapa comillas dobles y envuelve en comillas si hay comas, comillas o saltos.
    if (/[",\n]/.test(s)) {
      return '"' + s.replace(/"/g, '""') + '"';
    }
    return s;
  }

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }

  /** Fecha DD/MM/AAAA a partir de YYYY-MM-DD. */
  fecha(t: Transaction): string {
    const [y, m, d] = t.date.split('-');
    return `${Number(d)}/${Number(m)}/${y}`;
  }

  /** Monto con signo + moneda, p.ej. '-1.500,00 VES'. */
  monto(t: Transaction): string {
    const sign = t.type === 'income' ? '+' : '-';
    const num = Math.abs(t.amount).toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${sign}${num} ${t.walletCurrency ?? ''}`.trim();
  }

  /** Monto absoluto (sin signo) + moneda. */
  montoAbs(t: Transaction): string {
    const num = Math.abs(t.amount).toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${num} ${t.walletCurrency ?? ''}`.trim();
  }

  /** Fee con moneda, p.ej. '14,00 VES'. */
  montoAbsFee(t: Transaction): string {
    const num = t.fee.toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${num} ${t.walletCurrency ?? ''}`.trim();
  }

  tipoLabel(t: Transaction): string {
    return t.type === 'income' ? 'Ingreso' : 'Gasto';
  }

  /** Formatea la hora + prefijo relativo (Hoy/Ayer/fecha). */
  cuando(t: Transaction): string {
    return `${t.date} · ${t.time}`;
  }

  /** Fecha relativa corta: 'Hoy', 'Ayer' o 'D-mesAbrev' (26-ago), + hora. */
  cuandoRelativo(t: Transaction): string {
    const hoy = todayInTimeZone(this.tz);
    const ayer = this.addDays(hoy, -1);
    let dia = t.date;
    if (t.date === hoy) dia = 'Hoy';
    else if (t.date === ayer) dia = 'Ayer';
    else {
      const [y, m, d] = t.date.split('-');
      const meses = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
      dia = `${Number(d)}-${meses[Number(m) - 1]}`;
    }
    return `${dia} · ${t.time}`;
  }
}

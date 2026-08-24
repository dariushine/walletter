import { Component, inject, OnInit, signal, AfterViewInit, ElementRef } from '@angular/core';
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
import { MatAccordion, MatExpansionModule } from '@angular/material/expansion';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { Exchange, Wallet } from '../../models/walletter.models';
import { todayInTimeZone } from '../../core/utils/dates';
import { ExchangeDialog } from './exchange-dialog';

interface PeriodOption {
  value: string;
  label: string;
}

@Component({
  selector: 'app-exchanges',
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
  ],
  templateUrl: './exchanges.html',
  styleUrls: ['./exchanges.scss'],
})
export class Exchanges implements OnInit, AfterViewInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(NotificationService);
  private readonly settings = inject(SettingsStore);

  readonly timezone = this.settings.timezone;
  /** true si el ancho del contenido es menor a 1024px → modo móvil (acordeón). */
  isHandset = signal<boolean>(false);
  private ro: ResizeObserver | null = null;

  exchanges = signal<Exchange[]>([]);
  wallets = signal<Wallet[]>([]);
  total = signal(0);
  loading = signal(true);

  page = 1;
  limit = 20;
  tz = 'America/Caracas';

  /** Filtro de periodo seleccionado. */
  selectedPeriod = 'all';
  /** Rango de fechas aplicado (from/to) deducido del periodo. */
  private appliedFrom: string | undefined;
  private appliedTo: string | undefined;

  readonly periods: PeriodOption[] = [
    { value: 'all', label: 'Todo' },
    { value: 'today', label: 'Hoy' },
    { value: 'week', label: 'Esta semana' },
    { value: 'month', label: 'Este mes' },
    { value: 'year', label: 'Este año' },
  ];

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

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.tz = this.settings.timezone();
    this.api.wallets().subscribe((w) => this.wallets.set(w));
    this.load();
  }

  load(): void {
    // El endpoint de exchanges no acepta from/to; se carga paginado.
    this.loading.set(true);
    this.api.exchanges({ page: this.page, limit: this.limit }).subscribe({
      next: (res) => {
        this.exchanges.set(res.data);
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
    const ref = this.dialog.open(ExchangeDialog, { width: '480px', data: { wallets: this.wallets(), tz: this.tz } });
    ref.afterClosed().subscribe((created) => {
      if (created) {
        this.notifier.success('Exchange registrado');
        this.load();
      }
    });
  }

  /** Exporta los exchanges (pagina actual) a CSV. */
  exportCSV(): void {
    const header = ['ID', 'Fecha', 'Hora', 'Origen', 'Destino', 'Monto Origen', 'Monto Destino', 'Tasa', 'Fee Debito', 'Fee Credito', 'Descripcion'];
    const rows = this.exchanges().map((e) => [
      e.id,
      this.fecha(e.createdAt),
      this.hora(e.createdAt),
      e.fromWalletName,
      e.toWalletName,
      e.fromAmount,
      e.toAmount,
      e.rate,
      e.fee,
      e.creditFee,
      e.description ?? '',
    ]);
    const csv = [header, ...rows].map((row) => row.map((cell) => this.csvCell(cell)).join(',')).join('\n');
    const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `exchanges_${todayInTimeZone(this.tz).replace(/-/g, '')}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  private csvCell(value: unknown): string {
    const s = value === null || value === undefined ? '' : String(value);
    if (/[\",\n]/.test(s)) return '"' + s.replace(/\"/g, '""') + '"';
    return s;
  }

  /** Fecha DD/MM/AAAA desde ISO. */
  fecha(iso: string): string {
    const d = new Date(iso);
    return `${d.getDate()}/${d.getMonth() + 1}/${d.getFullYear()}`;
  }

  /** Hora HH:MM desde ISO. */
  hora(iso: string): string {
    const d = new Date(iso);
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }

  /** Monto con signo + moneda, p.ej. '-5.000,00 VES'. */
  monto(e: Exchange, amount: number, currency: string, sign = '-'): string {
    const num = Math.abs(amount).toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${sign}${num} ${currency ?? ''}`.trim();
  }

  /** Fee con moneda, p.ej. '90,00 VES'. */
  fee(e: Exchange, amount: number, currency: string): string {
    const num = amount.toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${num} ${currency ?? ''}`.trim();
  }

  sym(currency: string): string {
    return (currency || '').toUpperCase();
  }
}

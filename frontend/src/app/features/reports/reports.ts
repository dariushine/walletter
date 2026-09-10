import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatAccordion, MatExpansionModule } from '@angular/material/expansion';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTooltipModule } from '@angular/material/tooltip';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { UiPreferenceStore } from '../../core/services/ui-preference.store';
import { ReportData, PerformanceResponse } from '../../models/walletter.models';
import { formatNumber, formatWithCode } from '../../core/utils/money';

type RateType = 'bcv' | 'paralelo';
type PeriodId = 'month' | 'year' | 'custom' | 'all';
type Granularity = 'day' | 'month' | 'year';
type SortKey = 'key' | 'income' | 'expense' | 'net' | 'transactionCount';
type SortDir = 'asc' | 'desc';

const PAGE_SIZE_OPTIONS = [5, 10, 20, 50];

const PERIODS: { id: PeriodId; label: string }[] = [
  { id: 'month', label: 'Mes' },
  { id: 'year', label: 'Año' },
  { id: 'custom', label: 'Personalizado' },
  { id: 'all', label: 'Todo' },
];

const GRANS: { id: Granularity; label: string }[] = [
  { id: 'day', label: 'Día' },
  { id: 'month', label: 'Mes' },
  { id: 'year', label: 'Año' },
];

const MESES = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

function pad2(n: number): string {
  return n < 10 ? `0${n}` : String(n);
}

function currentParts(): { y: number; m: number } {
  const d = new Date();
  return { y: d.getFullYear(), m: d.getMonth() + 1 };
}

@Component({
  selector: 'app-reports',
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatAccordion,
    MatExpansionModule,
    MatPaginatorModule,
    MatTooltipModule,
  ],
  templateUrl: './reports.html',
  styleUrls: ['./reports.scss'],
})
export class Reports implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly prefs = inject(UiPreferenceStore);

  readonly decimalSeparator = this.prefs.decimalSeparator;
  readonly periods = PERIODS;
  readonly grans = GRANS;

  data = signal<ReportData | null>(null);
  loading = signal(true);

  // Paginación server-side del performance.
  page = signal(1);
  limit = signal(5);
  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;

  // Ordenamiento server-side: por defecto fecha-hora descendente (key desc).
  sortBy = signal<SortKey>('key');
  sortDir = signal<SortDir>('desc');

  // Fullscreen: el panel de performance ocupa toda la fila.
  perfFullscreen = signal(false);

  rateType = signal<RateType>(this.prefs.rate('reports', 'bcv'));
  period = signal<PeriodId>((this.prefs.period('reports', 'month') as PeriodId) || 'month');
  gran = signal<Granularity>((this.prefs.period('reports.gran', 'month') as Granularity) || 'month');

  /** Referencia navegada: 'YYYY-MM' en modo mes, 'YYYY' en modo año. */
  refMonth = signal<string>(this.prefs.period('reports.refMonth', this.defaultRefMonth()));
  refYear = signal<string>(this.prefs.period('reports.refYear', String(currentParts().y)));

  customFrom = signal<string>(this.prefs.period('reports.from', this.defaultFrom()));
  customTo = signal<string>(this.prefs.period('reports.to', this.defaultTo()));

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.load();
  }

  /** Filas de performance normalizadas (compat: performance ?? monthly). */
  readonly rows = computed(() => {
    const d = this.data();
    if (!d) return [];
    const src = d.performance?.length ? d.performance : (d.monthly as unknown as typeof d.performance) ?? [];
    return src.map((r) => ({
      key: (r as any).key ?? (r as any).month ?? '',
      income: r.income,
      expense: r.expense,
      net: r.net,
      transactionCount: r.transactionCount,
    }));
  });

  /** Total de filas antes de paginar (para el paginador). */
  readonly perfTotal = computed(() => this.data()?.performanceTotal ?? this.rows().length);

  /** Neto del periodo anterior (referencia de tendencia), desde el backend. */
  readonly prevNet = computed(() => this.data()?.meta?.prevNet ?? null);

  load(): void {
    this.loading.set(true);
    const params: {
      period: string;
      rate: string;
      tz: string;
      granularity: string;
      refDate?: string;
      from?: string;
      to?: string;
      sortBy?: string;
      sortDir?: string;
      page?: number;
      limit?: number;
    } = {
      period: this.period(),
      rate: this.rateType(),
      tz: this.settings.timezone(),
      granularity: this.gran(),
      sortBy: this.sortBy(),
      sortDir: this.sortDir(),
      page: this.page(),
      limit: this.limit(),
    };
    if (this.period() === 'month') params.refDate = this.refMonth();
    else if (this.period() === 'year') params.refDate = this.refYear();
    else if (this.period() === 'custom') {
      params.from = this.customFrom() || undefined;
      params.to = this.customTo() || undefined;
    }
    // 'all' no manda refDate ni from/to: el backend toma todo el historial.
    this.api
      .reports(params)
      .subscribe({
        next: (r) => {
          this.data.set(r);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  /** Carga solo los datos de performance usando el endpoint específico */
  loadPerformanceOnly(): void {
    const params = {
      period: this.period(),
      rate: this.rateType(),
      tz: this.settings.timezone(),
      granularity: this.gran(),
      sortBy: this.sortBy(),
      sortDir: this.sortDir(),
      page: this.page(),
      limit: this.limit(),
      refDate: this.period() === 'month' ? this.refMonth() : 
               this.period() === 'year' ? this.refYear() : undefined,
      from: this.period() === 'custom' ? this.customFrom() || undefined : undefined,
      to: this.period() === 'custom' ? this.customTo() || undefined : undefined,
    };
    
    this.api
      .performance(params)
      .subscribe({
        next: (response) => {
          // Actualizar solo los datos de performance manteniendo el resto
          const currentData = this.data();
          if (currentData) {
            this.data.set({
              ...currentData,
              performance: response.performance,
              performanceTotal: response.performanceTotal,
              monthly: response.performance.map(p => ({
                month: p.key,
                income: p.income,
                expense: p.expense,
                net: p.net,
                transactionCount: p.transactionCount
              })),
            });
          }
        },
        error: () => {
          // Si falla el endpoint específico, cargar todo
          this.load();
        },
      });
  }

  setRate(rate: RateType): void {
    if (this.rateType() === rate) return;
    this.rateType.set(rate);
    this.prefs.setRate('reports', rate);
    this.page.set(1);
    this.load();
  }

  setPeriod(p: PeriodId): void {
    if (this.period() === p) return;
    this.period.set(p);
    this.prefs.setPeriod('reports', p);
    // Al pasar a personalizado, precargar mes en curso si no hay rango guardado.
    if (p === 'custom') {
      if (!this.customFrom()) {
        this.customFrom.set(this.defaultFrom());
        this.prefs.setPeriod('reports.from', this.customFrom());
      }
      if (!this.customTo()) {
        this.customTo.set(this.defaultTo());
        this.prefs.setPeriod('reports.to', this.customTo());
      }
    }
    this.page.set(1);
    this.load();
  }

  setGran(g: Granularity): void {
    if (this.gran() === g) return;
    this.gran.set(g);
    this.prefs.setPeriod('reports.gran', g);
    this.page.set(1);
    this.load();
  }

  setCustomFrom(v: string): void {
    this.customFrom.set(v);
    this.prefs.setPeriod('reports.from', v);
    this.page.set(1);
    this.load();
  }

  setCustomTo(v: string): void {
    this.customTo.set(v);
    this.prefs.setPeriod('reports.to', v);
    this.page.set(1);
    this.load();
  }

  /** Alterna orden por columna: misma columna → cambia dirección; nueva → desc (o asc si ya está desc). */
  setSort(col: SortKey): void {
    if (this.sortBy() === col) {
      this.sortDir.set(this.sortDir() === 'desc' ? 'asc' : 'desc');
    } else {
      this.sortBy.set(col);
      this.sortDir.set('desc');
    }
    this.page.set(1);
    // Solo recargar performance, no todo
    this.loadPerformanceOnly();
  }

  sortIcon(col: SortKey): string {
    if (this.sortBy() !== col) return 'unfold_more';
    return this.sortDir() === 'desc' ? 'arrow_drop_down' : 'arrow_drop_up';
  }

  onPage(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
    this.limit.set(e.pageSize);
    // Solo recargar performance, no todo
    this.loadPerformanceOnly();
  }

  toggleFullscreen(): void {
    this.perfFullscreen.set(!this.perfFullscreen());
  }

  canGoPrev(): boolean {
    return true;
  }

  canGoNext(): boolean {
    const { y, m } = currentParts();
    if (this.period() === 'month') {
      const [ry, rm] = this.refMonth().split('-').map(Number);
      return ry < y || (ry === y && rm < m);
    }
    if (this.period() === 'year') return Number(this.refYear()) < y;
    return false;
  }

  goPrev(): void {
    if (this.period() === 'month') {
      const [y, m] = this.refMonth().split('-').map(Number);
      const d = new Date(y, m - 2, 1);
      this.refMonth.set(`${d.getFullYear()}-${pad2(d.getMonth() + 1)}`);
      this.prefs.setPeriod('reports.refMonth', this.refMonth());
      this.page.set(1);
      this.load();
    } else if (this.period() === 'year') {
      const ny = Number(this.refYear()) - 1;
      this.refYear.set(String(ny));
      this.prefs.setPeriod('reports.refYear', this.refYear());
      this.page.set(1);
      this.load();
    }
  }

  goNext(): void {
    if (!this.canGoNext()) return;
    if (this.period() === 'month') {
      const [y, m] = this.refMonth().split('-').map(Number);
      const d = new Date(y, m, 1);
      this.refMonth.set(`${d.getFullYear()}-${pad2(d.getMonth() + 1)}`);
      this.prefs.setPeriod('reports.refMonth', this.refMonth());
      this.page.set(1);
      this.load();
    } else if (this.period() === 'year') {
      const ny = Number(this.refYear()) + 1;
      this.refYear.set(String(ny));
      this.prefs.setPeriod('reports.refYear', this.refYear());
      this.page.set(1);
      this.load();
    }
  }

  /** Label del navegador: "septiembre 2026" (mes) o "2026" (año). */
  navLabel(): string {
    if (this.period() === 'month') {
      const [y, m] = this.refMonth().split('-').map(Number);
      const nombres = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
      return `${nombres[(m || 1) - 1]} ${y}`;
    }
    if (this.period() === 'year') return this.refYear();
    return '';
  }

  /** Título del panel de performance según granularidad. */
  perfTitle(): string {
    return this.gran() === 'day' ? 'Performance Diario' : this.gran() === 'year' ? 'Performance Anual' : 'Performance Mensual';
  }

  /** Label de fila según granularidad. */
  rowLabel(key: string): string {
    if (!key) return '';
    if (this.gran() === 'day') {
      // YYYY-MM-DD → "12 sep 2026"
      const [y, m, d] = key.split('-').map(Number);
      if (!y || !m || !d) return key;
      return `${d} ${MESES[m - 1]} ${y}`;
    }
    if (this.gran() === 'year') return key;
    return this.monthLabel(key);
  }

  /** Nombre legible del mes YYYY-MM → 'ago 2026'. */
  monthLabel(month: string): string {
    const [y, m] = (month || '').split('-');
    if (!y || !m) return month;
    return `${MESES[Number(m) - 1]} ${y}`;
  }

  pct(n: number): string {
    return `${(n ?? 0).toLocaleString('es-VE', { maximumFractionDigits: 1 })}%`;
  }

  /** "USD 1.402,16" (código como prefijo, como en el dashboard de referencia). */
  fmtUsd(n: number): string {
    return `USD ${formatNumber(n ?? 0, 2, this.decimalSeparator())}`;
  }

  /** Monto de billetera en su moneda nativa (p.ej. "Bs.S 124.260,90" o "USD 380,00"). */
  fmtWallet(balance: number, currency: string): string {
    const cur = (currency || '').toUpperCase();
    const num = formatNumber(balance ?? 0, 2, this.decimalSeparator());
    if (cur === 'VES') return `Bs.S ${num}`;
    return `${cur} ${num}`;
  }

  /** Variación porcentual entre dos netos. */
  trendPct(prevNet: number, net: number): string {
    const base = Math.max(Math.abs(prevNet || 0), 1);
    return this.pct(((net - prevNet) / base) * 100);
  }

  catWidth(total: number): number {
    const base = this.data()?.byCategoryTotal || this.data()?.summary.totalExpenses || 1;
    return Math.min((total / (base || 1)) * 100, 100);
  }

  exportReport(): void {
    const d = this.data();
    if (!d) return;
    const report = {
      generado: new Date().toISOString(),
      rango: this.period(),
      refDate: this.period() === 'month' ? this.refMonth() : this.period() === 'year' ? this.refYear() : undefined,
      desde: this.period() === 'custom' ? this.customFrom() : d.meta?.from,
      hasta: this.period() === 'custom' ? this.customTo() : d.meta?.to,
      tasa: this.rateType(),
      granularidad: this.gran(),
      resumen: d.summary,
      categorias: d.byCategory,
      performance: this.rows(),
      billeteras: d.walletBalances,
      exchanges: d.exchangeStats,
    };
    const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `reporte_finanzas_${new Date().toISOString().split('T')[0]}.json`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  /** 'YYYY-MM' del mes en curso. */
  private defaultRefMonth(): string {
    const { y, m } = currentParts();
    return `${y}-${pad2(m)}`;
  }

  /** Primer día del mes en curso (YYYY-MM-DD). */
  private defaultFrom(): string {
    const { y, m } = currentParts();
    return `${y}-${pad2(m)}-01`;
  }

  /** Hoy (YYYY-MM-DD). */
  private defaultTo(): string {
    const d = new Date();
    return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
  }
}

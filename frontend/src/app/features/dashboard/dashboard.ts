import { Component, inject, OnInit, signal, AfterViewInit, ElementRef } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { UiPreferenceStore } from '../../core/services/ui-preference.store';
import { Wallet, Transaction, ReportData } from '../../models/walletter.models';
import { formatMoney, currencySymbol, currencyName, formatWithCode } from '../../core/utils/money';
import { todayInTimeZone } from '../../core/utils/dates';

/** Rangos del gráfico de rendimiento. */
type ChartRange = '1d' | '7d' | '1m' | '1y' | 'ytd';

const RANGES: { id: ChartRange; label: string }[] = [
  { id: '1d', label: '1D' },
  { id: '7d', label: '7D' },
  { id: '1m', label: '1M' },
  { id: '1y', label: '1Y' },
  { id: 'ytd', label: 'YTD' },
];

/** Un punto del gráfico (día/semana/mes según rango). */
interface ChartPoint {
  key: string;
  label: string;
  income: number;
  expense: number;
}

@Component({
  selector: 'app-dashboard',
  imports: [
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatButtonModule,
    MatExpansionModule,
    MatTooltipModule,
    RouterLink,
  ],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.scss'],
})
export class Dashboard implements OnInit, AfterViewInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly prefs = inject(UiPreferenceStore);

  /** true si el ancho del contenido es menor a 1024px → modo móvil. */
  readonly isHandset = signal<boolean>(false);
  private ro: ResizeObserver | null = null;

  readonly timezone = this.settings.timezone;
  readonly hideBalances = this.prefs.hideBalances;
  readonly decimalSeparator = this.prefs.decimalSeparator;

  /** Ids de billeteras cuyo saldo se reveló con el ojo. */
  private readonly revealedIds = signal<Set<number>>(new Set());

  /** Tasa Bs/USD del día para el equivalente de billeteras VES. */
  private rate = signal<number | null>(null);

  /** Tasas BCV / paralelo mostradas en la tarjeta de balance. */
  readonly bcv = signal<number | null>(null);
  readonly paralelo = signal<number | null>(null);
  readonly rateDate = signal<string | null>(null);

  /** Tasa elegida para el cálculo (toggle BCV/Paralelo). Persistida en el navegador. */
  readonly selectedRate = signal<'bcv' | 'paralelo'>(this.prefs.rate('dashboard', 'bcv'));

  /** Revelado propio del balance total (independiente de hideBalances global). */
  readonly totalRevealed = signal(false);

  wallets = signal<Wallet[]>([]);
  recent = signal<Transaction[]>([]);
  /** Total de transacciones disponible según el backend. */
  private recentTotal = 0;
  /** Indica si hay un 'cargar más' en curso. */
  readonly loadingMore = signal(false);
  loading = signal(true);

  /** Datos del reporte mensual (ingresos/gastos/ahorro). */
  report = signal<ReportData | null>(null);

  /** Rango activo del gráfico de rendimiento. */
  readonly chartRange = signal<ChartRange>('7d');
  readonly ranges = RANGES;

  /** Transacciones del rango activo (para el gráfico). */
  private chartTx = signal<Transaction[]>([]);

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
    this.loadRate();
    this.load();
    this.loadReport();
    this.loadChart();
  }

  /** Carga el reporte mensual en USD para el resumen de mes. */
  private loadReport(): void {
    this.api
      .reports({ period: '1m', rate: this.selectedRate(), tz: this.settings.timezone() })
      .subscribe({
        next: (r) => this.report.set(r),
        error: () => undefined,
      });
  }

  /** Cambia el rango del gráfico y recarga. */
  setChartRange(r: ChartRange): void {
    if (this.chartRange() === r) return;
    this.chartRange.set(r);
    this.loadChart();
  }

  /** Rango [from, to) según el rango activo, en la zona del usuario. */
  private rangeSpan(): { from: string; to: string } {
    const hoy = this.today();
    switch (this.chartRange()) {
      case '1d':
        return { from: hoy, to: this.addDays(hoy, 1) };
      case '1m':
        return { from: this.addDays(hoy, -29), to: this.addDays(hoy, 1) };
      case '1y':
        return { from: this.addDays(hoy, -364), to: this.addDays(hoy, 1) };
      case 'ytd':
        return { from: `${hoy.slice(0, 4)}-01-01`, to: this.addDays(hoy, 1) };
      default:
        return { from: this.addDays(hoy, -6), to: this.addDays(hoy, 1) };
    }
  }

  /** Carga TODAS las transacciones del rango activo (pagina de 100 en 100). */
  private loadChart(accum: Transaction[] = [], page = 1): void {
    const { from, to } = this.rangeSpan();
    this.api.transactions({ from, to, page, limit: 100 }).subscribe({
      next: (res) => {
        const next = [...accum, ...(res.data ?? [])];
        if (next.length < res.total && page < 10) {
          this.loadChart(next, page + 1);
        } else {
          this.chartTx.set(next);
        }
      },
      error: () => this.chartTx.set(accum),
    });
  }

  private loadRate(): void {
    this.api.effectiveRate().subscribe({
      next: (e) => {
        this.bcv.set(e.vps?.bcv ?? null);
        this.paralelo.set(e.vps?.paralelo ?? null);
        this.rateDate.set(e.date ?? null);
        this.rate.set(this.pickRate(e.vps?.bcv, e.vps?.paralelo));
      },
      error: () => undefined,
    });
  }

  /** Devuelve la tasa correspondiente al toggle seleccionado. */
  private pickRate(bcv: number | null | undefined, paralelo: number | null | undefined): number | null {
    return this.selectedRate() === 'paralelo' ? (paralelo ?? bcv ?? null) : (bcv ?? paralelo ?? null);
  }

  /** Valor numérico de la tasa activa. */
  rateNumber(): number | null {
    return this.selectedRate() === 'paralelo' ? (this.paralelo() ?? this.bcv()) : (this.bcv() ?? this.paralelo());
  }

  toggleRate(kind: 'bcv' | 'paralelo'): void {
    if (this.selectedRate() === kind) return;
    this.selectedRate.set(kind);
    this.prefs.setRate('dashboard', kind);
    this.rate.set(this.pickRate(this.bcv(), this.paralelo()));
  }

  toggleTotalReveal(): void {
    this.totalRevealed.update((v) => !v);
  }

  isTotalHidden(): boolean {
    return this.hideBalances() && !this.totalRevealed();
  }

  /** Billeteras a mostrar en el dashboard (excluye las ocultas). */
  visibleWallets(): Wallet[] {
    return this.wallets().filter((w) => !w.hideInDashboard);
  }

  /** Desplaza el carrusel de billeteras. dir = -1 (izquierda) | 1 (derecha). */
  scrollWallets(dir: number): void {
    const el = this.el.nativeElement.querySelector<HTMLElement>('.wallet-scroll');
    if (!el) return;
    el.scrollBy({ left: dir * 280, behavior: 'smooth' });
  }

  /**
   * Balance total en USD: suma todas las billeteras activas no excluidas,
   * convirtiendo las VES a USD con la tasa elegida (BCV o paralelo).
   */
  totalUsd(): number {
    const r = this.rateNumber();
    let total = 0;
    for (const w of this.wallets()) {
      if (!w.isActive || w.excludeFromTotal) continue;
      const cur = (w.currency || '').toUpperCase();
      if (cur === 'VES') {
        total += r ? w.balance / r : 0;
      } else {
        total += w.balance;
      }
    }
    return total;
  }

  /** Texto del balance total: USD (convertido) o máscara / oculto. */
  totalLabel(): string {
    if (this.isTotalHidden()) return '•••';
    const total = this.totalUsd();
    return formatWithCode(total, 'USD', this.decimalSeparator());
  }

  /** Equivalente en VES del balance total (≈ bajo el hero). */
  totalVesLabel(): string {
    if (this.isTotalHidden()) return '•••';
    const r = this.rateNumber();
    if (!r) return '';
    const ves = this.totalUsd() * r;
    return `≈ ${formatWithCode(ves, 'VES', this.decimalSeparator())}`;
  }

  private load(): void {
    this.loading.set(true);
    this.recentTotal = 0;
    this.recent.set([]);
    this.api.wallets().subscribe({
      next: (w) => this.wallets.set(w),
      error: () => this.loading.set(false),
    });
    // Carga inicial de las 5 más recientes (de 5 en 5 con 'Ver más').
    this.loadRecent(1);
  }

  /** Carga una página de transacciones y la añade a las ya cargadas. */
  private loadRecent(page: number): void {
    this.api.transactions({ page, limit: 5 }).subscribe({
      next: (res) => {
        this.recentTotal = res.total;
        this.recent.update((current) => [...current, ...res.data]);
        this.loading.set(false);
        this.loadingMore.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadingMore.set(false);
      },
    });
  }

  /** true si aún quedan transacciones por cargar. */
  canLoadMore(): boolean {
    return this.recent().length < this.recentTotal;
  }

  /** Carga las siguientes 5 y las agrega debajo de las ya mostradas. */
  loadMore(): void {
    if (this.loadingMore() || !this.canLoadMore()) return;
    this.loadingMore.set(true);
    const nextPage = Math.floor(this.recent().length / 5) + 1;
    this.loadRecent(nextPage);
  }

  format(amount: number, currency = 'USD'): string {
    return formatMoney(amount, currency, this.decimalSeparator());
  }

  /** Devuelve el monto de la billetera, o máscara, según preferencia/revelado. */
  saldo(w: Wallet): string {
    if (this.hideBalances() && !this.revealedIds().has(w.id)) return '••• ' + w.currency.toUpperCase();
    return formatWithCode(w.balance, w.currency, this.decimalSeparator());
  }

  isHidden(w: Wallet): boolean {
    return this.hideBalances() && !this.revealedIds().has(w.id);
  }

  toggleReveal(w: Wallet): void {
    const set = new Set(this.revealedIds());
    if (set.has(w.id)) set.delete(w.id);
    else set.add(w.id);
    this.revealedIds.set(set);
  }

  name(currency: string): string {
    return currencyName(currency);
  }

  /** Formatea un número como USD: 'USD 1.402,16' (patrón del resto de la app). */
  fmtUsd(n: number): string {
    const num = n.toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `USD ${num}`;
  }

  /** Texto de la hora de actualización de las tasas. */
  updatedLabel(): string {
    const d = this.rateDate();
    if (!d) return 'Actualizado recién';
    const [y, m, day] = d.split('-').map(Number);
    if (!y || !m || !day) return 'Actualizado recién';
    const dt = new Date(y, m - 1, day);
    const now = new Date();
    const sameDay = dt.toDateString() === now.toDateString();
    const time = dt.toLocaleTimeString('es-VE', { hour: '2-digit', minute: '2-digit' });
    if (sameDay) return `Actualizado hoy ${time}`;
    return `Actualizado ${dt.toLocaleDateString('es-VE', { day: 'numeric', month: 'short' })} ${time}`;
  }

  /** Texto bajo el saldo: denominación (USD) o equivalente≈ en USD con tasa (VES). */
  subline(w: Wallet): string {
    if ((w.currency || '').toUpperCase() === 'VES') {
      const r = this.rate();
      const hidden = this.isHidden(w);
      if (!r) return '';
      const usd = w.balance / r;
      const equiv = hidden ? '•••' : formatWithCode(usd, 'USD', this.decimalSeparator());
      return `≈ USD ${equiv} USD (tasa ${r.toLocaleString('es-VE', { maximumFractionDigits: 2 })})`;
    }
    return this.name(w.currency);
  }

  symbol(currency: string): string {
    return currencySymbol(currency);
  }

  // ===== Resumen mensual (reporte 1m en USD) =====

  /** Ingresos del mes (desde el reporte en USD). */
  monthIncome(): number {
    return this.report()?.summary?.totalIncome ?? 0;
  }

  /** Gastos del mes (desde el reporte en USD). */
  monthExpense(): number {
    return this.report()?.summary?.totalExpenses ?? 0;
  }

  /** Ahorro estimado del mes (ingresos - gastos). */
  monthNet(): number {
    return this.monthIncome() - this.monthExpense();
  }

  /** Texto del mes actual: 'agosto 2026' (para el encabezado del resumen). */
  monthLabel(): string {
    const meses = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
    const now = new Date();
    const y = now.getFullYear();
    const m = now.getMonth();
    return `${meses[m]} ${y}`;
  }

  /** Con signo y color: '+$2.450,00' / '-$1.120,50' (estilo del mockup). */
  monthSigned(n: number): string {
    const sign = n >= 0 ? '+' : '-';
    const num = Math.abs(n).toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${sign}$${num}`;
  }

  // ===== Gráfico de barras (por rango) =====

  /** Serie del gráfico según el rango activo. */
  chartData(): ChartPoint[] {
    const r = this.chartRange();
    if (r === '1d') return this.buildChart('day');
    if (r === '7d') return this.buildChart('day', 7);
    if (r === '1m') return this.buildChart('week');
    return this.buildChart('month');
  }

  /** Construye los puntos del gráfico acumulando las transacciones del rango. */
  private buildChart(granularity: 'day' | 'week' | 'month', days = 30): ChartPoint[] {
    const hoy = this.today();
    const tx = this.chartTx();
    const points: ChartPoint[] = [];
    const meses = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

    const usdOf = (t: Transaction): number => {
      const cur = (t.walletCurrency || '').toUpperCase();
      const r = this.rateNumber();
      return cur === 'VES' && r ? t.amount / r : t.amount;
    };

    const accum = (key: string, t: Transaction): void => {
      let p = points.find((x) => x.key === key);
      if (!p) return;
      if (t.type === 'income') p.income += usdOf(t);
      else p.expense += usdOf(t);
    };

    if (granularity === 'day') {
      for (let i = days - 1; i >= 0; i--) {
        const day = this.addDays(hoy, -i);
        const dt = new Date(day + 'T00:00:00');
        points.push({ key: day, label: `${dt.getDate()} ${meses[dt.getMonth()]}`, income: 0, expense: 0 });
      }
      for (const t of this.chartTx()) accum(t.date, t);
      return points;
    }

    if (granularity === 'week') {
      // Semanas: agrupa por lunes de cada semana.
      const start = this.addDays(hoy, -29);
      for (let i = 0; i < 5; i++) {
        const ws = this.addDays(start, i * 7);
        const dt = new Date(ws + 'T00:00:00');
        const we = this.addDays(ws, 6);
        const weDt = new Date(we + 'T00:00:00');
        points.push({
          key: ws,
          label: `${dt.getDate()} ${meses[dt.getMonth()]}–${weDt.getDate()} ${meses[weDt.getMonth()]}`,
          income: 0,
          expense: 0,
        });
      }
      for (const t of this.chartTx()) {
        const k = points.find((p) => p.key <= t.date && t.date <= this.addDays(p.key, 6));
        if (k) {
          if (t.type === 'income') k.income += usdOf(t);
          else k.expense += usdOf(t);
        }
      }
      return points;
    }

    // Mensual (1Y / YTD): agrupa por YYYY-MM.
    const now = new Date();
    const monthsCount = this.chartRange() === '1y' ? 12 : now.getMonth() + 1;
    for (let i = monthsCount - 1; i >= 0; i--) {
      const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
      const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
      points.push({ key, label: meses[d.getMonth()], income: 0, expense: 0 });
    }
    for (const t of this.chartTx()) {
      accum(t.date.slice(0, 7), t);
    }
    return points;
  }

  /** Altura % de una barra respecto al máximo del gráfico. */
  barHeight(value: number, max: number): number {
    if (!max || value <= 0) return 0;
    return Math.max(4, Math.round((value / max) * 100));
  }

  /** Máximo común de ingresos/gastos para escalar el gráfico. */
  chartMax(): number {
    const max = Math.max(...this.chartData().map((x) => Math.max(x.income, x.expense)));
    return max > 0 ? max : 1;
  }

  /** true si no hay ningún movimiento en el rango. */
  chartEmpty(): boolean {
    return this.chartData().every((x) => x.income === 0 && x.expense === 0);
  }

  // ===== Iconos por categoría (círculos de transacciones) =====

  /** Icono temático para una categoría; por defecto según tipo. */
  txIcon(t: Transaction): string {
    const map: Record<string, string> = {
      comida: 'restaurant',
      restaurante: 'restaurant',
      supermercado: 'shopping_cart',
      mercado: 'shopping_cart',
      transporte: 'directions_bus',
      gasolina: 'local_gas_station',
      salud: 'favorite',
      farmacia: 'local_pharmacy',
      educacion: 'school',
      sueldo: 'work',
      salario: 'work',
      nomina: 'work',
      freelance: 'laptop',
      negocio: 'storefront',
      servicios: 'receipt',
      internet: 'wifi',
      telefono: 'phone',
      luz: 'bolt',
      agua: 'water_drop',
      renta: 'home',
      alquiler: 'home',
      entretenimiento: 'sports_esports',
      ropa: 'checkroom',
      viajes: 'flight',
      'seguro': 'shield',
    };
    const key = (t.category || '').toLowerCase().trim();
    if (map[key]) return map[key];
    return t.type === 'income' ? 'trending_up' : 'shopping_bag';
  }

  /** Fecha relativa corta (Hoy/Ayer/26-ago) + hora, estilo transacciones. */
  txCuando(t: Transaction): string {
    let dia = t.date || '';
    const hoy = this.today();
    const ayer = this.addDays(hoy, -1);
    if (t.date === hoy) dia = 'Hoy';
    else if (t.date === ayer) dia = 'Ayer';
    else {
      const [y, m, d] = (t.date || '').split('-');
      if (y && m && d) {
        const meses = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
        dia = `${Number(d)}-${meses[Number(m) - 1]}`;
      }
    }
    return `${dia} · ${t.time || ''}`;
  }

  txTipo(t: Transaction): string {
    return t.type === 'income' ? 'Ingreso' : 'Gasto';
  }

  /** Monto con signo + moneda; wallet en sublínea, estilo dashboard viejo. */
  txMonto(t: Transaction): string {
    const sign = t.type === 'income' ? '+' : '-';
    const num = Math.abs(t.amount).toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return `${sign}${num} ${t.walletCurrency ?? ''}`.trim();
  }

  private today(): string {
    return todayInTimeZone(this.timezone());
  }

  private addDays(dateStr: string, days: number): string {
    const d = new Date(dateStr + 'T00:00:00');
    d.setDate(d.getDate() + days);
    return d.toISOString().slice(0, 10);
  }
}
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

  /** Transacciones de los últimos 7 días (para el gráfico de rendimiento). */
  private chartTx = signal<Transaction[]>([]);

  /** Carga TODAS las transacciones de los últimos 7 días (no solo 5 recientes). */
  private loadChart(): void {
    const hoy = this.today();
    const from = this.addDays(hoy, -6);
    const to = this.addDays(hoy, 1); // exclusivo en backend: incluye todo el día de hoy
    this.api
      .transactions({ from, to, limit: 100 })
      .subscribe({
        next: (res) => this.chartTx.set(res.data ?? []),
        error: () => undefined,
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

  // ===== Gráfico de barras: Ingresos vs Gastos (7 días) =====

  /** Serie del gráfico: últimos 7 días con ingresos/gastos en USD. */
  chart7d(): { day: string; label: string; income: number; expense: number }[] {
    const days: { day: string; label: string; income: number; expense: number }[] = [];
    const hoy = this.today();
    for (let i = 6; i >= 0; i--) {
      const day = this.addDays(hoy, -i);
      const dt = new Date(day + 'T00:00:00');
      const meses = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
      const label = `${dt.getDate()} ${meses[dt.getMonth()]}`;
      days.push({ day, label, income: 0, expense: 0 });
    }
    // Acumula las transacciones reales de los últimos 7 días por día.
    for (const t of this.chartTx()) {
      const found = days.find((x) => x.day === t.date);
      if (!found) continue;
      // Convierte a USD si la billetera es VES (usa la tasa activa).
      const cur = (t.walletCurrency || '').toUpperCase();
      const r = this.rateNumber();
      const usd = cur === 'VES' && r ? t.amount / r : t.amount;
      if (t.type === 'income') found.income += usd;
      else found.expense += usd;
    }
    return days;
  }

  /** Altura % de una barra respecto al máximo del gráfico. */
  barHeight(value: number, max: number): number {
    if (!max || value <= 0) return 0;
    return Math.max(4, Math.round((value / max) * 100));
  }

  /** Máximo común de ingresos/gastos para escalar el gráfico. */
  chartMax(): number {
    const max = Math.max(...this.chart7d().map((x) => Math.max(x.income, x.expense)));
    return max > 0 ? max : 1;
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

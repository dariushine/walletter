import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { UiPreferenceStore } from '../../core/services/ui-preference.store';
import { ReportData } from '../../models/walletter.models';
import { formatNumber, formatWithCode } from '../../core/utils/money';

type RateType = 'bcv' | 'paralelo';
type PeriodId = '1m' | '3m' | '6m' | '1y' | 'all';

const PERIODS: { id: PeriodId; label: string }[] = [
  { id: '1m', label: 'Último mes' },
  { id: '3m', label: 'Últimos 3 meses' },
  { id: '6m', label: 'Últimos 6 meses' },
  { id: '1y', label: 'Último año' },
  { id: 'all', label: 'Todo' },
];

@Component({
  selector: 'app-reports',
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatButtonModule,
    MatSelectModule,
    MatFormFieldModule,
    MatInputModule,
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

  data = signal<ReportData | null>(null);
  loading = signal(true);

  rateType = signal<RateType>(this.prefs.rate('reports', 'bcv'));
  period = signal<PeriodId>(this.prefs.period('reports', '6m') as PeriodId);

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api
      .reports({
        period: this.period(),
        rate: this.rateType(),
        tz: this.settings.timezone(),
      })
      .subscribe({
        next: (r) => {
          this.data.set(r);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  setRate(rate: RateType): void {
    if (this.rateType() === rate) return;
    this.rateType.set(rate);
    this.prefs.setRate('reports', rate);
    this.load();
  }

  setPeriod(p: PeriodId): void {
    if (this.period() === p) return;
    this.period.set(p);
    this.prefs.setPeriod('reports', p);
    this.load();
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

  /** Nombre legible del mes YYYY-MM → 'ago 2026'. */
  monthLabel(month: string): string {
    const [y, m] = (month || '').split('-');
    if (!y || !m) return month;
    const meses = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
    return `${meses[Number(m) - 1]} ${y}`;
  }

  pct(n: number): string {
    return `${(n ?? 0).toLocaleString('es-VE', { maximumFractionDigits: 1 })}%`;
  }

  /** Variación porcentual entre dos netos mensuales. */
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
      tasa: this.rateType(),
      resumen: d.summary,
      categorias: d.byCategory,
      mensual: d.monthly,
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

  trackByIndex(i: number): number {
    return i;
  }
}

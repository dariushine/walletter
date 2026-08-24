import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { UiPreferenceStore } from '../../core/services/ui-preference.store';
import { Wallet, Stats, Transaction } from '../../models/walletter.models';
import { formatMoney, currencySymbol, currencyName, formatWithCode } from '../../core/utils/money';

@Component({
  selector: 'app-dashboard',
  imports: [
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatButtonModule,
    RouterLink,
  ],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.scss'],
})
export class Dashboard implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly prefs = inject(UiPreferenceStore);

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

  wallets = signal<Wallet[]>([]);
  stats = signal<Stats | null>(null);
  recent = signal<Transaction[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.loadRate();
    this.load();
  }

  private loadRate(): void {
    this.api.effectiveRate().subscribe({
      next: (e) => {
        this.bcv.set(e.vps?.bcv ?? null);
        this.paralelo.set(e.vps?.paralelo ?? null);
        this.rateDate.set(e.date ?? null);
        this.rate.set(e.vps?.bcv ?? e.vps?.paralelo ?? null);
      },
      error: () => undefined,
    });
  }

  private load(): void {
    this.loading.set(true);
    this.api.wallets().subscribe({
      next: (w) => this.wallets.set(w),
      error: () => this.loading.set(false),
    });
    this.api.stats().subscribe({
      next: (s) => this.stats.set(s),
      error: () => undefined,
    });
    this.api.transactions({ limit: 6 }).subscribe({
      next: (t) => this.recent.set(t.data),
      error: () => undefined,
    });
    // Marcar cargado cuando todas las peticiones resuelven o fallan.
    setTimeout(() => this.loading.set(false), 600);
  }

  format(amount: number, currency = 'USD'): string {
    return formatMoney(amount, currency, this.decimalSeparator());
  }

  /** Devuelve el monto de la billetera, o máscara, según preferencia/revelado. */
  saldo(w: Wallet): string {
    if (this.hideBalances() && !this.revealedIds().has(w.id)) return '••• ' + w.currency.toUpperCase();
    return formatWithCode(w.balance, w.currency, this.decimalSeparator());
  }

  /** Para valores agregados (stats): máscara simple si ocultar saldos. */
  saldoNum(amount: number, currency = 'USD'): string {
    if (this.hideBalances()) return '•••';
    return this.format(amount, currency);
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
}

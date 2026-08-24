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
import { formatMoney, currencySymbol } from '../../core/utils/money';

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

  wallets = signal<Wallet[]>([]);
  stats = signal<Stats | null>(null);
  recent = signal<Transaction[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.load();
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

  /** Devuelve el monto, o máscara, según la preferencia de ocultar saldos. */
  saldo(amount: number, currency = 'USD'): string {
    if (this.hideBalances()) return '•••';
    return this.format(amount, currency);
  }

  symbol(currency: string): string {
    return currencySymbol(currency);
  }
}

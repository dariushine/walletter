import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule, MatFabButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
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
    MatFabButton,
    MatProgressSpinnerModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatSelectModule,
    RouterLink,
    FormsModule,
  ],
  templateUrl: './transactions.html',
  styleUrls: ['./transactions.scss'],
})
export class Transactions implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly settings = inject(SettingsStore);

  readonly timezone = this.settings.timezone;

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

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }
}

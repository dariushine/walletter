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
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { Transaction, Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { TransactionDialog } from './transaction-dialog';

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
  walletFilter: number | null = null;
  tz = 'America/Caracas';

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
        walletId: this.walletFilter ?? undefined,
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

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.limit = e.pageSize;
    this.load();
  }

  onWalletChange(): void {
    this.page = 1;
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

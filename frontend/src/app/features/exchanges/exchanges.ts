import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Exchange, Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { ExchangeDialog } from './exchange-dialog';

@Component({
  selector: 'app-exchanges',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatPaginatorModule, RouterLink, DatePipe],
  templateUrl: './exchanges.html',
  styleUrls: ['./exchanges.scss'],
})
export class Exchanges implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(NotificationService);

  exchanges = signal<Exchange[]>([]);
  wallets = signal<Wallet[]>([]);
  total = signal(0);
  loading = signal(true);

  page = 1;
  limit = 20;

  ngOnInit(): void {
    this.api.wallets().subscribe((w) => this.wallets.set(w));
    this.load();
  }

  load(): void {
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

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.limit = e.pageSize;
    this.load();
  }

  openCreate(): void {
    const ref = this.dialog.open(ExchangeDialog, { width: '480px', data: { wallets: this.wallets() } });
    ref.afterClosed().subscribe((created) => {
      if (created) {
        this.notifier.success('Exchange registrado');
        this.load();
      }
    });
  }

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }
}

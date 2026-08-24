import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { UiPreferenceStore } from '../../core/services/ui-preference.store';
import { Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { WalletDialog } from './wallet-dialog';

@Component({
  selector: 'app-wallets',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, RouterLink],
  templateUrl: './wallets.html',
  styleUrls: ['./wallets.scss'],
})
export class Wallets implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(NotificationService);
  private readonly prefs = inject(UiPreferenceStore);

  readonly hideBalances = this.prefs.hideBalances;
  readonly decimalSeparator = this.prefs.decimalSeparator;

  wallets = signal<Wallet[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.wallets().subscribe({
      next: (w) => {
        this.wallets.set(w);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    const ref = this.dialog.open(WalletDialog, { width: '420px', data: {} });
    ref.afterClosed().subscribe((created?: Wallet) => {
      if (created) this.load();
    });
  }

  format(amount: number, currency: string): string {
    return formatMoney(amount, currency, this.decimalSeparator());
  }

  saldo(amount: number, currency: string): string {
    if (this.hideBalances()) return '•••';
    return this.format(amount, currency);
  }
}

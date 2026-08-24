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
import { formatMoney, currencyName } from '../../core/utils/money';
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

  /** Ids de billeteras cuyo saldo se reveló individualmente con el ojo. */
  private readonly revealedIds = signal<Set<number>>(new Set());

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

  saldo(w: Wallet): string {
    const hidden = this.hideBalances() && !this.revealedIds().has(w.id);
    if (hidden) return '•••';
    return this.format(w.balance, w.currency);
  }

  /** true si este saldo está oculto (no revelado). */
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
}

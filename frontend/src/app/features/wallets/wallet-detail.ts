import { Component, inject, input, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { WalletReport } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { formatInTimeZone } from '../../core/utils/dates';

@Component({
  selector: 'app-wallet-detail',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatListModule, MatProgressSpinnerModule, RouterLink],
  templateUrl: './wallet-detail.html',
  styleUrls: ['./wallet-detail.scss'],
})
export class WalletDetail implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly notifier = inject(NotificationService);
  private readonly router = inject(Router);

  readonly id = input.required<number>();
  readonly timezone = this.settings.timezone;

  report = signal<WalletReport | null>(null);
  loading = signal(true);

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.load();
  }

  load(): void {
    this.api.walletReport(this.id()).subscribe({
      next: (r) => {
        this.report.set(r);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  format(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  fmtDate(utc: string, tz: string | null | undefined): string {
    return formatInTimeZone(utc, tz || 'America/Caracas', 'yyyy-MM-dd');
  }

  back(): void {
    this.router.navigate(['/wallets']);
  }
}

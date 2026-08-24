import { Component, inject, input, OnInit, signal, computed } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { WalletReport } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { formatInTimeZone } from '../../core/utils/dates';
import { WalletDialog } from './wallet-dialog';

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
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  readonly id = input.required<number>();
  readonly timezone = this.settings.timezone;

  report = signal<WalletReport | null>(null);
  loading = signal(true);

  /** Resumen de ingresos/egresos/neto calculado desde el reporte. */
  readonly summary = computed(() => {
    const r = this.report();
    if (!r) return null;
    let income = 0;
    let expense = 0;
    for (const t of r.transactions) {
      if (t.type === 'income') income += t.amount - t.fee;
      else expense += t.amount;
    }
    return { income, expense, net: income - expense };
  });

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

  edit(): void {
    const w = this.report()?.wallet;
    if (!w) return;
    const ref = this.dialog.open(WalletDialog, { width: '420px', data: w });
    ref.afterClosed().subscribe((updated) => {
      if (updated) this.load();
    });
  }

  remove(): void {
    if (!confirm('¿Eliminar esta billetera?')) return;
    this.api.deleteWallet(this.id()).subscribe({
      next: () => {
        this.notifier.success('Billetera eliminada');
        this.router.navigate(['/wallets']);
      },
      error: () => undefined,
    });
  }
}

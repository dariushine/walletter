import { Component, inject, input, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { Router, RouterLink } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Exchange } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';

@Component({
  selector: 'app-exchange-detail',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatDividerModule, DatePipe],
  templateUrl: './exchange-detail.html',
  styleUrls: ['./exchange-detail.scss'],
})
export class ExchangeDetail implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly router = inject(Router);

  readonly id = input.required<number>();

  ex = signal<Exchange | null>(null);
  loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.exchange(this.id()).subscribe({
      next: (e) => {
        this.ex.set(e);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  delete(): void {
    if (!confirm('¿Eliminar este exchange?')) return;
    this.api.deleteExchange(this.id()).subscribe({
      next: () => {
        this.notifier.success('Exchange eliminado');
        this.router.navigate(['/exchanges']);
      },
      error: () => undefined,
    });
  }

  goBack(): void {
    this.router.navigate(['/exchanges']);
  }

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }
}

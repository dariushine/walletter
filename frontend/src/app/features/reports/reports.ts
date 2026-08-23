import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { CategoryStat, Stats } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';

@Component({
  selector: 'app-reports',
  imports: [MatCardModule, MatIconModule, MatProgressSpinnerModule, MatTableModule],
  templateUrl: './reports.html',
  styleUrls: ['./reports.scss'],
})
export class Reports implements OnInit {
  private readonly api = inject(WalletterApiService);

  stats = signal<Stats | null>(null);
  byCategory = signal<CategoryStat[]>([]);
  loading = signal(true);

  readonly columns = ['name', 'type', 'total'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.stats().subscribe({
      next: (s) => this.stats.set(s),
      error: () => undefined,
    });
    this.api.statsByCategory().subscribe({
      next: (c) => {
        this.byCategory.set(c);
        this.loading.set(false);
      },
      error: () => (this.loading.set(false)),
    });
  }

  format(amount: number): string {
    return formatMoney(amount);
  }
}

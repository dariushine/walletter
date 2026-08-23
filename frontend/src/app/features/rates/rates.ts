import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { DailyRate, EffectiveRate } from '../../models/walletter.models';

@Component({
  selector: 'app-rates',
  imports: [ReactiveFormsModule, MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatFormFieldModule, MatInputModule, MatTableModule],
  templateUrl: './rates.html',
  styleUrls: ['./rates.scss'],
})
export class Rates implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  effective = signal<EffectiveRate | null>(null);
  rates = signal<DailyRate[]>([]);
  loading = signal(true);

  readonly columns = ['date', 'bcv', 'paralelo', 'source', 'actions'];

  readonly form = this.fb.group({
    date: [new Date().toISOString().slice(0, 10), Validators.required],
    bcv: [0, [Validators.required, Validators.min(0.01)]],
    paralelo: [0, [Validators.required, Validators.min(0.01)]],
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.effectiveRate().subscribe({
      next: (e) => this.effective.set(e),
      error: () => undefined,
    });
    this.api.dailyRates().subscribe({
      next: (r) => {
        this.rates.set(r.data);
        this.loading.set(false);
      },
      error: () => (this.loading.set(false)),
    });
  }

  upsert(): void {
    if (this.form.invalid) return;
    const v = this.form.value;
    this.api
      .upsertDailyRate({ date: v.date!, bcv: Number(v.bcv) || 0, paralelo: Number(v.paralelo) || 0, source: 'manual' })
      .subscribe({
        next: () => {
          this.notifier.success('Tasa guardada');
          this.load();
        },
        error: () => undefined,
      });
  }

  remove(r: DailyRate): void {
    if (!confirm(`¿Eliminar la tasa del ${r.date}?`)) return;
    this.api.deleteDailyRate(r.id).subscribe({
      next: () => {
        this.notifier.success('Tasa eliminada');
        this.load();
      },
      error: () => undefined,
    });
  }

  fmt(n: number): string {
    return n?.toLocaleString('es-VE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) ?? '—';
  }
}

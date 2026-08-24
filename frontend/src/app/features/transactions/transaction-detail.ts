import { Component, inject, input, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';
import { TransactionDetail as TransactionDetailModel } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { todayInTimeZone } from '../../core/utils/dates';

@Component({
  selector: 'app-transaction-detail',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './transaction-detail.html',
  styleUrls: ['./transaction-detail.scss'],
})
export class TransactionDetail implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settings = inject(SettingsStore);
  private readonly notifier = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly id = input.required<number>();
  readonly timezone = this.settings.timezone;

  tx = signal<TransactionDetailModel | null>(null);
  loading = signal(true);
  editing = signal(false);
  tz = 'America/Caracas';
  busy = signal(false);

  readonly editForm = this.fb.group({
    description: [''],
    categoryName: [''],
    amount: [0],
    date: [''],
    time: [''],
  });

  ngOnInit(): void {
    this.settings.loadTimezone();
    this.tz = this.settings.timezone();
    this.load();
  }

  load(): void {
    this.api.transaction(this.id()).subscribe({
      next: (t) => {
        this.tx.set(t);
        this.editForm.patchValue({
          description: t.description || '',
          categoryName: t.category,
          amount: t.amount,
          date: t.date,
          time: t.time,
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  saveEdit(): void {
    this.busy.set(true);
    const v = this.editForm.value;
    this.api
      .updateTransaction(this.id(), {
        description: v.description || undefined,
        categoryName: v.categoryName || undefined,
        amount: Number(v.amount) || undefined,
        date: v.date || undefined,
        time: v.time || undefined,
        tz: this.tz,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.editing.set(false);
          this.notifier.success('Transacción actualizada');
          this.load();
        },
        error: () => this.busy.set(false),
      });
  }

  delete(): void {
    if (!confirm('¿Eliminar esta transacción?')) return;
    this.api.deleteTransaction(this.id()).subscribe({
      next: () => {
        this.notifier.success('Transacción eliminada');
        this.router.navigate(['/transactions']);
      },
      error: () => undefined,
    });
  }

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }

  /** Formatea YYYY-MM-DD a 'jueves, 20 de agosto de 2026'. */
  fmtFullDate(dateStr: string): string {
    if (!dateStr) return '—';
    const [y, m, d] = dateStr.split('-').map(Number);
    if (!y || !m || !d) return dateStr;
    return new Date(y, m - 1, d).toLocaleDateString('es-VE', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  }

  goBack(): void {
    this.router.navigate(['/transactions']);
  }
}

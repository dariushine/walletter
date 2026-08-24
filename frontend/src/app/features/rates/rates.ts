import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { DailyRate } from '../../models/walletter.models';

interface PeriodOption {
  value: string;
  label: string;
}

@Component({
  selector: 'app-rates',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    FormsModule,
  ],
  templateUrl: './rates.html',
  styleUrls: ['./rates.scss'],
})
export class Rates implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialog = inject(MatDialog);

  rates = signal<DailyRate[]>([]);
  total = signal(0);
  loading = signal(true);

  page = 1;
  limit = 20;

  /** Filtro de periodo. */
  selectedPeriod = 'all';
  private appliedFrom: string | undefined;
  private appliedTo: string | undefined;

  readonly periods: PeriodOption[] = [
    { value: 'all', label: 'Todo' },
    { value: 'today', label: 'Hoy' },
    { value: 'week', label: 'Esta semana' },
    { value: 'month', label: 'Este mes' },
    { value: 'year', label: 'Este año' },
  ];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.dailyRates().subscribe({
      next: (r) => {
        // Filtra por periodo en el frontend (el endpoint no acepta fechas).
        let data = r.data;
        if (this.appliedFrom || this.appliedTo) {
          data = data.filter((x) => {
            if (this.appliedFrom && x.date < this.appliedFrom) return false;
            if (this.appliedTo && x.date > this.appliedTo) return false;
            return true;
          });
        }
        this.rates.set(data);
        this.total.set(data.length);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  applyPeriod(): void {
    const today = this.today();
    const range = this.resolveRange(this.selectedPeriod, today);
    this.appliedFrom = range.from;
    this.appliedTo = range.to;
    this.page = 1;
    this.load();
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private addDays(dateStr: string, days: number): string {
    const d = new Date(dateStr + 'T00:00:00');
    d.setDate(d.getDate() + days);
    return d.toISOString().slice(0, 10);
  }

  private resolveRange(period: string, today: string): { from?: string; to?: string } {
    if (period === 'today') return { from: today, to: today };
    if (period === 'week') return { from: this.addDays(today, -6), to: today };
    if (period === 'month') return { from: today.slice(0, 8) + '01', to: today };
    if (period === 'year') return { from: today.slice(0, 4) + '-01-01', to: today };
    return {};
  }

  /** Devuelve las tasas de la página actual. */
  pagedRates(): DailyRate[] {
    const start = (this.page - 1) * this.limit;
    return this.rates().slice(start, start + this.limit);
  }

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.limit = e.pageSize;
  }

  openNew(): void {
    const ref = this.dialog.open(RateDialog, { width: '380px', data: null });
    ref.afterClosed().subscribe((saved) => {
      if (saved) this.load();
    });
  }

  edit(r: DailyRate): void {
    const ref = this.dialog.open(RateDialog, { width: '380px', data: r });
    ref.afterClosed().subscribe((saved) => {
      if (saved) this.load();
    });
  }

  syncToday(): void {
    // Consulta la tasa efectiva del día al backend (que a su vez la trae de
    // ve.dolarapi.com) y la persiste con los valores reales de hoy.
    this.api.effectiveRate().subscribe({
      next: (e) => {
        const bcv = e.vps?.bcv ?? 0;
        const paralelo = e.vps?.paralelo ?? 0;
        const dateStr = e.date ?? new Date().toISOString().slice(0, 10);
        this.api.upsertDailyRate({ date: dateStr, bcv, paralelo, source: 'dolarapi' }).subscribe({
          next: () => {
            this.notifier.success(`Tasa de hoy sincronizada (BCV ${bcv.toLocaleString('es-VE', { maximumFractionDigits: 2 })} / Paralelo ${paralelo.toLocaleString('es-VE', { maximumFractionDigits: 2 })})`);
            this.load();
          },
          error: () => this.notifier.error('Error al guardar la tasa de hoy'),
        });
      },
      error: () => this.notifier.error('No se pudo consultar la tasa del día'),
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

@Component({
  selector: 'app-rate-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './rate-dialog.html',
  styleUrls: ['./rates.scss'],
})
export class RateDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<RateDialog>);
  readonly data = inject<DailyRate | null>(MAT_DIALOG_DATA);

  loading = false;
  isEdit = !!this.data;

  readonly form = this.fb.group({
    date: [this.data?.date ?? new Date().toISOString().slice(0, 10), Validators.required],
    bcv: [this.data?.bcv ?? 0, [Validators.required, Validators.min(0)]],
    paralelo: [this.data?.paralelo ?? 0, [Validators.required, Validators.min(0)]],
    source: [this.data?.source ?? 'manual'],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const v = this.form.value;

    const request = this.isEdit
      ? this.api.updateDailyRate(this.data!.id, {
          bcv: Number(v.bcv) || 0,
          paralelo: Number(v.paralelo) || 0,
          source: v.source || undefined,
        })
      : this.api.upsertDailyRate({
          date: v.date!,
          bcv: Number(v.bcv) || 0,
          paralelo: Number(v.paralelo) || 0,
          source: v.source || 'manual',
        });

    request.subscribe({
      next: () => {
        this.loading = false;
        this.notifier.success(this.isEdit ? 'Tasa actualizada' : 'Tasa creada');
        this.dialogRef.close(true);
      },
      error: () => (this.loading = false),
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

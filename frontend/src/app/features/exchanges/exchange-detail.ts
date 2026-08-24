import { Component, inject, input, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ExchangeDetail as ExchangeDetailModel, ExchangeTransaction } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';

@Component({
  selector: 'app-exchange-detail',
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    MatMenuModule,
  ],
  templateUrl: './exchange-detail.html',
  styleUrls: ['./exchange-detail.scss'],
})
export class ExchangeDetail implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  readonly id = input.required<number>();

  ex = signal<ExchangeDetailModel | null>(null);
  loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.exchange(this.id()).subscribe({
      next: (e) => {
        this.ex.set({
          ...e,
          transactions: e.transactions ?? [],
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  delete(): void {
    const ref = this.dialog.open(ConfirmDeleteExchangeDialog);
    ref.afterClosed().subscribe((confirmed?: boolean) => {
      if (!confirmed) return;
      this.api.deleteExchange(this.id()).subscribe({
        next: () => {
          this.notifier.success('Exchange eliminado');
          this.router.navigate(['/exchanges']);
        },
        error: () => this.notifier.error('No se pudo eliminar el exchange'),
      });
    });
  }

  goToTransaction(id: number): void {
    this.router.navigate(['/transactions', id]);
  }

  /** Busca la transacción del débito (monto enviado). */
  debitTx(): ExchangeTransaction | undefined {
    const e = this.ex();
    return e?.transactions?.find((t) => t.id === e.debitTransactionId);
  }

  /** Busca la transacción del crédito (monto recibido). */
  creditTx(): ExchangeTransaction | undefined {
    const e = this.ex();
    return e?.transactions?.find((t) => t.id === e.creditTransactionId);
  }

  goBack(): void {
    this.router.navigate(['/exchanges']);
  }

  format(amount: number, currency?: string): string {
    return formatMoney(amount, currency || 'USD');
  }

  /** Formatea YYYY-MM-DD a 'sábado, 22 de agosto de 2026'. */
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

  /** Hora HH:MM desde ISO (YYYY-MM-DDTHH:MM:SS…). */
  hora(iso: string): string {
    if (!iso) return '—';
    return iso.slice(11, 16) || '—';
  }
}

/** Diálogo de confirmación para eliminar un exchange. */
@Component({
  selector: 'app-confirm-delete-exchange-dialog',
  imports: [MatDialogTitle, MatDialogContent, MatDialogActions, MatButtonModule],
  template: `
    <h2 mat-dialog-title>¿Eliminar exchange?</h2>
    <mat-dialog-content>
      <p>Esta operación eliminará el exchange y sus transacciones (débito, crédito y comisiones). ¿Confirma?</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="cancel()">Cancelar</button>
      <button mat-raised-button color="warn" type="button" (click)="confirm()">Eliminar</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDeleteExchangeDialog {
  private readonly dialogRef = inject(MatDialogRef<ConfirmDeleteExchangeDialog>);

  confirm(): void {
    this.dialogRef.close(true);
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

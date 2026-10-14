import { Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ImportResult, ImportRowResult, Wallet } from '../../models/walletter.models';

export interface ImportDialogData {
  /** Qué se importa: transacciones o exchanges. */
  mode: 'transactions' | 'exchanges';
  wallets: Wallet[];
  tz: string;
}

/** Carga por lote desde CSV: adjuntar archivo o pegar texto → previsualizar → importar. */
@Component({
  selector: 'app-import-dialog',
  imports: [
    MatDialogContent,
    MatDialogActions,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './import-dialog.html',
  styleUrls: ['./import-dialog.scss'],
})
export class ImportDialog {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<ImportDialog>);
  readonly data = inject<ImportDialogData>(MAT_DIALOG_DATA);

  readonly csv = signal('');
  readonly fileName = signal('');
  /** Preview (dryRun) con el resultado de cada fila. */
  readonly preview = signal<ImportResult | null>(null);
  /** Resultado de la importación real. */
  readonly result = signal<ImportResult | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  get isTransactions(): boolean {
    return this.data.mode === 'transactions';
  }

  title(): string {
    return this.isTransactions ? 'Cargar transacciones por lote' : 'Cargar exchanges por lote';
  }

  /** Plantilla CSV de cabeceras + una fila de ejemplo, según el modo. */
  template(): string {
    if (this.isTransactions) {
      return [
        'fecha,hora,tipo,billetera,categoria,monto,fee,descripcion',
        '2026-01-15,09:30,gasto,Efectivo,comida,25.50,,Almuerzo',
        '2026-01-15,14:00,ingreso,Banco,salario,1500,,Quincena',
      ].join('\n');
    }
    return [
      'fecha,hora,origen,destino,monto_enviado,monto_recibido,fee,credit_fee,descripcion',
      '2026-01-15,09:30,Banco,Efectivo,100,3800,5,0,Cambio del día',
    ].join('\n');
  }

  downloadTemplate(): void {
    const blob = new Blob(['\uFEFF' + this.template()], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = this.isTransactions ? 'plantilla_transacciones.csv' : 'plantilla_exchanges.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.csv.set(String(reader.result ?? ''));
      this.fileName.set(file.name);
      this.reset();
    };
    reader.readAsText(file, 'utf-8');
    input.value = '';
  }

  onPaste(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.csv.set(value);
    if (value.trim().length > 0) this.fileName.set('');
    this.reset();
  }

  /** Botón "Descargar plantilla" también limpia errores si hay texto pegado. */
  private reset(): void {
    this.preview.set(null);
    this.result.set(null);
    this.error.set(null);
  }

  /** Valida y muestra el preview sin escribir nada (dryRun). */
  validate(): void {
    if (!this.csv().trim()) {
      this.notifier.error('Adjunta un CSV o pega su contenido');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.result.set(null);
    this.send(true).subscribe({
      next: (res) => {
        this.preview.set(res);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(this.message(err));
        this.loading.set(false);
      },
    });
  }

  /** Importa de verdad las filas válidas. */
  confirm(): void {
    const p = this.preview();
    if (!p) return;
    if (p.imported === 0) {
      this.notifier.error('No hay filas válidas para importar');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.send(false).subscribe({
      next: (res) => {
        this.result.set(res);
        this.preview.set(null);
        this.loading.set(false);
        if (res.failed === 0) {
          this.notifier.success(`${res.imported} ${this.isTransactions ? 'transacciones' : 'exchanges'} importados`);
        } else {
          this.notifier.error(`${res.imported} importados, ${res.failed} con error`);
        }
      },
      error: (err) => {
        this.error.set(this.message(err));
        this.loading.set(false);
      },
    });
  }

  private send(dryRun: boolean) {
    const body = { csv: this.csv(), tz: this.data.tz, dryRun };
    return this.isTransactions ? this.api.importTransactions(body) : this.api.importExchanges(body);
  }

  /** Resumen legible de una fila (preview o resultado). */
  rowSummary(row: ImportRowResult): string {
    if (!row.ok) return row.error ?? 'Error';
    const d = row.data ?? {};
    if (this.isTransactions) {
      const sign = d.type === 'income' ? '+' : '-';
      return `${sign}${d.amount} ${d.wallet ?? ''} · ${d.category ?? ''} · ${d.date ?? ''} ${d.time ?? ''}`.trim();
    }
    return `${d.fromWallet} → ${d.toWallet} · ${d.fromAmount} → ${d.toAmount} · ${d.date ?? ''} ${d.time ?? ''}`.trim();
  }

  close(): void {
    this.dialogRef.close(this.result() != null);
  }

  private message(err: any): string {
    return err?.error?.error ?? err?.error?.title ?? err?.message ?? 'Error al procesar el CSV';
  }
}

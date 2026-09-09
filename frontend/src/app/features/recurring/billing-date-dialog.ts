import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { formatInTimeZone } from '../../core/utils/dates';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';

export interface BillingDateDialogData {
  name: string;
  date: string;
  tz: string;
}

@Component({
  selector: 'app-billing-date-dialog',
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule,
    MatDialogTitle,
    MatDialogContent,
    MatDialogActions,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatDatepickerModule,
  ],
  template: `
    <h2 mat-dialog-title>Corregir ciclo cubierto</h2>
    <mat-dialog-content>
      <p>Selecciona la fecha de facturación que cubrió el pago de «{{ data.name }}».</p>
      <mat-form-field appearance="outline">
        <mat-label>Fecha de facturación</mat-label>
        <input matInput [matDatepicker]="picker" [formControl]="date" />
        <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
        <mat-datepicker #picker></mat-datepicker>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="close()">Cancelar</button>
      <button mat-flat-button color="primary" [disabled]="date.invalid" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
})
export class BillingDateDialog {
  readonly data = inject<BillingDateDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<BillingDateDialog>);
  date = new FormControl<string>(this.data.date, [Validators.required]);

  close(): void { this.ref.close(); }
  save(): void {
    if (this.date.valid && this.date.value) {
      // Normalizar a yyyy-MM-dd en la zona del usuario (el datepicker entrega
      // un Date que se serializa como ISO UTC y el backend lo rechaza).
      this.ref.close(normalizeBillingDate(this.date.value, this.data.tz));
    }
  }
}

/** Convierte el valor del datepicker a yyyy-MM-dd en la zona indicada. */
function normalizeBillingDate(value: string | Date | null, tz: string): string | null {
  if (!value) return null;
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return null;
  return formatInTimeZone(d, tz, 'yyyy-MM-dd');
}

import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
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
    if (this.date.valid && this.date.value) this.ref.close(this.date.value);
  }
}

import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface BillingDateDialogData {
  name: string;
  date: string;
}

@Component({
  selector: 'app-billing-date-dialog',
  imports: [FormsModule, MatDialogContent, MatDialogActions, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Corregir ciclo cubierto</h2>
    <mat-dialog-content>
      <p>Selecciona la fecha de facturación que cubrió el pago de «{{ data.name }}».</p>
      <mat-form-field appearance="outline">
        <mat-label>Fecha de facturación</mat-label>
        <input matInput type="date" [(ngModel)]="date" />
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="close()">Cancelar</button>
      <button mat-flat-button color="primary" [disabled]="!date" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
})
export class BillingDateDialog {
  readonly data = inject<BillingDateDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<BillingDateDialog>);
  date = this.data.date;

  close(): void { this.ref.close(); }
  save(): void { if (this.date) this.ref.close(this.date); }
}

import { Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/** Notificaciones tipo toast reutilizables en toda la app. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  constructor(private readonly snackbar: MatSnackBar) {}

  success(message: string): void {
    this.snackbar.open(message, 'Cerrar', {
      duration: 3500,
      panelClass: 'snack-success',
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    });
  }

  error(message: string): void {
    this.snackbar.open(message, 'Cerrar', {
      duration: 5000,
      panelClass: 'snack-error',
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    });
  }

  info(message: string): void {
    this.snackbar.open(message, 'Cerrar', {
      duration: 3000,
      panelClass: 'snack-info',
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    });
  }
}

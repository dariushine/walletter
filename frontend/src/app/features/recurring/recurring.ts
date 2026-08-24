import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { RecurringPayment, Wallet } from '../../models/walletter.models';
import { formatMoney } from '../../core/utils/money';
import { RecurringDialog, RecurringExecuteDialog } from './recurring-dialog';

@Component({
  selector: 'app-recurring',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './recurring.html',
  styleUrls: ['./recurring.scss'],
})
export class Recurring implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(NotificationService);

  items = signal<RecurringPayment[]>([]);
  wallets = signal<Wallet[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.api.wallets().subscribe((w) => this.wallets.set(w));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.recurringPayments().subscribe({
      next: (r) => {
        this.items.set(r);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    const ref = this.dialog.open(RecurringDialog, { width: '440px', data: { wallets: this.wallets() } });
    ref.afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  edit(item: RecurringPayment): void {
    const ref = this.dialog.open(RecurringDialog, { width: '440px', data: { wallets: this.wallets(), item } });
    ref.afterClosed().subscribe((updated) => {
      if (updated) this.load();
    });
  }

  execute(item: RecurringPayment): void {
    const ref = this.dialog.open(RecurringExecuteDialog, { width: '420px', data: { item, wallets: this.wallets() } });
    ref.afterClosed().subscribe((ok) => {
      if (ok) this.notifier.success('Pago recurrente ejecutado');
    });
  }

  delete(item: RecurringPayment): void {
    if (!confirm(`¿Eliminar "${item.name}"?`)) return;
    this.api.deleteRecurringPayment(item.id).subscribe({
      next: () => {
        this.notifier.success('Pago recurrente eliminado');
        this.load();
      },
      error: () => undefined,
    });
  }

  format(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }
}

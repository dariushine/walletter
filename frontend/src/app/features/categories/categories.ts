import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions } from '@angular/material/dialog';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Category } from '../../models/walletter.models';

@Component({
  selector: 'app-categories',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatRadioModule],
  templateUrl: './categories.html',
  styleUrls: ['./categories.scss'],
})
export class Categories implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);

  categories = signal<Category[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.categories().subscribe({
      next: (c) => {
        this.categories.set(c);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    const ref = this.dialog.open(CategoryCreateDialog, { width: '360px' });
    ref.afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  delete(c: Category): void {
    if (!confirm(`¿Desactivar la categoría "${c.name}"?`)) return;
    this.api.deleteCategory(c.id).subscribe({
      next: () => {
        this.notifier.success('Categoría desactivada');
        this.load();
      },
      error: () => undefined,
    });
  }
}

@Component({
  selector: 'app-category-create-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatFormFieldModule, MatInputModule, MatRadioModule, MatButtonModule],
  templateUrl: './category-dialog.html',
  styleUrls: ['./categories.scss'],
})
export class CategoryCreateDialog {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialogRef = inject(MatDialogRef<CategoryCreateDialog>);

  loading = false;

  readonly form = this.fb.group({
    name: ['', Validators.required],
    type: ['expense', Validators.required],
    color: ['#e74c3c'],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const v = this.form.value;
    this.api
      .createCategory({ name: v.name!, type: v.type as 'income' | 'expense', color: v.color || undefined })
      .subscribe({
        next: (c) => {
          this.loading = false;
          this.notifier.success('Categoría creada');
          this.dialogRef.close(c);
        },
        error: () => (this.loading = false),
      });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

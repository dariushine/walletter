import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { Category } from '../../models/walletter.models';

/** Categorías de sistema que no se muestran ni se editan/eliminan. */
const SYSTEM_HIDDEN = ['exchange_out', 'exchange_in'];
const SYSTEM_PROTECTED = ['fee'];

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

  /** Filtro activo: 'all' | 'expense' | 'income'. */
  filterType = signal<'all' | 'expense' | 'income'>('all');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.categories().subscribe({
      next: (c) => {
        // Oculta las categorías de sistema exchange_in/exchange_out.
        this.categories.set(c.filter((cat) => !SYSTEM_HIDDEN.includes(cat.name)));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  setFilter(t: 'all' | 'expense' | 'income'): void {
    this.filterType.set(t);
  }

  currentFiltered(): Category[] {
    const all = this.categories();
    const f = this.filterType();
    if (f === 'all') return all;
    return all.filter((c) => c.type === f);
  }

  isProtected(c: Category): boolean {
    return SYSTEM_PROTECTED.includes(c.name);
  }

  openCreate(): void {
    const ref = this.dialog.open(CategoryCreateDialog, {
      width: '360px',
      data: { type: this.filterType() === 'all' ? 'expense' : this.filterType() },
    });
    ref.afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  edit(c: Category): void {
    const ref = this.dialog.open(CategoryCreateDialog, { width: '360px', data: { category: c } });
    ref.afterClosed().subscribe((updated) => {
      if (updated) this.load();
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
  private readonly dialogData = inject<{ category?: Category; type?: 'expense' | 'income' }>(MAT_DIALOG_DATA);

  loading = false;
  isEdit = !!this.dialogData?.category;

  readonly form = this.fb.group({
    name: [this.dialogData?.category?.name ?? '', Validators.required],
    type: [this.dialogData?.category?.type ?? this.dialogData?.type ?? 'expense', Validators.required],
    color: [this.dialogData?.category?.color ?? '#e74c3c'],
  });

  save(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const v = this.form.value;

    const request = this.isEdit
      ? this.api.updateCategory(this.dialogData!.category!.id, { name: v.name!, color: v.color || undefined })
      : this.api.createCategory({ name: v.name!, type: v.type as 'income' | 'expense', color: v.color || undefined });

    request.subscribe({
      next: (c) => {
        this.loading = false;
        this.notifier.success(this.isEdit ? 'Categoría actualizada' : 'Categoría creada');
        this.dialogRef.close(c);
      },
      error: () => (this.loading = false),
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { WalletterApiService } from '../core/services/walletter-api.service';
import { AuthStore } from '../core/services/auth-store';
import { NotificationService } from '../core/services/notification.service';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    MatCard,
    MatCardContent,
    MatCardTitle,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
  ],
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(WalletterApiService);
  private readonly auth = inject(AuthStore);
  private readonly notifier = inject(NotificationService);
  private readonly router = inject(Router);

  loading = false;
  hidePassword = true;

  readonly form = this.fb.group({
    username: ['', Validators.required],
    password: ['', Validators.required],
    remember: [false],
  });

  submit(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const { username, password, remember } = this.form.value;
    this.api.login(username!, password!, remember ?? false).subscribe({
      next: () => {
        this.auth.setAuthenticated(true);
        this.loading = false;
        this.notifier.success('Sesión iniciada');
        this.router.navigate(['/dashboard']);
      },
      error: (e) => {
        this.loading = false;
        this.notifier.error(e?.error?.error || 'Credenciales inválidas');
        this.auth.setAuthenticated(false);
      },
    });
  }
}

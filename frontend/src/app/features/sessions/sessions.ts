import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDividerModule } from '@angular/material/divider';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ApiToken, SessionInfo } from '../../models/walletter.models';

@Component({
  selector: 'app-sessions',
  imports: [ReactiveFormsModule, MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatListModule, MatFormFieldModule, MatInputModule, MatDividerModule],
  templateUrl: './sessions.html',
  styleUrls: ['./sessions.scss'],
})
export class Sessions implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  sessions = signal<SessionInfo[]>([]);
  tokens = signal<ApiToken[]>([]);
  loading = signal(true);
  newToken = signal<string | null>(null);

  readonly tokenForm = this.fb.group({
    name: ['', Validators.required],
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.sessions().subscribe({
      next: (s) => this.sessions.set(s),
      error: () => undefined,
    });
    this.api.apiTokens().subscribe({
      next: (t) => {
        this.tokens.set(t);
        this.loading.set(false);
      },
      error: () => (this.loading.set(false)),
    });
  }

  revokeSession(jti: string): void {
    this.api.revokeSession(jti).subscribe({
      next: () => {
        this.notifier.success('Sesión revocada');
        this.load();
      },
      error: () => undefined,
    });
  }

  createToken(): void {
    if (this.tokenForm.invalid) return;
    this.api.createApiToken(this.tokenForm.value.name!).subscribe({
      next: (res) => {
        this.newToken.set(res.token);
        this.tokenForm.reset();
        this.load();
      },
      error: () => undefined,
    });
  }

  revokeToken(id: number): void {
    this.api.revokeApiToken(id).subscribe({
      next: () => {
        this.notifier.success('Token revocado');
        this.load();
      },
      error: () => undefined,
    });
  }

  fmtTs(ms: number): string {
    return new Date(ms).toLocaleString('es-VE');
  }
}

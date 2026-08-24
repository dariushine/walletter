import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatInputModule } from '@angular/material/input';
import { MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ApiToken, SessionInfo } from '../../models/walletter.models';

@Component({
  selector: 'app-sessions',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatInputModule],
  templateUrl: './sessions.html',
  styleUrls: ['./sessions.scss'],
})
export class Sessions implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly notifier = inject(NotificationService);
  private readonly dialog = inject(MatDialog);

  sessions = signal<SessionInfo[]>([]);
  tokens = signal<ApiToken[]>([]);
  loading = signal(true);

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
      error: () => this.loading.set(false),
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

  /** Abre un dialogo para ingresar el nombre del token y crearlo. */
  createToken(): void {
    const ref = this.dialog.open(TokenNameDialog, { width: '380px' });
    ref.afterClosed().subscribe((name?: string) => {
      if (!name) return;
      this.api.createApiToken(name).subscribe({
        next: (res) => {
          this.load();
          // Muestra el token generado en un modal con aviso de copiarlo.
          this.dialog.open(TokenCreatedDialog, { width: '440px', data: res.token });
        },
        error: () => this.notifier.error('No se pudo crear el token'),
      });
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

  fmtDateTime(ms: number): string {
    return new Date(ms).toLocaleString('es-VE', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  /** Icono según el dispositivo de la sesión. */
  deviceIcon(info: SessionInfo): string {
    const name = (info.deviceName || '').toLowerCase();
    if (name.includes('windows')) return 'desktop_windows';
    if (name.includes('android')) return 'smartphone';
    if (name.includes('iphone') || name.includes('ipad') || name.includes('ios')) return 'phone_iphone';
    if (name.includes('mac')) return 'desktop_mac';
    if (name.includes('linux')) return 'laptop';
    return 'devices';
  }
}

/** Dialogo para ingresar el nombre del nuevo API token. */
@Component({
  selector: 'app-token-name-dialog',
  imports: [ReactiveFormsModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Nuevo token</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Nombre del token *</mat-label>
        <input matInput formControlName="name" placeholder="Ej. Openclaw" autocomplete="off" />
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="cancel()">Cancelar</button>
      <button mat-raised-button color="primary" (click)="accept()" [disabled]="form.invalid">Aceptar</button>
    </mat-dialog-actions>
  `,
})
export class TokenNameDialog {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<TokenNameDialog>);

  readonly form = this.fb.group({
    name: ['', Validators.required],
  });

  accept(): void {
    if (this.form.invalid) return;
    this.dialogRef.close(this.form.value.name);
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

/** Modal que muestra el token generado y avisa que se debe copiar. */
@Component({
  selector: 'app-token-created-dialog',
  imports: [MatDialogTitle, MatDialogContent, MatDialogActions, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Token creado</h2>
    <mat-dialog-content>
      <p class="token-warning">Copia este token ahora, no se mostrará más nunca:</p>
      <code class="token-value">{{ token }}</code>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-raised-button color="primary" (click)="copy()">Copiar</button>
      <button mat-button (click)="close()">Cerrar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full { width: 100%; }
    .token-warning { color: rgba(0,0,0,0.7); }
    .token-value {
      display: block;
      word-break: break-all;
      background: #fffde7;
      border: 1px solid #fbc02d;
      padding: 10px;
      border-radius: 6px;
      margin-top: 8px;
    }
  `],
})
export class TokenCreatedDialog {
  readonly token = inject<string>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<TokenCreatedDialog>);
  private readonly notifier = inject(NotificationService);

  copy(): void {
    navigator.clipboard?.writeText(this.token).then(
      () => this.notifier.success('Token copiado'),
      () => this.notifier.error('No se pudo copiar')
    );
  }

  close(): void {
    this.dialogRef.close();
  }
}

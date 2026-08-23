import { Component, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { WalletterApiService } from '../../core/services/walletter-api.service';
import { SettingsStore } from '../../core/services/settings-store';
import { NotificationService } from '../../core/services/notification.service';

const TIMEZONES = [
  'America/Caracas',
  'America/Bogota',
  'America/Lima',
  'America/Mexico_City',
  'America/Argentina/Buenos_Aires',
  'America/Santiago',
  'America/Sao_Paulo',
  'America/New_York',
  'America/Los_Angeles',
  'Europe/Madrid',
  'Europe/London',
  'UTC',
];

@Component({
  selector: 'app-settings',
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatProgressSpinnerModule, FormsModule],
  templateUrl: './settings.html',
  styleUrls: ['./settings.scss'],
})
export class Settings implements OnInit {
  private readonly api = inject(WalletterApiService);
  private readonly settingsStore = inject(SettingsStore);
  private readonly notifier = inject(NotificationService);

  readonly timezones = TIMEZONES;
  timezone = 'America/Caracas';
  loading = signal(true);
  saving = signal(false);

  ngOnInit(): void {
    this.settingsStore.loadTimezone();
    this.timezone = this.settingsStore.timezone();
    this.loading.set(false);
  }

  save(): void {
    this.saving.set(true);
    this.settingsStore.setTimezone(this.timezone).subscribe({
      next: () => {
        this.saving.set(false);
        this.notifier.success('Zona horaria guardada');
      },
      error: () => this.saving.set(false),
    });
  }
}

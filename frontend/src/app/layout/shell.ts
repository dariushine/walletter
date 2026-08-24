import { Component, signal, ViewChild } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { MatDrawer, MatDrawerContainer, MatDrawerContent } from '@angular/material/sidenav';
import { MatIcon } from '@angular/material/icon';
import { MatButton, MatIconButton, MatFabButton } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatTooltip } from '@angular/material/tooltip';
import { BreakpointObserver } from '@angular/cdk/layout';
import { AsyncPipe } from '@angular/common';
import { Observable } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { AuthStore } from '../core/services/auth-store';
import { WalletterApiService } from '../core/services/walletter-api.service';
import { SettingsStore } from '../core/services/settings-store';
import { Wallet } from '../models/walletter.models';
import { NewOperationDialog } from './new-operation-dialog';

interface NavItem {
  routerLink: string;
  icon: string;
  label: string;
}

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatDrawer,
    MatDrawerContainer,
    MatDrawerContent,
    MatIcon,
    MatButton,
    MatIconButton,
    MatFabButton,
    MatListModule,
    MatTooltip,
    AsyncPipe,
  ],
  templateUrl: './shell.html',
  styleUrls: ['./shell.scss'],
})
export class Shell {
  @ViewChild('drawer') drawer!: MatDrawer;

  readonly navItems: NavItem[] = [
    { routerLink: '/dashboard', icon: 'dashboard', label: 'Dashboard' },
    { routerLink: '/wallets', icon: 'account_balance_wallet', label: 'Billeteras' },
    { routerLink: '/transactions', icon: 'receipt_long', label: 'Transacciones' },
    { routerLink: '/exchanges', icon: 'currency_exchange', label: 'Exchanges' },
    { routerLink: '/categories', icon: 'category', label: 'Categorías' },
    { routerLink: '/recurring', icon: 'repeat', label: 'Pagos recurrentes' },
    { routerLink: '/rates', icon: 'trending_up', label: 'Tasas' },
    { routerLink: '/reports', icon: 'bar_chart', label: 'Reportes' },
    { routerLink: '/settings', icon: 'settings', label: 'Ajustes' },
    { routerLink: '/sessions', icon: 'devices', label: 'Sesiones' },
  ];

  /** Signal que expone si la API exige autenticación. */
  get authEnabled(): Observable<boolean> {
    return this.authStore.authEnabled;
  }

  /** Opciones principales de la barra inferior (móvil). */
  readonly bottomNav: NavItem[] = this.navItems.slice(0, 4);

  /** Opciones secundarias del menú lateral (sección MÁS). */
  readonly moreNav: NavItem[] = this.navItems.slice(4);

  readonly isHandset = signal<boolean>(false);
  /** Menú colapsado (solo iconos) en escritorio. */
  readonly collapsed = signal<boolean>(false);

  constructor(
    private readonly authStore: AuthStore,
    private readonly router: Router,
    private readonly api: WalletterApiService,
    private readonly settings: SettingsStore,
    private readonly dialog: MatDialog,
    breakpointObserver: BreakpointObserver
  ) {
    breakpointObserver.observe('(max-width: 900px)').subscribe((state) => {
      this.isHandset.set(state.matches);
      // Al pasar a escritorio, restablecer a expandido.
      if (!state.matches) this.collapsed.set(false);
    });
  }

  logout(): void {
    this.router.navigate(['/login']);
  }

  toggleDrawer(): void {
    this.drawer?.toggle();
  }

  toggleCollapsed(): void {
    this.collapsed.set(!this.collapsed());
  }

  /** Carga las billeteras y abre el modal global 'Nueva operación'. */
  openNewOperation(): void {
    this.settings.loadTimezone();
    this.api.wallets().subscribe({
      next: (wallets: Wallet[]) => {
        this.dialog.open(NewOperationDialog, {
          width: '520px',
          maxWidth: '95vw',
          data: { wallets, tz: this.settings.timezone() },
        });
      },
      error: () => undefined,
    });
  }
}

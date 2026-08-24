import { Component, signal } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { MatDrawer, MatDrawerContainer, MatDrawerContent } from '@angular/material/sidenav';
import { MatToolbar } from '@angular/material/toolbar';
import { MatIcon } from '@angular/material/icon';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatTooltip } from '@angular/material/tooltip';
import { BreakpointObserver } from '@angular/cdk/layout';
import { AsyncPipe } from '@angular/common';
import { Observable } from 'rxjs';
import { AuthStore } from '../core/services/auth-store';

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
    MatToolbar,
    MatIcon,
    MatButton,
    MatIconButton,
    MatListModule,
    MatTooltip,
    AsyncPipe,
  ],
  templateUrl: './shell.html',
  styleUrls: ['./shell.scss'],
})
export class Shell {
  drawer!: MatDrawer;

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

  readonly isHandset = signal<boolean>(false);
  /** Menú colapsado (solo iconos) en escritorio. */
  readonly collapsed = signal<boolean>(false);

  constructor(
    private readonly authStore: AuthStore,
    private readonly router: Router,
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
}

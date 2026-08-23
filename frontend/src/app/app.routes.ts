import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { Shell } from './layout/shell';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./auth/login').then((m) => m.Login) },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard) },
      { path: 'wallets', loadComponent: () => import('./features/wallets/wallets').then((m) => m.Wallets) },
      { path: 'wallets/:id', loadComponent: () => import('./features/wallets/wallet-detail').then((m) => m.WalletDetail) },
      { path: 'transactions', loadComponent: () => import('./features/transactions/transactions').then((m) => m.Transactions) },
      { path: 'transactions/:id', loadComponent: () => import('./features/transactions/transaction-detail').then((m) => m.TransactionDetail) },
      { path: 'exchanges', loadComponent: () => import('./features/exchanges/exchanges').then((m) => m.Exchanges) },
      { path: 'exchanges/:id', loadComponent: () => import('./features/exchanges/exchange-detail').then((m) => m.ExchangeDetail) },
      { path: 'categories', loadComponent: () => import('./features/categories/categories').then((m) => m.Categories) },
      { path: 'recurring', loadComponent: () => import('./features/recurring/recurring').then((m) => m.Recurring) },
      { path: 'rates', loadComponent: () => import('./features/rates/rates').then((m) => m.Rates) },
      { path: 'reports', loadComponent: () => import('./features/reports/reports').then((m) => m.Reports) },
      { path: 'sessions', loadComponent: () => import('./features/sessions/sessions').then((m) => m.Sessions) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings').then((m) => m.Settings) },
    ],
  },
  { path: '**', redirectTo: '/dashboard' },
];

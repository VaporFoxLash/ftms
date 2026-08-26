import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

/**
 * design: doc 07 section 5 - standalone components with lazy loaded routes so the first bundle
 * stays small. Every feature route below is a dynamic import, which means the transactions
 * screens are not downloaded by someone who only ever reaches the login page.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'transactions',
  },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.authRoutes),
  },
  {
    path: 'transactions',
    canActivate: [authGuard],
    loadChildren: () =>
      import('./features/transactions/transactions.routes').then((m) => m.transactionRoutes),
  },
  {
    path: '**',
    redirectTo: 'transactions',
  },
];

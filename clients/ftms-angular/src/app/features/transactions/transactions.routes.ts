import { Routes } from '@angular/router';

/**
 * 'new' is declared before ':id' on purpose. Angular matches routes in order, so the reverse
 * would send /transactions/new to the detail page with an id of "new".
 */
export const transactionRoutes: Routes = [
  {
    path: '',
    title: 'Transactions | FTMS',
    loadComponent: () => import('./list/transaction-list').then((m) => m.TransactionList),
  },
  {
    path: 'new',
    title: 'Capture transaction | FTMS',
    loadComponent: () => import('./form/transaction-form').then((m) => m.TransactionForm),
  },
  {
    path: ':id/edit',
    title: 'Edit transaction | FTMS',
    loadComponent: () => import('./form/transaction-form').then((m) => m.TransactionForm),
  },
  {
    path: ':id',
    title: 'Transaction | FTMS',
    loadComponent: () => import('./detail/transaction-detail').then((m) => m.TransactionDetail),
  },
];

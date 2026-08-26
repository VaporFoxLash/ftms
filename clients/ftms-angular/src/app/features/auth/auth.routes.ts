import { Routes } from '@angular/router';

export const authRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'login',
  },
  {
    path: 'login',
    title: 'Sign in | FTMS',
    loadComponent: () => import('./login/login').then((m) => m.Login),
  },
];

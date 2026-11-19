import { Routes } from '@angular/router';
import { adminGuard, userGuard } from './core/guards';

// Screens are loaded when first visited, which keeps the first download small.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'rules' },
  { path: 'login', loadComponent: () => import('./features/auth/login').then((m) => m.Login) },
  { path: 'admin/login', loadComponent: () => import('./features/auth/admin-login').then((m) => m.AdminLogin) },

  { path: 'rules', canActivate: [userGuard], loadComponent: () => import('./features/rules/rules').then((m) => m.Rules) },
  { path: 'rules/new', canActivate: [userGuard], loadComponent: () => import('./features/rules/rule-editor').then((m) => m.RuleEditor) },
  { path: 'rules/:id', canActivate: [userGuard], loadComponent: () => import('./features/rules/rule-editor').then((m) => m.RuleEditor) },
  { path: 'channels', canActivate: [userGuard], loadComponent: () => import('./features/channels/channels').then((m) => m.Channels) },
  { path: 'history', canActivate: [userGuard], loadComponent: () => import('./features/history/history').then((m) => m.History) },

  { path: 'admin', pathMatch: 'full', canActivate: [adminGuard], loadComponent: () => import('./features/admin/overview').then((m) => m.AdminOverview) },
  { path: 'admin/notifications', canActivate: [adminGuard], loadComponent: () => import('./features/admin/notifications').then((m) => m.AdminNotifications) },
  { path: 'admin/events', canActivate: [adminGuard], loadComponent: () => import('./features/admin/events').then((m) => m.AdminEvents) },
  { path: 'admin/sources', canActivate: [adminGuard], loadComponent: () => import('./features/admin/sources').then((m) => m.AdminSources) },
  { path: 'admin/channel-types', canActivate: [adminGuard], loadComponent: () => import('./features/admin/channel-types').then((m) => m.AdminChannelTypes) },

  { path: '**', redirectTo: 'rules' },
];

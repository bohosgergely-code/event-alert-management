import { Routes } from '@angular/router';
import { adminGuard, userGuard } from './core/guards';

// Screens are loaded when first visited, which keeps the first download small.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'rules' },
  { path: 'login', title: 'Sign in · EventAlert', loadComponent: () => import('./features/auth/login').then((m) => m.Login) },
  { path: 'admin/login', title: 'Administrator sign-in · EventAlert', loadComponent: () => import('./features/auth/admin-login').then((m) => m.AdminLogin) },

  { path: 'rules', title: 'Rules · EventAlert', canActivate: [userGuard], loadComponent: () => import('./features/rules/rules').then((m) => m.Rules) },
  { path: 'rules/new', title: 'New rule · EventAlert', canActivate: [userGuard], loadComponent: () => import('./features/rules/rule-editor').then((m) => m.RuleEditor) },
  { path: 'rules/:id', title: 'Edit rule · EventAlert', canActivate: [userGuard], loadComponent: () => import('./features/rules/rule-editor').then((m) => m.RuleEditor) },
  { path: 'channels', title: 'Channels · EventAlert', canActivate: [userGuard], loadComponent: () => import('./features/channels/channels').then((m) => m.Channels) },
  { path: 'history', title: 'History · EventAlert', canActivate: [userGuard], loadComponent: () => import('./features/history/history').then((m) => m.History) },

  { path: 'admin', pathMatch: 'full', title: 'Overview · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/overview').then((m) => m.AdminOverview) },
  { path: 'admin/notifications', title: 'Deliveries · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/notifications').then((m) => m.AdminNotifications) },
  { path: 'admin/events', title: 'Events · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/events').then((m) => m.AdminEvents) },
  { path: 'admin/sources', title: 'Sources · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/sources').then((m) => m.AdminSources) },
  { path: 'admin/channel-types', title: 'Channel types · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/channel-types').then((m) => m.AdminChannelTypes) },

  { path: 'admin/users', title: 'Users · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/users').then((m) => m.AdminUsers) },
  { path: 'admin/rules', title: 'Rules (all) · EventAlert', canActivate: [adminGuard], loadComponent: () => import('./features/admin/rules').then((m) => m.AdminRules) },

  { path: '**', redirectTo: 'rules' },
];

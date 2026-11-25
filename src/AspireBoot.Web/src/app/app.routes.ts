import { Routes } from '@angular/router';
import { authGuard } from './identity/shared/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'home.title',
    loadComponent: () => import('./platform/home/home.page').then((m) => m.HomePage),
  },
  {
    path: 'signup',
    title: 'auth.signUp.title',
    loadComponent: () => import('./identity/auth/sign-up/sign-up.page').then((m) => m.SignUpPage),
  },
  {
    path: 'confirm',
    title: 'auth.confirm.title',
    loadComponent: () => import('./identity/auth/confirm/confirm.page').then((m) => m.ConfirmPage),
  },
  {
    path: 'signin',
    title: 'auth.signIn.title',
    loadComponent: () => import('./identity/auth/sign-in/sign-in.page').then((m) => m.SignInPage),
  },
  {
    path: 'recover',
    title: 'auth.recover.title',
    loadComponent: () =>
      import('./identity/auth/recover/recover-request.page').then((m) => m.RecoverRequestPage),
  },
  {
    path: 'recover/complete',
    title: 'auth.recover.complete.title',
    loadComponent: () =>
      import('./identity/auth/recover/recover-complete.page').then((m) => m.RecoverCompletePage),
  },
  {
    path: 'account',
    title: 'account.home.title',
    canMatch: [authGuard],
    loadComponent: () =>
      import('./identity/account/home/account-home.page').then((m) => m.AccountHomePage),
  },
  {
    path: 'account/passkeys',
    title: 'account.passkeys.title',
    canMatch: [authGuard],
    loadComponent: () =>
      import('./identity/account/passkeys/passkeys.page').then((m) => m.PasskeysPage),
  },
  {
    path: '**',
    title: 'notFound.title',
    loadComponent: () => import('./platform/not-found/not-found.page').then((m) => m.NotFoundPage),
  },
];

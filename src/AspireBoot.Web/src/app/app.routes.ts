import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: $localize`:@@home.title:AspireBoot`,
    loadComponent: () => import('./platform/home/home.page').then((m) => m.HomePage),
  },
  {
    path: '**',
    title: $localize`:@@notFound.title:Page not found · AspireBoot`,
    loadComponent: () => import('./platform/not-found/not-found.page').then((m) => m.NotFoundPage),
  },
];

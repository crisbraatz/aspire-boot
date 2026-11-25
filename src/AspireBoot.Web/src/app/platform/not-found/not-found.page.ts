import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'ab-not-found-page',
  imports: [RouterLink],
  template: `
    <section aria-labelledby="not-found-heading" class="space-y-4">
      <h1 id="not-found-heading" class="text-2xl font-bold" i18n="@@notFound.heading">
        Page not found
      </h1>
      <p i18n="@@notFound.body">The page you are looking for does not exist or was moved.</p>
      <a
        routerLink="/"
        class="inline-flex min-h-11 items-center rounded-md bg-brand-600 px-4 font-medium text-white hover:bg-brand-700"
        i18n="@@notFound.backHome"
        >Back to home</a
      >
    </section>
  `,
})
export class NotFoundPage {}

import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'ab-not-found-page',
  imports: [RouterLink, TranslatePipe],
  template: `
    <section aria-labelledby="not-found-heading" class="space-y-4">
      <h1 id="not-found-heading" class="text-2xl font-bold">{{ 'notFound.heading' | t }}</h1>
      <p>{{ 'notFound.body' | t }}</p>
      <a
        routerLink="/"
        class="inline-flex min-h-11 items-center rounded-md bg-brand-600 px-4 font-medium text-white hover:bg-brand-700"
        >{{ 'notFound.backHome' | t }}</a
      >
    </section>
  `,
})
export class NotFoundPage {}

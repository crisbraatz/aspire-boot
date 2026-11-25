import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18n, TranslateParams } from './i18n';

/** `{{ 'home.heading' | t }}` or `{{ 'home.apiStatus.online' | t: { version } }}`. Impure so it follows culture switches. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(id: string, params?: TranslateParams): string {
    return this.i18n.t(id, params);
  }
}

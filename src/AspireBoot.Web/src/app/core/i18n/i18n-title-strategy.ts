import { Injectable, effect, inject } from '@angular/core';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { I18n } from './i18n';

/** Treats route titles as message ids and re-translates the document title when the culture changes. */
@Injectable()
export class I18nTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18n);
  private titleKey: string | undefined;

  constructor() {
    super();
    effect(() => {
      this.i18n.culture();
      if (this.titleKey) this.title.setTitle(this.i18n.t(this.titleKey));
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.titleKey = this.buildTitle(snapshot);
    if (this.titleKey) this.title.setTitle(this.i18n.t(this.titleKey));
  }
}

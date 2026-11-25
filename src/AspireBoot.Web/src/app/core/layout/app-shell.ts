import { Component, ElementRef, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { I18n } from '../i18n/i18n';
import { SUPPORTED_CULTURES, isSupportedCulture } from '../i18n/supported-cultures';
import { TranslatePipe } from '../i18n/translate.pipe';
import { Session } from '../../identity/shared/session';

/** Accessible application frame: skip link, header, main landmark and focus management on navigation. */
@Component({
  selector: 'ab-app-shell',
  imports: [RouterOutlet, RouterLink, TranslatePipe],
  templateUrl: './app-shell.html',
})
export class AppShell {
  protected readonly i18n = inject(I18n);
  protected readonly session = inject(Session);
  private readonly router = inject(Router);
  protected readonly cultures = SUPPORTED_CULTURES;
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  constructor() {
    // Anonymous visitors see the sign-in and sign-up links once GET me has answered.
    this.session.refresh().catch(() => undefined);
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.focusMainContent());
  }

  /** Switches the UI language in place; an unknown or failed load keeps the current culture. */
  protected onLanguageChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (isSupportedCulture(value)) {
      this.session.changeCulture(value).catch(() => undefined);
    }
  }

  /** Ends the session on the API, then leaves the signed-in area. */
  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/');
  }

  protected focusMainContent(): void {
    this.main().nativeElement.focus({ preventScroll: true });
  }
}

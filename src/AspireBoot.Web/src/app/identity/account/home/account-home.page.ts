import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Session } from '../../shared/session';

/** Signed-in area entry point: shows who is signed in and links to passkey management. */
@Component({
  selector: 'ab-account-home-page',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './account-home.page.html',
})
export class AccountHomePage {
  protected readonly session = inject(Session);
  private readonly router = inject(Router);

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/');
  }
}

import { Component, inject } from '@angular/core';
import { PlatformApi } from '../shared/platform-api';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'ab-home-page',
  imports: [TranslatePipe],
  templateUrl: './home.page.html',
})
export class HomePage {
  protected readonly appInfo = inject(PlatformApi).appInfo();
}

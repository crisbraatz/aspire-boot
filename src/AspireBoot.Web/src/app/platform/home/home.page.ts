import { Component, inject } from '@angular/core';
import { PlatformApi } from '../shared/platform-api';

@Component({
  selector: 'ab-home-page',
  templateUrl: './home.page.html',
})
export class HomePage {
  protected readonly appInfo = inject(PlatformApi).appInfo();
}

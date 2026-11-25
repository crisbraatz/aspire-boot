import { Component } from '@angular/core';
import { AppShell } from './core/layout/app-shell';

@Component({
  selector: 'ab-root',
  imports: [AppShell],
  template: '<ab-app-shell />',
})
export class App {}

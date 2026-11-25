import { Component, ElementRef, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';

/** Accessible application frame: skip link, header, main landmark and focus management on navigation. */
@Component({
  selector: 'ab-app-shell',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app-shell.html',
})
export class AppShell {
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.focusMainContent());
  }

  protected focusMainContent(): void {
    this.main().nativeElement.focus({ preventScroll: true });
  }
}

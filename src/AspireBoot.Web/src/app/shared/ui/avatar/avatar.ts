import { Component, computed, inject, input } from '@angular/core';
import { I18n } from '../../../core/i18n/i18n';

export type AvatarSize = 'sm' | 'md' | 'lg';

const SIZE_CLASSES: Record<AvatarSize, string> = {
  sm: 'h-8 w-8 text-xs',
  md: 'h-10 w-10 text-sm',
  lg: 'h-24 w-24 text-2xl',
};

/**
 * Round profile picture. Shows the image when a url is given, otherwise the initials of the first and
 * last word. The full name is always available to assistive technology: as the image alt text, or as
 * visually hidden text beside the initials (which are aria-hidden).
 */
@Component({
  selector: 'ab-avatar',
  template: `
    <span
      class="inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-slate-200 font-semibold text-slate-800 dark:bg-slate-700 dark:text-slate-100 {{
        sizeClass()
      }}"
    >
      @if (url()) {
        <img class="h-full w-full object-cover" [src]="url()" [alt]="altText()" />
      } @else {
        <span aria-hidden="true">{{ initials() }}</span>
        <span class="sr-only">{{ name() }}</span>
      }
    </span>
  `,
})
export class Avatar {
  private readonly i18n = inject(I18n);

  readonly name = input.required<string>();
  readonly url = input<string | null>(null);
  readonly size = input<AvatarSize>('md');

  protected readonly sizeClass = computed(() => SIZE_CLASSES[this.size()]);
  protected readonly altText = computed(() => this.i18n.t('avatar.alt', { name: this.name() }));

  /** First letter of the first word and of the last word, uppercase. Empty for an empty name. */
  protected readonly initials = computed(() => {
    const words = this.name().trim().split(/\s+/).filter(Boolean);
    if (words.length === 0) return '';
    const first = Array.from(words[0])[0] ?? '';
    const last = words.length > 1 ? (Array.from(words[words.length - 1])[0] ?? '') : '';
    return (first + last).toLocaleUpperCase();
  });
}

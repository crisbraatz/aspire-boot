import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Culture, DEFAULT_CULTURE, isSupportedCulture, matchCulture } from './supported-cultures';

export type Catalog = Record<string, string>;
export type TranslateParams = Record<string, string | number>;

const STORAGE_KEY = 'aspireboot.culture';

/** Runtime translation service: catalogs are fetched from /i18n/{culture}.json and switched without reload. */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly http = inject(HttpClient);
  private readonly catalogs = signal<Partial<Record<Culture, Catalog>>>({});

  /** The active culture. */
  readonly culture = signal<Culture>(DEFAULT_CULTURE);

  private readonly active = computed<Catalog>(() => this.catalogs()[this.culture()] ?? {});
  private readonly fallback = computed<Catalog>(() => this.catalogs()[DEFAULT_CULTURE] ?? {});

  /** Looks up a message id in the active culture, then en-US, and interpolates `{param}` placeholders. */
  t(id: string, params?: TranslateParams): string {
    const template = this.active()[id] ?? this.fallback()[id] ?? id;
    if (!params) return template;
    return template.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
      name in params ? String(params[name]) : placeholder,
    );
  }

  /** Loads the catalogs for `culture` (en-US is always loaded as fallback), then makes it active and persists it. */
  async use(culture: Culture): Promise<void> {
    await Promise.all([...new Set([DEFAULT_CULTURE, culture])].map((value) => this.load(value)));
    this.culture.set(culture);
    this.persist(culture);
    document.documentElement.lang = culture;
  }

  /** Persisted choice, then the browser language, then en-US. */
  resolveInitialCulture(): Culture {
    const stored = this.readStored();
    if (stored) return stored;
    return matchCulture(navigator.language) ?? DEFAULT_CULTURE;
  }

  private async load(culture: Culture): Promise<void> {
    if (this.catalogs()[culture]) return;
    const catalog = await firstValueFrom(this.http.get<Catalog>(`/i18n/${culture}.json`));
    this.catalogs.update((current) => ({ ...current, [culture]: catalog }));
  }

  private readStored(): Culture | undefined {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value && isSupportedCulture(value) ? value : undefined;
    } catch {
      return undefined;
    }
  }

  private persist(culture: Culture): void {
    try {
      localStorage.setItem(STORAGE_KEY, culture);
    } catch {
      // Storage can be unavailable (private mode, blocked site data); the choice then lasts for this page only.
    }
  }
}

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18n } from '../../../core/i18n/i18n';
import en from '../../../../../public/i18n/en-US.json';
import { Avatar } from './avatar';

describe('Avatar', () => {
  let fixture: ComponentFixture<Avatar>;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Ana Maria Silva');
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  /** Loads the real en-US catalog so localized text resolves as in the app. */
  async function loadCatalog(): Promise<void> {
    const loading = TestBed.inject(I18n).use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush(en);
    await loading;
  }

  function element<T extends HTMLElement>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector) as T | null;
  }

  it('shows the picture with localized alt text when a url is given', async () => {
    await loadCatalog();
    fixture.componentRef.setInput('url', '/api/identity/account/avatar/2');
    fixture.detectChanges();

    const img = element<HTMLImageElement>('img');
    expect(img).not.toBeNull();
    expect(img?.getAttribute('src')).toBe('/api/identity/account/avatar/2');
    expect(img?.alt).toBe(en['avatar.alt'].replace('{name}', 'Ana Maria Silva'));
    expect(element('[aria-hidden="true"]')).toBeNull();
  });

  it('shows the initials of the first and last word when there is no url', () => {
    fixture.componentRef.setInput('url', null);
    fixture.detectChanges();

    expect(element('img')).toBeNull();
    expect(element('[aria-hidden="true"]')?.textContent?.trim()).toBe('AS');
  });

  it('uses a single initial for a one-word name, in uppercase', () => {
    fixture.componentRef.setInput('name', '  ana ');
    fixture.componentRef.setInput('url', null);
    fixture.detectChanges();

    expect(element('[aria-hidden="true"]')?.textContent?.trim()).toBe('A');
  });

  it('exposes the full name as visually hidden text next to the initials', () => {
    fixture.componentRef.setInput('url', null);
    fixture.detectChanges();

    const hidden = element('.sr-only');
    expect(hidden?.textContent?.trim()).toBe('Ana Maria Silva');
  });

  it('renders no initials for an empty name', () => {
    fixture.componentRef.setInput('name', '');
    fixture.componentRef.setInput('url', null);
    fixture.detectChanges();

    expect(element('[aria-hidden="true"]')?.textContent?.trim()).toBe('');
  });

  it('follows changes to the name and url', () => {
    fixture.componentRef.setInput('url', null);
    fixture.detectChanges();
    fixture.componentRef.setInput('name', 'Bia Costa');
    fixture.detectChanges();

    expect(element('[aria-hidden="true"]')?.textContent?.trim()).toBe('BC');

    fixture.componentRef.setInput('url', '/api/identity/account/avatar/3');
    fixture.detectChanges();

    expect(element('img')?.getAttribute('src')).toBe('/api/identity/account/avatar/3');
  });
});

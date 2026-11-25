import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { I18n } from './i18n';
import { TranslatePipe } from './translate.pipe';

const STORAGE_KEY = 'aspireboot.culture';

@Component({
  selector: 'ab-probe',
  imports: [TranslatePipe],
  template: `<p>{{ 'shell.brand' | t }}</p>
    <p>{{ 'home.apiStatus.online' | t: { version: '2.0' } }}</p>`,
})
class ProbeComponent {}

describe('I18n', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ProbeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  it('switches the rendered text without a reload', async () => {
    const i18n = TestBed.inject(I18n);
    const loading = i18n.use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush({
      'shell.brand': 'AspireBoot',
      'home.apiStatus.online': 'API online · version {version}',
    });
    await loading;

    const fixture = TestBed.createComponent(ProbeComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('API online · version 2.0');

    const switching = i18n.use('pt-BR');
    httpTesting.expectOne('/i18n/pt-BR.json').flush({
      'shell.brand': 'AspireBoot PT',
      'home.apiStatus.online': 'API online · versão {version}',
    });
    await switching;
    fixture.detectChanges();

    expect(i18n.culture()).toBe('pt-BR');
    expect(fixture.nativeElement.textContent).toContain('AspireBoot PT');
    expect(fixture.nativeElement.textContent).toContain('versão 2.0');
  });

  it('falls back to en-US when the active catalog lacks a key', async () => {
    const i18n = TestBed.inject(I18n);
    const loading = i18n.use('en-US');
    httpTesting
      .expectOne('/i18n/en-US.json')
      .flush({ 'shell.brand': 'AspireBoot', 'notFound.heading': 'Page not found' });
    await loading;

    const switching = i18n.use('es-ES');
    httpTesting.expectOne('/i18n/es-ES.json').flush({});
    await switching;

    expect(i18n.t('notFound.heading')).toBe('Page not found');
    expect(i18n.t('missing.key')).toBe('missing.key');
  });

  it('persists the chosen culture and sets the document language', async () => {
    const i18n = TestBed.inject(I18n);
    const loading = i18n.use('pt-BR');
    httpTesting.expectOne('/i18n/en-US.json').flush({});
    httpTesting.expectOne('/i18n/pt-BR.json').flush({});
    await loading;

    expect(localStorage.getItem(STORAGE_KEY)).toBe('pt-BR');
    expect(document.documentElement.lang).toBe('pt-BR');
  });

  it('interpolates named parameters', async () => {
    const i18n = TestBed.inject(I18n);
    const loading = i18n.use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush({ greet: 'Hi {name}, {name}!' });
    await loading;

    expect(i18n.t('greet', { name: 'Ana' })).toBe('Hi Ana, Ana!');
  });

  it('prefers the persisted culture over the browser language', () => {
    localStorage.setItem(STORAGE_KEY, 'es-ES');
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('pt-BR');
    expect(TestBed.inject(I18n).resolveInitialCulture()).toBe('es-ES');
  });

  it('matches the browser language by primary subtag', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('pt-PT');
    expect(TestBed.inject(I18n).resolveInitialCulture()).toBe('pt-BR');
  });

  it('defaults to en-US for unsupported languages and corrupt storage', () => {
    localStorage.setItem(STORAGE_KEY, 'xx-XX');
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('fr-FR');
    expect(TestBed.inject(I18n).resolveInitialCulture()).toBe('en-US');
  });
});

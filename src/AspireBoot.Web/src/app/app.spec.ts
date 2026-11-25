import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { problemInterceptor } from './core/http/problem.interceptor';
import { I18n } from './core/i18n/i18n';

const ENGLISH = {
  'shell.skipToContent': 'Skip to main content',
  'shell.language.label': 'Language',
  'shell.nav.account': 'Account',
  'shell.nav.signIn': 'Sign in',
  'shell.nav.signUp': 'Sign up',
};
const PORTUGUESE = {
  'shell.skipToContent': 'Pular para o conteúdo principal',
  'shell.language.label': 'Idioma',
};

describe('App', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    const loading = TestBed.inject(I18n).use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush(ENGLISH);
    await loading;
  });

  // The shell asks who is signed in on start; an anonymous visitor gets 401.
  function answerAnonymous(): void {
    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
  }

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  it('renders the skip link and the main landmark', async () => {
    const fixture = TestBed.createComponent(App);
    answerAnonymous();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('a[href="#main"]')?.textContent).toContain('Skip to main content');
    expect(element.querySelector('main#main')).not.toBeNull();
  });

  it('offers sign in and sign up links to anonymous visitors', async () => {
    const fixture = TestBed.createComponent(App);
    answerAnonymous();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('nav a[href="/signin"]')?.textContent).toContain('Sign in');
    expect(element.querySelector('nav a[href="/signup"]')?.textContent).toContain('Sign up');
  });

  it('switches the shell language from the labelled select', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    answerAnonymous();

    const element = fixture.nativeElement as HTMLElement;
    const select = element.querySelector<HTMLSelectElement>('select#language-select');
    expect(select?.labels?.[0]?.textContent).toContain('Language');

    select!.value = 'pt-BR';
    select!.dispatchEvent(new Event('change'));
    httpTesting.expectOne('/i18n/pt-BR.json').flush(PORTUGUESE);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('a[href="#main"]')?.textContent).toContain(
      'Pular para o conteúdo principal',
    );
    expect(select?.value).toBe('pt-BR');
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HomePage } from './home.page';

describe('HomePage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('renders a single level-one heading', async () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();

    const headings = (fixture.nativeElement as HTMLElement).querySelectorAll('h1');
    expect(headings).toHaveLength(1);
    httpTesting
      .expectOne('/api/platform/info')
      .flush({ name: 'AspireBoot', version: '1.0.0', environment: 'Test' });
    await fixture.whenStable();
  });

  it('announces the API version when the API responds', async () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();

    httpTesting
      .expectOne('/api/platform/info')
      .flush({ name: 'AspireBoot', version: '1.2.3', environment: 'Development' });
    await fixture.whenStable();

    const status = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(status?.textContent).toContain('1.2.3');
  });

  it('announces when the API is unavailable', async () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();

    httpTesting
      .expectOne('/api/platform/info')
      .flush('down', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    const status = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(status?.textContent).toContain('API unavailable');
  });
});

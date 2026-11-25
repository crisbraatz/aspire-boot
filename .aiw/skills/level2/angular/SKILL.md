---
name: angular
description: Load before writing any frontend code in src/AspireBoot.Web (components, routing, forms, HTTP, i18n, tests).
level: 2
kind: technology
version: Angular 22.3 / TypeScript 6.0
sources:
  - https://angular.dev/ai/develop-with-ai — Google Angular team
  - https://angular.dev/assets/context/best-practices.md — Google Angular team (official AI rules file)
  - https://angular.dev/style-guide — Google Angular team
  - https://angular.dev/best-practices/a11y — Google Angular team
  - https://angular.dev/best-practices/security — Google Angular team
  - https://angular.dev/guide/i18n — Google Angular team
  - https://angular.dev/guide/testing — Google Angular team
  - https://angular.dev/guide/zoneless — Google Angular team
  - https://angular.dev/guide/signals — Google Angular team
  - https://angular.dev/guide/signals/resource — Google Angular team
  - https://angular.dev/guide/http/http-resource — Google Angular team
  - https://angular.dev/guide/forms/signals/overview — Google Angular team
  - model-knowledge
fetched: 2026-11-25
---

# Angular 22

## Official AI rules (condensed from Angular's best-practices.md)
- Strict TypeScript. Prefer inference. `unknown` instead of `any`.
- Standalone components only (do not write `standalone: true`, it is the default). `OnPush` is the default in v22, so do not set it.
- State with `signal()`, derived with `computed()`, `set()`/`update()` (never `mutate`), and `linkedSignal()` for resettable derived state. `effect()` only for side effects on non-reactive APIs.
- `input()`, `output()`, `model()` functions, never decorators. Host bindings go in the `host` object (no `@HostBinding`/`@HostListener`).
- Native control flow `@if`/`@for` (with `track`)/`@switch`. `class`/`style` bindings instead of `ngClass`/`ngStyle`. Do not import `CommonModule`.
- `inject()` instead of constructor injection. Single-responsibility services (`@Service` for new singletons in v22, or `@Injectable({ providedIn: 'root' })`).
- Forms: **Signal Forms** (`@angular/forms/signals`) for new forms, with schema validation.
- Lazy-load every route. `NgOptimizedImage` for static images.
- Must pass AXE checks and WCAG 2.2 AA.

## Project conventions (AspireBoot)
- Zoneless (default). No `zone.js`.
- Folder per bounded context → slice: `identity/users/list-users/list-users.page.ts`. Route files per context (`identity/users/users.routes.ts`). See the `architecture-ddd-vertical-slices` skill.
- File names hyphenated, matching the main identifier, with tests beside the code (`*.spec.ts`). No `utils.ts`/`helpers.ts`/`components/` folders.
- Selectors prefixed `ab-`. Members used only by the template are `protected`. Angular-initialized members are `readonly`.
- Name handlers by intent (`saveProfile()`), not by event.
- HTTP: `HttpClient` with `withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })` and `withCredentials` for same-origin `/api`. Use `httpResource()` for reads (GET), and `HttpClient` for mutations. Guard `value()` with `hasValue()`.
- API clients per context in `<context>/shared/*-api.ts` with typed request/response models matching the backend contracts. Map Problem Details `type` to translated messages.
- Auth state in `core/auth` as signals (current user, roles). Route guards (`canMatch`) for UX only. The API enforces authorization.
- Errors: a global problem interceptor maps Problem Details `code` values to translated messages. 401 redirects to sign-in, and 429 shows a translated wait time from `Retry-After`. Never log tokens.
- Passkeys: `identity/shared/webauthn.ts` wraps `navigator.credentials` (feature detection, `parseCreationOptionsFromJSON`/`parseRequestOptionsFromJSON` with a manual base64url fallback, and `toJSON()`). Cancel and `NotAllowedError` are mapped to a message, not to an error page. Sign-in uses conditional UI when `PublicKeyCredential.isConditionalMediationAvailable()` is true, plus a visible button.
- Remove the `token` query parameter from the URL with `history.replaceState` after reading it in the confirm and recovery pages.

## i18n (pt-BR, en-US, es-ES) — runtime, ADR 0001
- Runtime language switch without reload, using the in-house `I18n` service in `src/app/core/i18n/`. Do not use Angular's build-time i18n (template i18n attributes, the localize marker function, XLIFF files or the `@angular/localize` package). This project has one runtime mechanism only.
- Templates: `{{ 'identity.signIn.title' | t }}` or `{{ 'identity.welcome' | t: { name } }}` for `{param}` interpolation. TypeScript: `inject(I18n).t(id, params)`.
- Every message id goes into all three catalogs, `public/i18n/pt-BR.json`, `en-US.json` and `es-ES.json`, as a flat JSON key. `catalog-parity.spec.ts` fails on missing or extra keys. Use stable, dotted ids by slice (for example `identity.signUp.submit`).
- The `I18n` service falls back to en-US for missing ids, and it lazy-loads the other catalogs from `/i18n/{culture}.json`. Culture changes are signals, so `| t` updates without reload.
- Route titles are message ids. `I18nTitleStrategy` translates them and updates `document.title` on a culture change.
- The active culture sets `<html lang>`. Use Angular's locale-aware pipes (`date`, `number`) with the active culture for dates and numbers.
- Preference: `localStorage` for anonymous visitors, and the server's `PreferredCulture` for signed-in accounts (synced at sign-in and when changed).
- Plurals and gender are not supported by the catalogs. Use separate ids.

## Accessibility (Angular specifics)
- Native elements first. Extend `button[abButton]` instead of clickable divs.
- `[attr.aria-*]` bindings. CDK `LiveAnnouncer` for async results, `cdkTrapFocus` for dialogs (return focus to the trigger on close).
- On `NavigationEnd`, move focus to the page `<h1>` and update the `<title>` (`TitleStrategy`).
- `routerLinkActive` + `ariaCurrentWhenActive="page"`.
- Form errors: associate with `aria-describedby`, announce them, and focus the first invalid field on submit.

## Security
- Never `innerHTML` with untrusted data, never `bypassSecurityTrust*`. No direct DOM access where templates work.
- AOT builds. CSP compatible (`autoCsp` or nonces). No secrets in `environment.ts`.

## Testing (Vitest)
- `ng test` (watch) / `ng test --no-watch` (CI) with coverage. Use `TestBed` with zoneless defaults, `await fixture.whenStable()`, and `provideHttpClientTesting()` + `HttpTestingController`.
- Test behavior through the DOM (roles/labels), not implementation details.

## Commands
`npm ci` · `npm start` (via Aspire) · `npm run lint` (angular-eslint) · `npm run format:check` (Prettier) · `npm test -- --no-watch` · `npm run build`

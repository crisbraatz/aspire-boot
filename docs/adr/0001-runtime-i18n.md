# ADR 0001: Runtime internationalization with an in-house signal service

- Status: accepted
- Date: 2026-11-25
- Feature: 001 (passwordless account authentication with passkeys)

## Context
The UI ships in pt-BR, en-US and es-ES. Users must be able to switch the language without a page reload, and the choice must follow the user: stored in the browser for anonymous visitors and on the server for signed-in accounts (`Users.PreferredCulture`, also used for the language of the emails).

Angular's built-in i18n (`i18n` attributes and `$localize`) is a build-time mechanism. Each locale is a separate compiled bundle, so switching language means a different build or a reload. It cannot satisfy the requirement, so the application does not use `@angular/localize`.

Third-party runtime libraries (Transloco, ngx-translate) would solve the problem but add a dependency, their own template pipes, and an abstraction layer for a feature that needs three catalogs and one lookup function.

## Decision
Use a small in-house runtime translation layer in `src/AspireBoot.Web/src/app/core/i18n/`:

- `I18n` (`providedIn: 'root'`): a signal-based service. `culture` is a signal, and `t(id, params)` reads the active catalog with fallback to en-US and replaces `{param}` placeholders. `use(culture)` loads catalogs, switches the active culture, persists the choice in `localStorage` and sets `<html lang>`.
- `TranslatePipe` (`| t`): used in templates. Because `t` reads signals, templates update when the culture changes.
- `I18nTitleStrategy`: a `TitleStrategy` that treats route `title` values as message ids and re-translates the document title on a culture change.
- Catalogs: flat JSON files keyed by stable message ids, in `src/AspireBoot.Web/public/i18n/{pt-BR,en-US,es-ES}.json`. en-US is the default and is loaded with every culture as fallback. The other catalogs are loaded on demand from `/i18n/{culture}.json`.
- A parity spec (`catalog-parity.spec.ts`) fails when a key used in a template or in code is missing from any of the three catalogs, and when the catalogs disagree on their keys.
- Preference sync: anonymous visitors use `localStorage`. The signed-in preference is read from `GET /api/identity/account/me` and changed through `PUT /api/identity/account/preferences`, and the server value wins at sign-in.

Alternatives considered:
- Angular build-time i18n with one build per locale: rejected, because switching requires a reload and three builds per deployment.
- Transloco or ngx-translate: rejected for now. The need is covered by about 150 lines of code without a new dependency. Revisit if the project needs plural rules, ICU messages or lazy scopes beyond what the catalogs provide.

## Consequences
- No new frontend dependency. The project does not depend on `@angular/localize` or its polyfill.
- Contributors add text by adding a stable id to all three catalogs and using `| t` or `I18n.t`. Hard-coded user-facing text fails review, and the parity spec catches missing keys.
- Catalogs are plain JSON, so translators can edit them without tooling. There is no ICU plural or gender support. Messages that need plurals must use separate ids.
- The language is applied on the client. The server only stores the preference and uses it for emails (`Culture` in the outbox messages).
- Dates and numbers still use Angular's locale-aware pipes and the `Intl` APIs with the active culture.

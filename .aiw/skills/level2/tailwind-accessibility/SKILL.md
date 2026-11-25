---
name: tailwind-accessibility
description: Load when styling UI, building layouts or shared UI components, or reviewing responsiveness/accessibility in the Angular app.
level: 2
kind: technology
version: Tailwind CSS 4.3 + WCAG 2.2 AA
sources:
  - https://tailwindcss.com/docs/installation/framework-guides/angular — Tailwind Labs
  - https://tailwindcss.com/docs/styling-with-utility-classes — Tailwind Labs
  - https://tailwindcss.com/docs/responsive-design — Tailwind Labs
  - https://tailwindcss.com/docs/dark-mode — Tailwind Labs
  - https://tailwindcss.com/docs/theme — Tailwind Labs
  - https://tailwindcss.com/docs/adding-custom-styles — Tailwind Labs
  - https://www.w3.org/WAI/WCAG22/quickref/ — W3C WAI
  - https://www.w3.org/WAI/ARIA/apg/practices/keyboard-interface/ — W3C WAI
  - model-knowledge
fetched: 2026-11-25
---

# Tailwind 4 + accessibility

## Setup
- `tailwindcss` + `@tailwindcss/postcss` + `postcss`, with `.postcssrc.json` `{ "plugins": { "@tailwindcss/postcss": {} } }` and `src/styles.css` starting with `@import "tailwindcss";`.
- Design tokens live **only** in `src/styles.css` `@theme { ... }` (brand colors in `oklch`, fonts, radius, spacing extras). Never invent ad-hoc hex values in templates.
- Dark mode by class (user preference + system default): `@custom-variant dark (&:where(.dark, .dark *));`. A tiny inline script in `index.html` applies the saved theme before paint.

## Authoring rules
- Utilities in templates. Write **complete class names** (never `'bg-' + color`). Use `[class.x]` bindings or maps of full class strings for variants.
- Reuse through **Angular components** in `shared/ui` (button, input, field, card, dialog, table, pagination, alert, avatar). Use `@layer components` only for a handful of primitives. Avoid `@apply` sprawl.
- Never put two conflicting utilities on one element. `!` important is a last resort.
- Arbitrary values (`top-[117px]`) are rare. Promote repeated ones to `@theme`.

## Responsive
- Mobile-first: unprefixed = mobile, then `sm: md: lg: xl:`. Layout must work from **320px** without horizontal scroll and at 200% zoom (WCAG 1.4.10 Reflow).
- Use container queries (`@container`, `@md:`) for components reused in different widths.
- Tables collapse to stacked cards or scroll inside a labeled region on small screens.

## Accessibility checklist (WCAG 2.2 AA, every screen)
- Semantic landmarks (`header`, `nav`, `main`, `footer`), one `<h1>`, logical heading order, and a "skip to content" link (2.4.1).
- Visible focus always (`focus-visible:ring-2 ring-offset-2`), never `outline-none` without a replacement (2.4.7). Focus must not be hidden by sticky headers (2.4.11).
- Contrast: text 4.5:1 (large 3:1), UI components/icons 3:1 (1.4.3, 1.4.11), verified in **both themes**.
- Never convey meaning with color alone. Pair it with icon or text (1.4.1).
- Target size ≥ 24×24 CSS px (2.5.8). Prefer 44px for primary mobile actions.
- Every control has a visible label whose text is in its accessible name (2.5.3, 3.3.2). Use `sr-only` for icon-only buttons.
- Errors identified in text, with a suggestion, linked to fields (3.3.1, 3.3.3). Status messages via `role="status"`/`aria-live="polite"` (4.1.3).
- Accessible authentication: allow paste and password managers, no cognitive tests (3.3.8). Show/hide password toggle.
- Keyboard: everything operable, no traps, DOM order = visual order, no `tabindex > 0`. Composite widgets follow APG patterns (roving tabindex). Dialogs trap focus and return it.
- Respect `prefers-reduced-motion` (`motion-safe:`/`motion-reduce:`).
- Consistent navigation and help placement across pages (3.2.3, 3.2.6).

## Verification
- axe checks in component tests where practical, a manual keyboard pass, and a 320px + 200% zoom check before finishing a UI slice.

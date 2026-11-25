export const SUPPORTED_CULTURES = ['en-US', 'pt-BR', 'es-ES'] as const;

export type Culture = (typeof SUPPORTED_CULTURES)[number];

export const DEFAULT_CULTURE: Culture = 'en-US';

export function isSupportedCulture(value: string): value is Culture {
  return (SUPPORTED_CULTURES as readonly string[]).includes(value);
}

/** Maps a browser language tag (e.g. `pt-PT`) to a supported culture by exact or primary-subtag match. */
export function matchCulture(language: string): Culture | undefined {
  const exact = SUPPORTED_CULTURES.find(
    (culture) => culture.toLowerCase() === language.toLowerCase(),
  );
  if (exact) return exact;
  const primary = language.split('-')[0].toLowerCase();
  return SUPPORTED_CULTURES.find((culture) => culture.split('-')[0].toLowerCase() === primary);
}

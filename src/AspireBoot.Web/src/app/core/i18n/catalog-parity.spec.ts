/// <reference types="vite/client" />
import en from '../../../../public/i18n/en-US.json';
import ptBR from '../../../../public/i18n/pt-BR.json';
import esES from '../../../../public/i18n/es-ES.json';

const modules = import.meta.glob('/src/**/*.{html,ts}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;
const sources = Object.entries(modules)
  .filter(([path]) => !path.endsWith('.spec.ts'))
  .map(([, source]) => source);

/** Keys used through the `t` pipe or as route titles. */
function keysUsedInSources(): Set<string> {
  const keys = new Set<string>();
  const pipe = /['"]([\w.-]+)['"]\s*\|\s*t\b/g;
  const title = /title:\s*'([\w.-]+)'/g;
  for (const source of sources) {
    for (const match of source.matchAll(pipe)) keys.add(match[1]);
    for (const match of source.matchAll(title)) keys.add(match[1]);
  }
  return keys;
}

describe('i18n catalogs', () => {
  const catalogs = { 'en-US': en, 'pt-BR': ptBR, 'es-ES': esES } as Record<
    string,
    Record<string, string>
  >;

  it('have identical key sets', () => {
    const expected = Object.keys(en).sort();
    for (const [culture, catalog] of Object.entries(catalogs)) {
      expect(Object.keys(catalog).sort(), culture).toEqual(expected);
    }
  });

  it('have no empty values', () => {
    for (const [culture, catalog] of Object.entries(catalogs)) {
      for (const [key, value] of Object.entries(catalog)) {
        expect(value.trim(), `${culture} ${key}`).not.toBe('');
      }
    }
  });

  it('define every key used with the t pipe or as a route title', () => {
    const used = keysUsedInSources();
    expect(used.size).toBeGreaterThan(0);
    const missing = [...used].filter((key) => !(key in en));
    expect(missing).toEqual([]);
  });
});

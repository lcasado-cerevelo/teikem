// Verifica que los pares texto/fondo de la paleta (claro y oscuro) cumplan WCAG AA (4.5:1). Uso: node scripts/check-theme-contrast.mjs
import { readFileSync } from 'node:fs';

const src = readFileSync(new URL('../src/constants/theme.ts', import.meta.url), 'utf8');
const block = (name) => {
  const m = src.match(new RegExp(`export const ${name}: Colors = \\{([\\s\\S]*?)\\n\\};`));
  return Object.fromEntries([...m[1].matchAll(/(\w+): '([^']+)'/g)].map((x) => [x[1], x[2]]));
};
const lum = (hex) => {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
};
const ratio = (a, b) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
};
const pairs = [['text', 'background'], ['text', 'backgroundSecondary'], ['muted', 'background'], ['muted', 'backgroundSecondary'], ['primary', 'background'], ['primary', 'backgroundSecondary'], ['primaryContrast', 'primary'], ['danger', 'background']];
let bad = 0;
for (const theme of ['lightColors', 'darkColors']) {
  const c = block(theme);
  for (const [fg, bg] of pairs) {
    const r = ratio(c[fg], c[bg]);
    if (r < 4.5) { bad++; console.error(`${theme}: ${fg} sobre ${bg} = ${r.toFixed(2)} (< 4.5)`); }
  }
  for (const bg of ['online', 'offline']) {
    const r = ratio('#ffffff', c[bg]);
    if (r < 4.5) { bad++; console.error(`${theme}: blanco sobre ${bg} = ${r.toFixed(2)} (< 4.5)`); }
  }
}
if (bad) process.exit(1);
console.log('Contraste AA correcto en ambos temas.');

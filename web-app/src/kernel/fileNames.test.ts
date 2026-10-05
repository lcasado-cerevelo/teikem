import { describe, expect, it } from 'vitest'

// En Windows (y macOS) el sistema de archivos NO distingue mayúsculas: `./BinLabels` encuentra `binLabels.ts` aunque quien lo
// importa quería `BinLabels.tsx`, y la pantalla falla con "does not provide an export named …" (2026-10-05). En Linux/CI no pasa, así que
// el error solo aparece en la máquina de quien desarrolla en Windows. Esta prueba lo atrapa en cualquier sistema.
// (import.meta.glob de Vite solo lista las rutas: no carga ningún módulo.)
const MODULES = Object.keys(import.meta.glob('/src/**/*.{ts,tsx,js,jsx}'))

describe('nombres de archivo', () => {
  it('ningún par de archivos de src se diferencia solo por mayúsculas (sin contar la extensión)', () => {
    const seen = new Map<string, string>()
    const clashes: string[] = []
    for (const path of MODULES) {
      // las importaciones omiten la extensión: `x/Foo.tsx` y `x/foo.ts` son el mismo módulo para Windows (css/json se importan con extensión)
      const key = path.replace(/\.(tsx?|jsx?)$/i, '').toLowerCase()
      const other = seen.get(key)
      if (other && other !== path) clashes.push(`${other}  <->  ${path}`)
      else seen.set(key, path)
    }
    expect(MODULES.length).toBeGreaterThan(100) // que el glob de verdad encuentre los archivos
    expect(clashes).toEqual([])
  })
})

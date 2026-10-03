// Lote F14 — codificador Code 128: tabla de patrones, valores de símbolos con los cambios de subconjunto B/C, dígito
// verificador módulo 103, parada y barras. Los vectores se validaron fuera del repo decodificando las imágenes con
// zxing-cpp y comparando la tabla con python-barcode (ver docs/frontend/loteF14-decisiones.md); aquí quedan fijos.
import { describe, expect, it } from 'vitest'
import {
  CODE128,
  CODE128_PATTERNS,
  code128Checksum,
  code128Unsupported,
  code128Values,
  code128WidthFor,
  encodeCode128,
  isCode128Encodable,
} from './code128'

const sum = (w: string) => [...w].reduce((a, c) => a + Number(c), 0)

describe('code128 · tabla', () => {
  it('107 patrones: 0–105 de 11 módulos (3 barras + 3 espacios), parada de 13 (2331112)', () => {
    expect(CODE128_PATTERNS).toHaveLength(107)
    CODE128_PATTERNS.slice(0, 106).forEach((p) => {
      expect(p).toMatch(/^[1-4]{6}$/)
      expect(sum(p)).toBe(11)
    })
    expect(CODE128_PATTERNS[CODE128.STOP]).toBe('2331112')
    expect(sum(CODE128_PATTERNS[CODE128.STOP])).toBe(13)
    // inicios A/B/C y algunos de referencia de la norma
    expect(CODE128_PATTERNS[CODE128.START_A]).toBe('211412')
    expect(CODE128_PATTERNS[CODE128.START_B]).toBe('211214')
    expect(CODE128_PATTERNS[CODE128.START_C]).toBe('211232')
    expect(CODE128_PATTERNS[0]).toBe('212222')
    expect(CODE128_PATTERNS[CODE128.CODE_C]).toBe('113141')
    expect(CODE128_PATTERNS[CODE128.CODE_B]).toBe('114131')
    // sin patrones repetidos
    expect(new Set(CODE128_PATTERNS).size).toBe(107)
  })
})

describe('code128 · valores y verificador', () => {
  it.each([
    // [valor, valores de símbolos con el verificador al final]
    ['PJJ123C', [104, 48, 42, 42, 17, 18, 19, 35, 55]], // solo B: una corrida de 3 dígitos en medio no conviene en C
    ['A', [104, 33, 34]], // mínimo: un carácter
    ['12', [105, 12, 14]], // exactamente dos dígitos: inicio C
    ['123', [104, 17, 18, 19, 8]], // tres dígitos: B (C no ahorra)
    ['1234', [105, 12, 34, 82]], // cuatro dígitos: C
    ['12345', [105, 12, 34, 100, 21, 54]], // impar desde C: el último dígito tras "Code B"
    ['123456', [105, 12, 34, 56, 44]],
    ['0123456789', [105, 1, 23, 45, 67, 89, 73]], // par: todo en C (con el 0 inicial)
    ['012345678', [105, 1, 23, 45, 67, 100, 24, 66]], // impar
    ['X1234Y', [104, 56, 17, 18, 19, 20, 57, 45]], // 4 dígitos en medio: se quedan en B
    ['X123456Y', [104, 56, 99, 12, 34, 56, 100, 57, 58]], // 6 en medio: a C y de vuelta a B
    ['X12345Y', [104, 56, 17, 18, 19, 20, 21, 57, 22]], // 5 en medio: B
    ['AB1234', [104, 33, 34, 99, 12, 34, 102]], // 4 al final: a C
    ['AB12345', [104, 33, 34, 17, 99, 23, 45, 7]], // 5 al final: el primero en B, el resto (par) en C
    ['1234A', [105, 12, 34, 100, 33, 102]],
    ['SKU-0001', [104, 51, 43, 53, 13, 99, 0, 1, 27]], // SKU con guion y dígitos finales
    ['SKU/2026/0042', [104, 51, 43, 53, 15, 18, 16, 18, 22, 15, 99, 0, 42, 2]], // con barras
    ['A-01-02-03', [104, 33, 13, 16, 17, 13, 16, 18, 13, 16, 19, 77]], // código de posición típico
  ])('%s', (value, expected) => {
    const symbol = encodeCode128(value)
    expect(symbol.values).toEqual(expected)
    expect(symbol.checksum).toBe(expected[expected.length - 1])
    expect(code128Checksum(code128Values(value))).toBe(symbol.checksum)
    expect(symbol.width).toBe(code128WidthFor(expected.length))
  })

  it('verificador: (inicio + Σ valor × posición) módulo 103', () => {
    // PJJ123C: 104 + 48·1 + 42·2 + 42·3 + 17·4 + 18·5 + 19·6 + 35·7 = 879 → 879 mod 103 = 55
    expect(code128Checksum([104, 48, 42, 42, 17, 18, 19, 35])).toBe(55)
    expect(code128Checksum([105])).toBe(2)
  })
})

describe('code128 · módulos y barras', () => {
  it('módulos alternan barra/espacio, empiezan y terminan en barra; las barras suman el ancho negro', () => {
    const s = encodeCode128('GLU-100')
    expect(s.modules.length % 2).toBe(1) // termina en barra (la final de la parada)
    expect(s.modules.reduce((a, b) => a + b, 0)).toBe(s.width)
    expect(s.width).toBe(112)
    // la primera barra es la del inicio B (2 módulos) y la última, la barra final de 2 módulos
    expect(s.bars[0]).toEqual({ x: 0, width: 2 })
    expect(s.bars[s.bars.length - 1]).toEqual({ x: s.width - 2, width: 2 })
    const black = s.modules.filter((_, i) => i % 2 === 0).reduce((a, b) => a + b, 0)
    expect(s.bars.reduce((a, b) => a + b.width, 0)).toBe(black)
    // símbolos: inicio + 7 datos + verificador = 9 → 9 × 3 barras + 4 de la parada
    expect(s.bars).toHaveLength(9 * 3 + 4)
  })

  it('máximo de longitud de los datos: SKU de 60 caracteres (tope de la base) y 60 dígitos', () => {
    const letters = 'Z'.repeat(60)
    const s = encodeCode128(letters)
    expect(s.values).toHaveLength(62) // inicio + 60 + verificador
    expect(s.width).toBe(62 * 11 + 13)
    const digits = '1234567890'.repeat(6)
    const d = encodeCode128(digits)
    expect(d.values).toHaveLength(32) // inicio C + 30 pares + verificador
    expect(d.width).toBe(32 * 11 + 13)
  })
})

describe('code128 · caracteres admitidos', () => {
  it('ASCII 32–126 sí (espacio, símbolos, minúsculas); vacío, acentos, ñ, tabulador y emojis no', () => {
    expect(isCode128Encodable(' !"#$%&\'()*+,-./0123456789:;<=>?@AZ[\\]^_`az{|}~')).toBe(true)
    expect(isCode128Encodable('')).toBe(false)
    expect(isCode128Encodable('CAFÉ-01')).toBe(false)
    expect(code128Unsupported('AÑO-Ñ1\tx📦')).toEqual(['Ñ', '\t', '📦'])
    expect(() => encodeCode128('')).toThrow(/vacío/)
    expect(() => encodeCode128('CAFÉ')).toThrow(/É/)
  })
})

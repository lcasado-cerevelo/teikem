// Asistente de lectura (2026-10-24): del renglón de muestra a la expresión, sin que nadie escriba expresiones.
import { describe, expect, it } from 'vitest'
import { columnsFor, countMatches, lineRegexFor, parseColumnsText, sampleLines, singlePatternFor, splitCells, totalPatternFor } from './assistantRules'
import { parseBlockColumns } from './emailRules'

const doc = ['Invoice 2193', 'Item\tDescription\tPrice\tAmount', 'T-100\tWalker\t10.00\t120.00', 'T-200\tCane\t5.50\t33.00', 'Total Amount USD 153.00'].join('\n')

describe('asistente de lectura', () => {
  it('separa un renglón en celdas por tabulador o por huecos', () => {
    expect(splitCells('T-100\tWalker\t10.00\t120.00')).toEqual(['T-100', 'Walker', '10.00', '120.00'])
    expect(splitCells('T-100   Walker   10.00')).toEqual(['T-100', 'Walker', '10.00'])
  })

  it('arma la expresión de un renglón y reconoce los renglones parecidos', () => {
    const lines = sampleLines(doc)
    const re = lineRegexFor(lines[2])
    expect(countMatches(lines, re)).toBe(2)   // los dos productos, no el encabezado ni el total
    expect(countMatches(lines, '([')).toBe(-1)
  })

  it('«columnas» sale en el formato que entiende el servidor, con la división', () => {
    const text = columnsFor({ 2: { cell: 1, divideBy: 0 }, 3: { cell: 4, divideBy: 3 }, 9: { cell: 0, divideBy: 0 } })
    expect(text).toBe('2=1, 3=4/3')
    expect(parseBlockColumns(text)).toEqual([{ position: 2, index: 1 }, { position: 3, index: 4, divide: 3 }])
    expect(parseColumnsText(text)).toEqual([{ position: 2, cell: 1, divideBy: 0 }, { position: 3, cell: 4, divideBy: 3 }])
  })

  it('arma la expresión del total que declara el documento', () => {
    const p = totalPatternFor('Total Amount USD 153.00', '153.00')!
    expect(new RegExp(p).exec('xx Total Amount USD   2,153.00')?.[1]).toBe('2,153.00')
    expect(totalPatternFor('153.00', '153.00')).toBeNull()
  })

  it('arma la expresión de un dato suelto', () => {
    const p = singlePatternFor('Invoice 2193', '2193')!
    expect(new RegExp(p).exec('Invoice 4410')?.[1]).toBe('4410')
  })
})

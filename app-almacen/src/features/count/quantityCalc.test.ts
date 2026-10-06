import { addBlock, calcFromText, calcTotal, emptyCalc, removeBlock, setBlock, totalToText, CALC_MAX, type CalcState } from './quantityCalc'

const st = (blocks: Array<[string, string]>, extra = ''): CalcState => ({ blocks: blocks.map(([rows, cols]) => ({ rows, cols })), extra })

describe('calculadora de cantidad', () => {
  it('el ejemplo del dueño: 5 filas de 3 columnas y 10 sueltas = (5 × 3) + 10 = 25', () => {
    expect(calcTotal(st([['5', '3']], '10'))).toEqual({ total: 25, expression: '(5 × 3) + 10', issue: null })
  })

  it('varios bloques se suman; un bloque en blanco se ignora; sin sueltas también vale', () => {
    expect(calcTotal(st([['5', '3'], ['4', '3'], ['', '']], ''))).toEqual({ total: 27, expression: '(5 × 3) + (4 × 3)', issue: null })
    expect(calcTotal(st([['', '']], '7')).total).toBe(7)
  })

  it('un bloque con solo una medida es un error; algo que no es número, también; todo en blanco no es error pero no hay total', () => {
    expect(calcTotal(st([['5', '']]))).toMatchObject({ total: null, issue: 'incompleteBlock' })
    expect(calcTotal(st([['', '3']]))).toMatchObject({ total: null, issue: 'incompleteBlock' })
    expect(calcTotal(st([['5', '3.5']]))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(st([['-1', '3']]))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(st([['5', '3']], 'abc'))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(emptyCalc())).toEqual({ total: null, expression: '', issue: 'empty' })
  })

  it('filas y columnas son enteros; las sueltas admiten coma o punto decimal y se redondea a 3 decimales', () => {
    expect(calcTotal(st([['2', '2']], '1,5')).total).toBe(5.5)
    expect(calcTotal(st([['1', '1']], '0.1234')).total).toBe(1.123)
    expect(totalToText(5.5)).toBe('5.5')
    expect(totalToText(25)).toBe('25')
    expect(totalToText(null)).toBe('')
  })

  it('un total absurdo (más del tope) no se acepta', () => {
    expect(calcTotal(st([['99999', '999']]))).toMatchObject({ total: null, issue: 'tooLarge' })
    expect(calcTotal(st([[String(CALC_MAX), '1']])).total).toBe(CALC_MAX)
  })

  it('al abrir la calculadora lo que ya estaba escrito queda como sueltas; agregar, editar y quitar bloques', () => {
    expect(calcFromText(' 12 ')).toEqual({ blocks: [{ rows: '', cols: '' }], extra: '12' })
    let s = emptyCalc()
    s = setBlock(s, 0, { rows: '2', cols: '3' })
    s = addBlock(s)
    s = setBlock(s, 1, { rows: '1', cols: '4' })
    expect(calcTotal(s).total).toBe(10)
    s = removeBlock(s, 0)
    expect(s.blocks).toEqual([{ rows: '1', cols: '4' }])
    // quitar el único bloque lo deja en blanco (no desaparece)
    expect(removeBlock(s, 0).blocks).toEqual([{ rows: '', cols: '' }])
  })
})

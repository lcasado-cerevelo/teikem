import { addBlock, calcFromText, calcTotal, emptyCalc, removeBlock, setBlock, totalToText, CALC_MAX, splitByPack, packLabel, type CalcState } from './quantityCalc'

const st = (blocks: Array<[string, string] | [string, string, string]>, extra = ''): CalcState => ({ blocks: blocks.map(([rows, cols, depth]) => ({ rows, cols, depth: depth ?? '' })), extra })

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
    expect(calcFromText(' 12 ')).toEqual({ blocks: [{ rows: '', cols: '', depth: '' }], extra: '12' })
    let s = emptyCalc()
    s = setBlock(s, 0, { rows: '2', cols: '3' })
    s = addBlock(s)
    s = setBlock(s, 1, { rows: '1', cols: '4' })
    expect(calcTotal(s).total).toBe(10)
    s = removeBlock(s, 0)
    expect(s.blocks).toEqual([{ rows: '1', cols: '4', depth: '' }])
    // quitar el único bloque lo deja en blanco (no desaparece)
    expect(removeBlock(s, 0).blocks).toEqual([{ rows: '', cols: '', depth: '' }])
  })

  it('el fondo (una detrás de otra) multiplica el bloque: 5 × 3 × 2 + 10 = 40; en blanco vale 1', () => {
    expect(calcTotal(st([['5', '3', '2']], '10'))).toEqual({ total: 40, expression: '(5 × 3 × 2) + 10', issue: null })
    expect(calcTotal(st([['5', '3', '']], '10'))).toEqual({ total: 25, expression: '(5 × 3) + 10', issue: null })
    expect(calcTotal(st([['5', '3', '1']])).expression).toBe('(5 × 3 × 1)')
    expect(calcTotal(st([['5', '3', '2'], ['4', '3', '']], '')).total).toBe(42)
  })

  it('un fondo inválido (0, negativo, decimal) o sin filas y columnas no calcula', () => {
    expect(calcTotal(st([['5', '3', '0']]))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(st([['5', '3', '-1']]))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(st([['5', '3', '1.5']]))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(st([['', '', '2']]))).toMatchObject({ total: null, issue: 'incompleteBlock' })
    expect(calcTotal(st([['', '', '0']]))).toMatchObject({ total: null, issue: 'invalid' })
    expect(calcTotal(st([['99999', '999', '2']]))).toMatchObject({ total: null, issue: 'tooLarge' })
  })
})

describe('calculadora con empaque', () => {
  const box = { qty: 12, name: 'Caja' }
  it('un bloque en empaques multiplica por las unidades del empaque', () => {
    const state: CalcState = { blocks: [{ rows: '3', cols: '4', depth: '2', unit: 'pack' }], extra: '5', extraPacks: '2' }
    expect(calcTotal(state, box)).toEqual({ total: 24 * 12 + 24 + 5, expression: '(3 × 4 × 2) × 12 + 2 × 12 + 5', issue: null })
  })
  it('sin empaque del producto, la unidad del bloque se ignora', () => {
    const state: CalcState = { blocks: [{ rows: '3', cols: '4', depth: '', unit: 'pack' }], extra: '' }
    expect(calcTotal(state).total).toBe(12)
  })
  it('empaques sueltos inválidos', () => {
    expect(calcTotal({ blocks: [{ rows: '', cols: '', depth: '' }], extra: '', extraPacks: '1.5' }, box).issue).toBe('invalid')
  })
  it('splitByPack y packLabel', () => {
    expect(splitByPack(290, box)).toEqual({ packs: 24, loose: 2 })
    expect(splitByPack(5, box)).toBeNull()
    expect(splitByPack(5, null)).toBeNull()
    expect(packLabel({ qty: 50, name: 'Barril' })).toBe('Barril de 50')
  })
})

import { api } from '../../kernel/api/client'
import { ApiError } from '../../kernel/api/problem'
import { checkCountLine, checkStateOf, forgetChecks, isClosedState, isCountOff, markCountOff, rememberCheck, revealMessage } from './lineCheck'

afterEach(() => {
  jest.restoreAllMocks()
  forgetChecks()
})

function mockPost(impl: () => Promise<unknown>) {
  jest.spyOn(api, 'POST').mockImplementation(impl as never)
}

describe('revealMessage', () => {
  it('RECOUNT nunca menciona lo esperado', () => {
    expect(revealMessage({ state: 'RECOUNT', matches: false, countedQty: 18, expectedQty: null })).toEqual({ key: 'count.revealRecount', tone: 'warn', params: {} })
  })
  it('MATCH con o sin número', () => {
    expect(revealMessage({ state: 'MATCH', matches: true, countedQty: 20, expectedQty: 20 })).toEqual({ key: 'count.revealMatchNumber', tone: 'ok', params: { counted: 20, expected: 20 } })
    expect(revealMessage({ state: 'MATCH', matches: true, countedQty: 20, expectedQty: null })).toEqual({ key: 'count.revealMatch', tone: 'ok', params: {} })
  })
  it('FINAL fuera del margen: "contaste X y se esperaba Y" o solo "No coincide"', () => {
    expect(revealMessage({ state: 'FINAL', matches: false, countedQty: 19, expectedQty: 20 })).toEqual({ key: 'count.revealFinalNumber', tone: 'warn', params: { counted: 19, expected: 20 } })
    expect(revealMessage({ state: 'FINAL', matches: false, countedQty: 19, expectedQty: null })).toEqual({ key: 'count.revealFinalNoNumber', tone: 'warn', params: {} })
  })
  it('FINAL dentro del margen coincide', () => {
    expect(revealMessage({ state: 'FINAL', matches: true, countedQty: 20, expectedQty: 20 }).key).toBe('count.revealMatchNumber')
  })
})

describe('estado de la sesión', () => {
  it('recuerda las líneas verificadas, cuáles están cerradas y los conteos sin verificación', () => {
    rememberCheck(7, 'RECOUNT')
    rememberCheck(8, 'MATCH')
    expect(checkStateOf(7)).toBe('RECOUNT')
    expect(isClosedState(checkStateOf(7))).toBe(false)
    expect(isClosedState(checkStateOf(8))).toBe(true)
    expect(isClosedState(checkStateOf(null))).toBe(false)
    markCountOff(3)
    expect(isCountOff(3)).toBe(true)
    forgetChecks()
    expect(checkStateOf(8)).toBeUndefined()
    expect(isCountOff(3)).toBe(false)
  })
})

describe('checkCountLine', () => {
  it('devuelve el resultado del servidor', async () => {
    mockPost(async () => ({ data: { lineId: 5, state: 'RECOUNT', matches: false, countedQty: 18 }, response: new Response(null, { status: 200 }) }))
    expect(await checkCountLine(1, 5, 18)).toEqual({ kind: 'result', result: { state: 'RECOUNT', matches: false, countedQty: 18, expectedQty: null } })
  })

  it('403 → off; sin señal → offline; 409 → rechazada con su mensaje', async () => {
    mockPost(async () => ({ error: { status: 403, title: 'No está habilitado ver lo esperado al contar.' }, response: new Response(null, { status: 403 }) }))
    expect(await checkCountLine(1, 5, 18)).toEqual({ kind: 'off' })
    mockPost(async () => {
      throw new ApiError(0, null)
    })
    expect(await checkCountLine(1, 5, 18)).toEqual({ kind: 'offline' })
    mockPost(async () => ({ error: { status: 409, title: 'La línea ya se verificó; no se puede cambiar su cantidad.' }, response: new Response(null, { status: 409 }) }))
    expect(await checkCountLine(1, 5, 18)).toEqual({ kind: 'rejected', message: 'La línea ya se verificó; no se puede cambiar su cantidad.' })
  })
})

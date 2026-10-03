// Lote A6 — clasificación del 409 "línea corregida por el supervisor" en la cola de salida y lectura segura de sus renglones.
import en from '../../kernel/i18n/en.json'
import es from '../../kernel/i18n/es.json'
import { translate } from '../../kernel/i18n/i18n'
import {
  classifyCountBatchRejection,
  CORRECTED_LINE_LOCKED,
  countIdFromBatchPath,
  countIdFromFinishPath,
  countRefreshState,
  isCorrectedLineLockedMessage,
  parseLockedRows,
  refreshStateKey,
  summarizeRefreshedLines,
  type RefreshedLine,
} from './countRejection'

// Texto EXACTO que arma CycleCountService.CaptureBatchAsync (src/Teikem.Infrastructure/Services/CycleCountService.cs).
const MSG_ONE = `${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 2 (SKU-A). No se guardó nada.`
const MSG_TWO = `${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 1 (TORN 1/4, ACERO), 3 (SKU-B). No se guardó nada.`

const BATCH_BODY = JSON.stringify({ lines: [{ lineId: 10, countedQty: 4 }, { lineId: 11, countedQty: 7.5 }, { binId: 3, productPublicId: 'p', countedQty: 0 }] })

function row(overrides: Partial<{ kind: string; path: string; body: string; last_error: string | null }> = {}) {
  return { kind: 'countBatch', path: '/api/v1/cycle-counts/300/lines/batch', body: BATCH_BODY, last_error: MSG_ONE, ...overrides }
}

describe('mensaje del servidor', () => {
  it('el mensaje base es el mismo que CycleCountRules.CorrectedLineLocked', () => {
    expect(CORRECTED_LINE_LOCKED).toBe('La línea ya fue corregida por el supervisor; no se puede volver a capturar.')
  })

  it('reconoce el 409 con y sin la lista de renglones, y nada más', () => {
    expect(isCorrectedLineLockedMessage(MSG_ONE)).toBe(true)
    expect(isCorrectedLineLockedMessage(CORRECTED_LINE_LOCKED)).toBe(true)
    expect(isCorrectedLineLockedMessage(`  ${MSG_TWO}  `)).toBe(true)
    expect(isCorrectedLineLockedMessage('El conteo ya fue reconciliado; solo se consulta.')).toBe(false)
    expect(isCorrectedLineLockedMessage('Ocurrió un error. Intente de nuevo.')).toBe(false)
    expect(isCorrectedLineLockedMessage(null)).toBe(false)
    expect(isCorrectedLineLockedMessage(undefined)).toBe(false)
  })
})

describe('parseLockedRows', () => {
  it('extrae renglón y SKU (un renglón, varios, SKU con coma, espacios y barra)', () => {
    expect(parseLockedRows(MSG_ONE)).toEqual([{ row: 2, sku: 'SKU-A' }])
    expect(parseLockedRows(MSG_TWO)).toEqual([
      { row: 1, sku: 'TORN 1/4, ACERO' },
      { row: 3, sku: 'SKU-B' },
    ])
  })

  it('cualquier formato distinto → null (se muestra el mensaje tal cual, sin adivinar)', () => {
    expect(parseLockedRows(CORRECTED_LINE_LOCKED)).toBeNull()
    // SKU con paréntesis: ambiguo
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 1 (SKU (A)). No se guardó nada.`)).toBeNull()
    // sin el cierre "No se guardó nada."
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 1 (SKU-A).`)).toBeNull()
    // renglón que no es número, renglón 0, SKU vacío, lista vacía, separador raro
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: x (SKU-A). No se guardó nada.`)).toBeNull()
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 0 (SKU-A). No se guardó nada.`)).toBeNull()
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 1 (). No se guardó nada.`)).toBeNull()
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: . No se guardó nada.`)).toBeNull()
    expect(parseLockedRows(`${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 1 (A); 2 (B). No se guardó nada.`)).toBeNull()
    // otro mensaje que termina igual
    expect(parseLockedRows('Otra cosa. Renglón(es) del lote: 1 (A). No se guardó nada.')).toBeNull()
  })
})

describe('rutas de la cola', () => {
  it('saca el id del conteo del lote y del cierre', () => {
    expect(countIdFromBatchPath('/api/v1/cycle-counts/300/lines/batch')).toBe(300)
    expect(countIdFromBatchPath('/api/v1/cycle-counts/300/lines')).toBeNull()
    expect(countIdFromBatchPath('/api/v1/cycle-counts/abc/lines/batch')).toBeNull()
    expect(countIdFromBatchPath('/api/v1/cycle-counts/0/lines/batch')).toBeNull()
    expect(countIdFromFinishPath('/api/v1/cycle-counts/300/finish')).toBe(300)
    expect(countIdFromFinishPath('/api/v1/cycle-counts/300/lines/batch')).toBeNull()
  })
})

describe('classifyCountBatchRejection', () => {
  it('captura de conteo con el 409: conteo, renglones y lo que mandó el operario en cada uno', () => {
    expect(classifyCountBatchRejection(row({ last_error: MSG_TWO }))).toEqual({
      countId: 300,
      rows: [
        { row: 1, sku: 'TORN 1/4, ACERO', capturedQty: 4 },
        { row: 3, sku: 'SKU-B', capturedQty: 0 },
      ],
      message: MSG_TWO,
    })
  })

  it('sin la lista de renglones: rows null y el mensaje tal cual', () => {
    expect(classifyCountBatchRejection(row({ last_error: CORRECTED_LINE_LOCKED }))).toEqual({ countId: 300, rows: null, message: CORRECTED_LINE_LOCKED })
  })

  it('cuerpo ilegible o renglón fuera del cuerpo: la cantidad queda en null, el resto sigue', () => {
    expect(classifyCountBatchRejection(row({ body: 'no es json' }))?.rows).toEqual([{ row: 2, sku: 'SKU-A', capturedQty: null }])
    expect(classifyCountBatchRejection(row({ body: '{"lines":[]}' }))?.rows).toEqual([{ row: 2, sku: 'SKU-A', capturedQty: null }])
  })

  it('ruta desconocida: se explica igual, sin conteo para actualizar', () => {
    expect(classifyCountBatchRejection(row({ path: '/otra' }))?.countId).toBeNull()
  })

  it('otros rechazos no se clasifican: otro mensaje, otro tipo o sin mensaje', () => {
    expect(classifyCountBatchRejection(row({ last_error: 'El conteo ya fue reconciliado; solo se consulta.' }))).toBeNull()
    expect(classifyCountBatchRejection(row({ kind: 'countFinish' }))).toBeNull()
    expect(classifyCountBatchRejection(row({ kind: 'receipt' }))).toBeNull()
    expect(classifyCountBatchRejection(row({ last_error: null }))).toBeNull()
  })
})

describe('estado del conteo tras actualizar', () => {
  it('OPEN → abierto; COUNTED → contado; reconciliado o desconocido → cerrado; dado de baja → no existe', () => {
    expect(countRefreshState('OPEN', true)).toBe('open')
    expect(countRefreshState('open', undefined)).toBe('open')
    expect(countRefreshState('COUNTED', true)).toBe('counted')
    expect(countRefreshState('RECONCILED', true)).toBe('closed')
    expect(countRefreshState('RECONCILED_VARIANCE', true)).toBe('closed')
    expect(countRefreshState(null, true)).toBe('closed')
    expect(countRefreshState('OPEN', false)).toBe('notFound')
  })

  it('resume corregidas y sin contar', () => {
    const base: RefreshedLine = { lineId: 1, sku: 'A', productName: 'A', binCode: 'B', lotNumber: null, countedQty: 1, wasCorrected: false, correctedByName: null }
    expect(
      summarizeRefreshedLines([
        base,
        { ...base, lineId: 2, wasCorrected: true, correctedByName: 'Beto' },
        { ...base, lineId: 3, countedQty: null },
      ]),
    ).toEqual({ total: 3, corrected: 1, uncounted: 1 })
  })
})

describe('textos (es/en)', () => {
  const KEYS = [
    'headline',
    'nothingSaved',
    'rowsTitle',
    'row',
    'rowWithQty',
    'serverSaid',
    'whatToDoTitle',
    'whatToDo',
    'noReopenInApp',
    'finishAlsoRejected',
    'refresh',
    'noCountId',
    'offline',
    'summary',
    'lineCounted',
    'lineUncounted',
    'lineCorrected',
    'lineCorrectedAnon',
    'moreLines',
    'discard',
  ]

  it('cada clave existe en los dos idiomas, incluidos los cuatro estados', () => {
    const esDict = es as unknown as { countRejection: Record<string, unknown> }
    const enDict = en as unknown as { countRejection: Record<string, unknown> }
    for (const k of KEYS) {
      expect(typeof esDict.countRejection[k]).toBe('string')
      expect(typeof enDict.countRejection[k]).toBe('string')
    }
    for (const s of ['open', 'counted', 'closed', 'notFound'] as const) {
      expect(translate('es', refreshStateKey(s), { number: 'CC-1' })).not.toBe(refreshStateKey(s))
      expect(translate('en', refreshStateKey(s), { number: 'CC-1' })).not.toBe(refreshStateKey(s))
    }
  })

  it('dice qué pasó, que no se guardó nada y qué hacer', () => {
    expect(translate('es', 'countRejection.headline')).toBe('El supervisor ya corrigió todas las líneas de este envío.')
    expect(translate('es', 'countRejection.nothingSaved')).toContain('No se guardó ninguna línea de este envío')
    expect(translate('es', 'countRejection.whatToDo')).toBe(
      'Si falta contar algo, crea un conteo nuevo (escanea la posición otra vez) o pide al supervisor que lo revise.',
    )
    expect(translate('en', 'countRejection.whatToDo')).toContain('create a new count')
    expect(translate('es', 'countRejection.refresh')).toBe('Actualizar el conteo')
    // el renglón y el SKU se pintan tal cual (sin separador de miles)
    expect(translate('es', 'countRejection.rowWithQty', { number: 1200, sku: 'SKU-1000', qty: 1500 })).toBe('• Renglón 1200: SKU-1000 (mandaste 1,500)')
  })
})

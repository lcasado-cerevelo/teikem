// Lote 13 — lógica pura del panel "Recolección" (collectForm.ts): fila vacía automática, compactación de líneas y cuerpo del
// POST, errores del servidor devueltos a su fila, filtro de dueño, FEFO sugerido (réplica de PickBatchRules.Eligible) y el
// esquema zod con los mensajes del manual 06 §7.
import { beforeAll, describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import { t } from '../../kernel/i18n/i18n'
import { setLang } from '../../kernel/i18n/i18n'
import type { components } from '../../kernel/api/schema'
import {
  buildCollectBody,
  collectSchema,
  compactPickLines,
  EMPTY_COLLECT_LINE,
  fefoAvailable,
  fefoBinOptions,
  fefoBinSuggestions,
  fefoCandidates,
  isBlankLine,
  needsTrailingBlank,
  OWN,
  ownerFilterFor,
  remapCollectErrors,
  type CollectLine,
} from './collectForm'
import type { LineIssue } from './lineRules'

type BalanceDto = components['schemas']['BalanceDto']

const line = (over: Partial<CollectLine>): CollectLine => ({ ...EMPTY_COLLECT_LINE, ...over })
const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'
const P2 = 'aaaaaaaa-0000-0000-0000-000000000002'
const CLIENT = 'cccccccc-0000-0000-0000-000000000001'
const WH = '11111111-1111-1111-1111-111111111111'

beforeAll(() => setLang('es'))

describe('fila vacía automática', () => {
  it('isBlankLine: sin producto, cantidad, posición, lote ni series', () => {
    expect(isBlankLine(EMPTY_COLLECT_LINE)).toBe(true)
    expect(isBlankLine(line({ quantity: 2 }))).toBe(false)
    expect(isBlankLine(line({ productPublicId: P1 }))).toBe(false)
    expect(isBlankLine(line({ binId: '10' }))).toBe(false)
    expect(isBlankLine(line({ serialNumbers: ' \n , ' }))).toBe(true)
  })

  it('needsTrailingBlank: la última fila tiene datos y no se llegó a 100', () => {
    expect(needsTrailingBlank([line({ productPublicId: P1, quantity: 1 })])).toBe(true)
    expect(needsTrailingBlank([line({ productPublicId: P1 }), EMPTY_COLLECT_LINE])).toBe(false)
    expect(needsTrailingBlank([])).toBe(true)
    const full = Array.from({ length: 100 }, () => line({ productPublicId: P1, quantity: 1 }))
    expect(needsTrailingBlank(full)).toBe(false)
  })
})

describe('compactPickLines y cuerpo del POST', () => {
  it('ignora las filas vacías (la del final y las de en medio) y guarda el índice de pantalla de cada línea enviada', () => {
    const lines = [line({ productPublicId: P1, quantity: 2 }), EMPTY_COLLECT_LINE, line({ productPublicId: P2, quantity: 1 }), EMPTY_COLLECT_LINE]
    const { lines: kept, indexMap } = compactPickLines(lines)
    expect(kept.map((l) => l.productPublicId)).toEqual([P1, P2])
    expect(indexMap).toEqual([0, 2])
  })

  it('arma el cuerpo con varias líneas: posición y lote como número, series separadas, vacíos = null', () => {
    const { body, indexMap } = buildCollectBody({
      warehousePublicId: WH,
      lines: [
        line({ productPublicId: P1, quantity: 2.5, binId: '10', lotId: '7' }),
        line({ productPublicId: P2, quantity: 2, serialNumbers: 'S-1\nS-2' }),
        EMPTY_COLLECT_LINE,
      ],
    })
    expect(indexMap).toEqual([0, 1])
    expect(body).toEqual({
      warehousePublicId: WH,
      lines: [
        { productPublicId: P1, quantity: 2.5, binId: 10, lotId: 7, serialNumbers: null },
        { productPublicId: P2, quantity: 2, binId: null, lotId: null, serialNumbers: ['S-1', 'S-2'] },
      ],
    })
  })
})

describe('remapCollectErrors', () => {
  it('devuelve los errores lines[k] del servidor a la fila de la pantalla; sin campo = la cantidad; "lines" queda igual', () => {
    const err = new ApiError(400, {
      title: 'Datos inválidos',
      errors: {
        'lines[0].quantity': ['La cantidad debe ser mayor que cero.'],
        'lines[1]': ['No hay existencia suficiente de B-2 en ALM-01.'],
        'lines[1].serialNumbers': ['La serie S-1 está repetida en la recolección.'],
        lines: ['Una recolección solo puede tener productos de un mismo dueño.'],
      },
    })
    const out = remapCollectErrors(err, [0, 3]) as ApiError
    expect(out).toBeInstanceOf(ApiError)
    expect(out.title).toBe('Datos inválidos')
    expect(out.errors).toEqual({
      'lines.0.quantity': ['La cantidad debe ser mayor que cero.'],
      'lines.3.quantity': ['No hay existencia suficiente de B-2 en ALM-01.'],
      'lines.3.serialNumbers': ['La serie S-1 está repetida en la recolección.'],
      lines: ['Una recolección solo puede tener productos de un mismo dueño.'],
    })
  })

  it('índice fuera del mapa o error que no es del API: sin cambios', () => {
    const err = new ApiError(400, { title: 'x', errors: { 'lines[5].quantity': ['m'] } })
    expect((remapCollectErrors(err, [0]) as ApiError).errors).toEqual({ 'lines[5].quantity': ['m'] })
    const plain = new Error('red')
    expect(remapCollectErrors(plain, [0])).toBe(plain)
  })
})

describe('ownerFilterFor', () => {
  it('limita el selector al dueño de la primera OTRA fila con producto', () => {
    const lines = [line({ productPublicId: P1, owner: CLIENT }), line({ productPublicId: P2, owner: OWN }), EMPTY_COLLECT_LINE]
    expect(ownerFilterFor(lines, 2)).toEqual({ ownerClientPublicId: CLIENT })
    expect(ownerFilterFor(lines, 0)).toEqual({ ownOnly: true })
    expect(ownerFilterFor([EMPTY_COLLECT_LINE], 0)).toEqual({})
    expect(ownerFilterFor([line({ owner: OWN }), EMPTY_COLLECT_LINE], 1)).toEqual({ ownOnly: true })
  })
})

describe('FEFO sugerido (PickBatchRules.Eligible)', () => {
  const bal = (over: Partial<BalanceDto>): BalanceDto => ({ binId: 1, binCode: 'X', zoneTypeCode: 'PICKING', qtyAvailable: 5, lotId: null, expiryDate: null, ...over })
  const balances: BalanceDto[] = [
    bal({ binId: 1, binCode: 'R-01', zoneTypeCode: 'RESERVE', expiryDate: null }),
    bal({ binId: 2, binCode: 'P-02', zoneTypeCode: 'PICKING', expiryDate: '2026-12-01', lotId: 21, lotNumber: 'L-21' }),
    bal({ binId: 3, binCode: 'P-01', zoneTypeCode: 'PICKING', expiryDate: '2026-11-01', lotId: 22 }),
    bal({ binId: 4, binCode: 'Q-01', zoneTypeCode: 'QUARANTINE', expiryDate: '2026-01-01' }),
    bal({ binId: 5, binCode: 'X-01', zoneTypeCode: 'CROSSDOCK', expiryDate: '2026-01-01' }),
    bal({ binId: 6, binCode: 'P-00', zoneTypeCode: 'PICKING', qtyAvailable: 0, expiryDate: '2026-01-01' }),
    bal({ binId: 7, binCode: 'S-01', zoneTypeCode: 'STAGING', expiryDate: null }),
    bal({ binId: 8, binCode: 'P-09', zoneTypeCode: 'PICKING', expiryDate: null }),
    bal({ binId: null, binCode: null, expiryDate: '2026-01-01' }),
    bal({ binId: 3, binCode: 'P-01', zoneTypeCode: 'PICKING', expiryDate: '2026-12-01', lotId: 23 }),
  ]

  it('excluye cuarentena, cruce de muelle, disponible ≤ 0 y saldos sin posición; ordena por vencimiento, zona y código', () => {
    expect(fefoCandidates(balances).map((b) => `${b.binId}/${b.lotId ?? '-'}`)).toEqual(['3/22', '3/23', '2/21', '8/-', '1/-', '7/-'])
  })

  it('posiciones sin repetir en orden FEFO; con lote, solo ese lote; disponible = suma de lo elegible', () => {
    expect(fefoBinSuggestions(balances)).toEqual([3, 2, 8, 1, 7])
    expect(fefoBinSuggestions(balances, 21)).toEqual([2])
    expect(fefoAvailable(balances)).toBe(30)
    expect(fefoAvailable(balances, 22)).toBe(5)
    expect(fefoBinSuggestions([])).toEqual([])
  })

  it('opciones del selector "Posición": una por posición con su disponible (suma de lotes), en orden FEFO; con lote, solo ese lote', () => {
    expect(fefoBinOptions(balances).map((b) => `${b.binCode}:${b.qtyAvailable}`)).toEqual(['P-01:10', 'P-02:5', 'P-09:5', 'R-01:5', 'S-01:5'])
    expect(fefoBinOptions(balances, 23).map((b) => `${b.binId}:${b.qtyAvailable}`)).toEqual(['3:5'])
    expect(fefoBinOptions(balances, 99)).toEqual([])
    expect(fefoBinOptions([])).toEqual([])
  })

  it('código de posición en orden ordinal (como StringComparer.Ordinal): mayúsculas antes que minúsculas', () => {
    const out = fefoCandidates([bal({ binId: 1, binCode: 'a-1' }), bal({ binId: 2, binCode: 'B-1' })])
    expect(out.map((b) => b.binCode)).toEqual(['B-1', 'a-1'])
  })
})

describe('collectSchema', () => {
  const issueText = (i: LineIssue) => t(`warehouse.lineRules.${i.code}`, i.params)
  const schema = () => collectSchema(t, issueText)
  const errorsOf = (values: unknown) => {
    const r = schema().safeParse(values)
    if (r.success) return {}
    return Object.fromEntries(r.error.issues.map((i) => [i.path.join('.'), i.message]))
  }

  it('solo la fila vacía: almacén y "al menos una línea" bajo el producto de la primera fila', () => {
    expect(errorsOf({ warehousePublicId: null, lines: [EMPTY_COLLECT_LINE] })).toEqual({
      warehousePublicId: t('warehouse.receipts.errors.warehouseRequired'),
      'lines.0.productPublicId': 'Indique al menos una línea a recolectar.',
    })
  })

  it('la fila vacía del final no se valida; una fila con cantidad sin producto sí', () => {
    const ok = { warehousePublicId: WH, lines: [line({ productPublicId: P1, sku: 'A-1', trackingTypeCode: 'NONE', owner: OWN, quantity: 2 }), EMPTY_COLLECT_LINE] }
    expect(schema().safeParse(ok).success).toBe(true)
    expect(errorsOf({ warehousePublicId: WH, lines: [EMPTY_COLLECT_LINE, line({ quantity: 3 })] })).toEqual({
      'lines.1.productPublicId': t('warehouse.receipts.errors.productRequired'),
    })
  })

  it('reglas por línea con el índice de la pantalla, series repetidas entre líneas y un solo dueño', () => {
    const errors = errorsOf({
      warehousePublicId: WH,
      lines: [
        line({ productPublicId: P1, sku: 'A-1', trackingTypeCode: 'SERIAL', owner: CLIENT, quantity: 1, serialNumbers: 'S-1' }),
        EMPTY_COLLECT_LINE,
        line({ productPublicId: P2, sku: 'B-2', trackingTypeCode: 'SERIAL', owner: CLIENT, quantity: 1, serialNumbers: 's-1' }),
        line({ productPublicId: P2, sku: 'C-3', trackingTypeCode: 'NONE', owner: OWN, quantity: 0 }),
      ],
    })
    expect(errors).toEqual({
      'lines.3.quantity': 'La cantidad debe ser mayor que cero.',
      'lines.2.serialNumbers': 'La serie s-1 está repetida en la recolección.',
      'lines.3.productPublicId': 'Una recolección solo puede tener productos de un mismo dueño.',
    })
  })
})

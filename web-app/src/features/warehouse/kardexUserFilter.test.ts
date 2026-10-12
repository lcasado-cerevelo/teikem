// Filtro Usuario del Kárdex (2026-10-12; docs/kardex-filtro-usuario-diseno.md): constructor de la consulta y "Filtros aplicados".
import { describe, expect, it } from 'vitest'
import {
  EMPTY_INVENTORY_FILTERS,
  filterApplies,
  inactiveFilters,
  kardexQuery,
  summaryQuery,
  SYSTEM_USER,
  userQuery,
} from './kardexView'
import { adjustmentsQuery, describeMovementFilters, EMPTY_MOVEMENT_FILTERS, transfersQuery } from './movementFilters'

const t = (key: string) => key

describe('filtro Usuario · consulta', () => {
  it('userQuery: «Sistema» va como includeSystem y las personas como ids numéricos; vacío = no filtra', () => {
    expect(userQuery([SYSTEM_USER])).toEqual({ userIds: undefined, includeSystem: true })
    expect(userQuery(['2', '5'])).toEqual({ userIds: [2, 5], includeSystem: undefined })
    expect(userQuery([SYSTEM_USER, '2'])).toEqual({ userIds: [2], includeSystem: true })
    expect(userQuery([])).toEqual({ userIds: undefined, includeSystem: undefined })
    // un valor que no es id (p. ej. una URL vieja) no se manda
    expect(userQuery(['abc', '7'])).toEqual({ userIds: [7], includeSystem: undefined })
  })

  it('el Kárdex y su resumen llevan los mismos userIds / includeSystem; sin usuarios no viajan', () => {
    const f = { ...EMPTY_INVENTORY_FILTERS, users: ['2', '3', SYSTEM_USER] }
    expect(kardexQuery(f)).toMatchObject({ userIds: [2, 3], includeSystem: true })
    expect(summaryQuery(f)).toMatchObject({ userIds: [2, 3], includeSystem: true })
    const none = JSON.parse(JSON.stringify(kardexQuery(EMPTY_INVENTORY_FILTERS)))
    expect(none).not.toHaveProperty('userIds')
    expect(none).not.toHaveProperty('includeSystem')
  })

  it('Saldos no aplica el filtro de usuario (se atenúa si tiene valor); el Kárdex sí', () => {
    expect(filterApplies('kardex', 'users')).toBe(true)
    expect(filterApplies('balances', 'users')).toBe(false)
    expect(filterApplies('reconciliation', 'users')).toBe(false)
    expect(inactiveFilters('balances', { ...EMPTY_INVENTORY_FILTERS, users: ['2'] })).toEqual(['users'])
    expect(inactiveFilters('kardex', { ...EMPTY_INVENTORY_FILTERS, users: ['2'] })).toEqual([])
  })

  it('Transferencias y ajustes: la consulta de cada pestaña lleva el usuario', () => {
    const f = { ...EMPTY_MOVEMENT_FILTERS, users: ['4', SYSTEM_USER] }
    expect(adjustmentsQuery(f)).toMatchObject({ types: ['ADJUSTMENT'], userIds: [4], includeSystem: true })
    expect(transfersQuery(f)).toMatchObject({ types: ['TRANSFER'], userIds: [4], includeSystem: true })
  })

  it('«Filtros aplicados» del Reporte de ajustes nombra a los usuarios (y deja el id si no lo conoce)', () => {
    const names = { warehouses: new Map(), owners: new Map(), reasons: new Map(), users: new Map([['4', 'Beto Supervisor'], [SYSTEM_USER, 'Sistema (sin usuario)']]) }
    const out = describeMovementFilters({ ...EMPTY_MOVEMENT_FILTERS, users: ['4', SYSTEM_USER, '9'] }, names, t)
    expect(out.at(-1)).toEqual({ label: 'warehouse.transfersAdjustments.filters.user', value: 'Beto Supervisor, Sistema (sin usuario), 9' })
    // sin usuarios no aparece la línea
    expect(describeMovementFilters(EMPTY_MOVEMENT_FILTERS, names, t).some((x) => x.label.endsWith('.user'))).toBe(false)
  })
})

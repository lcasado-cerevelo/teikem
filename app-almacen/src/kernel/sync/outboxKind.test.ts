import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../db/database'
import { translate } from '../i18n/i18n'
import { outboxKindKey, outboxKindLabel } from './outboxKind'
import type { OutboxKind } from './outbox'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

// Todos los tipos que la cola puede guardar (el Record de outboxKind.ts ya obliga en tsc; aquí se comprueba el texto).
const ALL: OutboxKind[] = ['receipt', 'pack', 'collect', 'countBatch', 'countFinish', 'transfer', 'adjust', 'damage', 'manualIssue']

describe('etiqueta del tipo de operación de la cola (Sincronización, 2026-10-11)', () => {
  it('cada tipo tiene su nombre en español y en inglés, distinto del kind técnico', () => {
    for (const kind of ALL) {
      const key = outboxKindKey(kind)
      expect(key).toBe(`sync.kind${kind[0].toUpperCase()}${kind.slice(1)}`)
      for (const lang of ['es', 'en'] as const) {
        const label = outboxKindLabel(kind, (k) => translate(lang, k))
        expect(label).not.toBe(kind)
        expect(label).not.toBe(key)
      }
    }
  })

  it('nombres concretos (es / en)', () => {
    const es = (kind: string) => outboxKindLabel(kind, (k) => translate('es', k))
    const en = (kind: string) => outboxKindLabel(kind, (k) => translate('en', k))
    expect(es('manualIssue')).toBe('Despacho manual')
    expect(en('manualIssue')).toBe('Manual issue')
    expect(es('transfer')).toBe('Transferencia')
    expect(es('adjust')).toBe('Ajuste de inventario')
    expect(es('damage')).toBe('Reporte de daño')
    expect(es('pack')).toBe('Despacho (recolectar y empacar)')
    expect(es('collect')).toBe('Despacho (solo recolectar)')
    expect(en('countFinish')).toBe('Count (finish)')
  })

  it('un kind desconocido (de otra versión de la app) se muestra tal cual', () => {
    expect(outboxKindKey('somethingNew')).toBeNull()
    expect(outboxKindLabel('somethingNew', (k) => translate('es', k))).toBe('somethingNew')
    // nombres heredados de Object.prototype tampoco cuentan como tipo conocido
    expect(outboxKindKey('toString')).toBeNull()
    expect(outboxKindLabel('constructor', (k) => translate('es', k))).toBe('constructor')
  })
})

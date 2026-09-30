// Registro de filtros aplicados (lógica pura): orden de la barra, valores vacíos ignorados, cómo se escribe cada valor y
// la oración "Filtros: …" / "Sin filtros" de las exportaciones.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../i18n/i18n'
import {
  createFilterRegistry,
  dateRangeFilterText,
  filterItemText,
  filtersSentence,
  formatFilterDate,
  joinFilterValues,
  textFilterValue,
} from './filterRegistry'

beforeAll(() => setLang('es'))

describe('createFilterRegistry', () => {
  it('cuenta los controles, ignora los vacíos (null) y conserva el orden de alta al actualizar', () => {
    const r = createFilterRegistry()
    r.set('a', { label: 'Almacén', value: 'ALM-DEPOT' })
    r.set('b', { label: 'Estatus', value: null })
    r.set('c', { label: 'Solo manuales', value: '' })
    // actualizar un valor no lo manda al final
    r.set('a', { label: 'Almacén', value: 'ALM-2' })
    expect(r.snapshot()).toEqual({
      controls: 3,
      applied: [
        { label: 'Almacén', value: 'ALM-2' },
        { label: 'Solo manuales', value: '' },
      ],
    })
    r.remove('a')
    r.remove('c')
    expect(r.snapshot()).toEqual({ controls: 1, applied: [] })
  })

  it('con elementos, ordena como el documento (un filtro montado después pero antes en la barra va primero)', () => {
    const bar = document.createElement('div')
    const first = document.createElement('div')
    const second = document.createElement('div')
    bar.append(first, second)
    document.body.append(bar)
    try {
      const r = createFilterRegistry()
      r.set('second', { label: 'Estatus', value: 'Recibiendo', element: second })
      r.set('none', { label: 'Vista', value: 'Activos' })
      r.set('first', { label: 'Almacén', value: 'ALM-DEPOT', element: first })
      expect(r.snapshot().applied.map((f) => f.label)).toEqual(['Almacén', 'Estatus', 'Vista'])
    } finally {
      bar.remove()
    }
  })
})

describe('valores legibles', () => {
  it('selección múltiple: lista, "y N más" desde el quinto; vacía = null', () => {
    expect(joinFilterValues([], t)).toBeNull()
    expect(joinFilterValues(['  ', ''], t)).toBeNull()
    expect(joinFilterValues(['A', 'B'], t)).toBe('A, B')
    expect(joinFilterValues(['A', 'B', 'C', 'D', 'E', 'F'], t)).toBe('A, B, C, D y 2 más')
  })

  it('"Código · Nombre" se escribe "Código (Nombre)": el " · " queda solo entre filtros', () => {
    expect(filterItemText('ALM-01 · Almacén principal')).toBe('ALM-01 (Almacén principal)')
    expect(filterItemText('A-01 · PISO · ALM-01')).toBe('A-01 (PISO, ALM-01)')
    expect(filterItemText('Recibiendo')).toBe('Recibiendo')
    expect(joinFilterValues(['ALM-01 · Principal', 'ALM-02 · Norte'], t)).toBe('ALM-01 (Principal), ALM-02 (Norte)')
  })

  it('fechas: día/mes/año en español, mes/día/año en inglés; rango, desde, hasta o nada', () => {
    expect(formatFilterDate('2026-09-01', 'es')).toBe('01/09/2026')
    expect(formatFilterDate('2026-09-01', 'en')).toBe('09/01/2026')
    expect(dateRangeFilterText({ from: '2026-09-01', to: '2026-09-30' }, 'es', t)).toBe('del 01/09/2026 al 30/09/2026')
    expect(dateRangeFilterText({ from: '2026-09-01', to: '' }, 'es', t)).toBe('desde 01/09/2026')
    expect(dateRangeFilterText({ from: '', to: '2026-09-30' }, 'es', t)).toBe('hasta 30/09/2026')
    expect(dateRangeFilterText({ from: '', to: '' }, 'es', t)).toBeNull()
  })

  it('texto libre entre comillas; vacío o solo espacios = null', () => {
    expect(textFilterValue('  abc ')).toBe('"abc"')
    expect(textFilterValue('   ')).toBeNull()
    expect(textFilterValue(undefined)).toBeNull()
  })
})

describe('filtersSentence', () => {
  it('sin ámbito o sin controles: sin línea (null)', () => {
    expect(filtersSentence(null, t)).toBeNull()
    expect(filtersSentence({ controls: 0, applied: [] }, t)).toBeNull()
  })

  it('barra sin nada elegido: "Sin filtros"', () => {
    expect(filtersSentence({ controls: 4, applied: [] }, t)).toBe('Sin filtros')
  })

  it('"Filtros: Etiqueta valor · …"; un interruptor va solo con su etiqueta', () => {
    const s = filtersSentence(
      {
        controls: 5,
        applied: [
          { label: 'Almacén', value: 'ALM-DEPOT' },
          { label: 'Estatus', value: 'Recibiendo' },
          { label: 'Creado', value: 'del 01/09/2026 al 30/09/2026' },
          { label: 'Solo manuales', value: '' },
        ],
      },
      t,
    )
    expect(s).toBe('Filtros: Almacén ALM-DEPOT · Estatus Recibiendo · Creado del 01/09/2026 al 30/09/2026 · Solo manuales')
  })

  it('en inglés', () => {
    setLang('en')
    try {
      expect(filtersSentence({ controls: 1, applied: [{ label: 'Status', value: 'Open' }] }, t)).toBe('Filters: Status Open')
      expect(filtersSentence({ controls: 1, applied: [] }, t)).toBe('No filters')
    } finally {
      setLang('es')
    }
  })
})

import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { PR_FORMAT, resetFormatSettings, setFormatSettings } from '../format'
import { setLang } from '../i18n/i18n'
import type { DatePresetMode } from './datePresets'
import { EMPTY_RANGE, type DateRange } from './dateRange'
import { DateRangeFilter } from './Filters'
import { typeDate } from './typeDate'

// «Hoy» fijo: lunes 2026-10-12, mediodía en Puerto Rico (zona de la compañía por omisión: UTC−4).
const NOW = new Date('2026-10-12T16:00:00Z')

beforeAll(() => setLang('es'))
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'], now: NOW })
  resetFormatSettings()
})
afterEach(() => {
  vi.useRealTimers()
  resetFormatSettings()
})

function Host({ initial = EMPTY_RANGE, mode, onValue }: { initial?: DateRange; mode?: DatePresetMode; onValue?: (v: DateRange) => void }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <DateRangeFilter
        label="Fecha"
        value={value}
        mode={mode}
        onChange={(v) => {
          setValue(v)
          onValue?.(v)
        }}
      />
      <button type="button" onClick={() => setValue(EMPTY_RANGE)}>
        limpiar
      </button>
    </>
  )
}

const preset = () => screen.getByRole('combobox', { name: 'Fecha' })

describe('DateRangeFilter compacto', () => {
  it('sin fechas muestra «Todas las fechas» y no muestra los campos de fecha', () => {
    render(<Host />)
    expect(preset()).toHaveValue('all')
    expect(screen.queryByLabelText('Desde')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Hasta')).not.toBeInTheDocument()
  })

  it('escoger un rango escribe sus fechas (hoy de la compañía) y no abre los campos', async () => {
    const user = userEvent.setup()
    const onValue = vi.fn()
    render(<Host onValue={onValue} />)
    await user.selectOptions(preset(), 'thisMonth')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-01', to: '2026-10-31' })
    expect(preset()).toHaveValue('thisMonth')
    expect(screen.queryByLabelText('Desde')).not.toBeInTheDocument()
    await user.selectOptions(preset(), 'last7')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-06', to: '2026-10-12' })
    await user.selectOptions(preset(), 'all')
    expect(onValue).toHaveBeenLastCalledWith({ from: '', to: '' })
  })

  it('«Esta semana» es la semana completa y respeta el primer día de la compañía (domingo / lunes)', async () => {
    const user = userEvent.setup()
    const onValue = vi.fn()
    const { unmount } = render(<Host onValue={onValue} />)
    await user.selectOptions(preset(), 'thisWeek')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-11', to: '2026-10-17' })
    unmount()
    setFormatSettings({ ...PR_FORMAT, weekStartDay: 1 })
    render(<Host onValue={onValue} />)
    await user.selectOptions(preset(), 'thisWeek')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-12', to: '2026-10-18' })
  })

  it('«hoy» es el de la zona de la compañía, no el de UTC (a las 9 p. m. en Puerto Rico ya es mañana en UTC)', async () => {
    vi.setSystemTime(new Date('2026-10-13T01:00:00Z')) // 2026-10-12 21:00 en Puerto Rico
    const user = userEvent.setup()
    const onValue = vi.fn()
    render(<Host onValue={onValue} />)
    await user.selectOptions(preset(), 'today')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-12', to: '2026-10-12' })
  })

  it('el desplegable se deduce de las fechas que ya trae la pantalla', () => {
    const { unmount } = render(<Host initial={{ from: '2026-10-12', to: '2026-10-12' }} />)
    expect(preset()).toHaveValue('today')
    unmount()
    render(<Host initial={{ from: '2026-09-01', to: '2026-09-30' }} />)
    expect(preset()).toHaveValue('lastMonth')
  })

  it('«Personalizado» abre los dos campos con las fechas actuales; escribir no cambia el desplegable aunque coincida con un rango', async () => {
    const user = userEvent.setup()
    const onValue = vi.fn()
    render(<Host initial={{ from: '2026-10-12', to: '2026-10-12' }} onValue={onValue} />)
    await user.selectOptions(preset(), 'custom')
    const from = screen.getByLabelText('Desde')
    const to = screen.getByLabelText('Hasta')
    expect(from).toHaveValue('10/12/2026')
    expect(to).toHaveValue('10/12/2026')
    expect(onValue).not.toHaveBeenCalled() // escoger «Personalizado» no cambia el filtro

    await user.clear(from)
    await typeDate(user, from, '2026-10-05')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-05', to: '2026-10-12' })
    // ahora coincide con ninguna opción; el desplegable sigue en «Personalizado» y los campos siguen a la vista
    expect(preset()).toHaveValue('custom')
    await user.clear(to)
    await typeDate(user, to, '2026-10-05')
    expect(onValue).toHaveBeenLastCalledWith({ from: '2026-10-05', to: '2026-10-05' })
    expect(preset()).toHaveValue('custom')
    expect(screen.getByLabelText('Desde')).toBeInTheDocument()
  })

  it('«Desde» no pasa de «Hasta» y «Hasta» no es anterior a «Desde» (en el calendario esos días no se pueden elegir)', async () => {
    const user = userEvent.setup()
    render(<Host initial={{ from: '2026-10-05', to: '2026-10-20' }} />)
    await user.selectOptions(preset(), 'custom')
    await user.click(screen.getByLabelText('Desde'))
    expect(document.querySelector('button[data-iso="2026-10-20"]')).toBeEnabled()
    expect(document.querySelector('button[data-iso="2026-10-21"]')).toBeDisabled()
    await user.click(screen.getByLabelText('Hasta'))
    expect(document.querySelector('button[data-iso="2026-10-05"]')).toBeEnabled()
    expect(document.querySelector('button[data-iso="2026-10-04"]')).toBeDisabled()
  })

  it('«Personalizado» sin fechas todavía no filtra; «Limpiar» desde afuera vuelve a «Todas las fechas» y cierra los campos', async () => {
    const user = userEvent.setup()
    const onValue = vi.fn()
    render(<Host initial={{ from: '2026-10-01', to: '2026-10-31' }} onValue={onValue} />)
    await user.selectOptions(preset(), 'custom')
    expect(screen.getByLabelText('Desde')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'limpiar' }))
    expect(preset()).toHaveValue('all')
    expect(screen.queryByLabelText('Desde')).not.toBeInTheDocument()
  })

  it('el juego «futuro» ofrece Mañana y Próximos 7 días y no ofrece Ayer ni Últimos 30 días; el juego «ambos», Ayer y Mañana', () => {
    const labels = (c: HTMLElement) => within(c).getAllByRole('option').map((o) => o.textContent)
    const { container, unmount } = render(<Host mode="future" />)
    expect(labels(container)).toEqual(['Todas las fechas', 'Hoy', 'Mañana', 'Próximos 7 días', 'Esta semana', 'Próxima semana', 'Este mes', 'Próximo mes', 'Personalizado'])
    unmount()
    const both = render(<Host mode="both" />)
    expect(labels(both.container)).toEqual(expect.arrayContaining(['Ayer', 'Hoy', 'Mañana', 'Últimos 7 días', 'Próximos 7 días']))
  })

  it('el juego por omisión («pasado») trae la lista acordada', () => {
    const { container } = render(<Host />)
    expect(within(container).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Todas las fechas',
      'Hoy',
      'Ayer',
      'Últimos 7 días',
      'Esta semana',
      'Semana pasada',
      'Últimos 30 días',
      'Este mes',
      'Mes pasado',
      'Este año',
      'Personalizado',
    ])
  })
})

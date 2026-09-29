import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StrictMode, useState } from 'react'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { CommandPalette } from './CommandPalette'
import {
  closeCommandPalette,
  filterCommands,
  groupCommands,
  isCommandPaletteOpen,
  isPaletteShortcut,
  useCommandPaletteShortcut,
  type CommandItem,
} from './commandPaletteStore'

const ITEMS: CommandItem[] = [
  { id: '/', group: 'ops', groupLabel: 'Operación', title: 'Pulso del día', subtitle: 'las dos corrientes en vivo' },
  { id: '/ops/dispatch', group: 'ops', groupLabel: 'Operación', title: 'Sala de despacho', subtitle: 'armar y despachar rutas' },
  { id: '/analytics/charts', group: 'analytics', groupLabel: 'Análisis', title: 'Gráficos', subtitle: 'barras, dona y línea' },
  { id: '/system/users', group: 'system', groupLabel: 'Sistema', title: 'Roles y usuarios', subtitle: 'RBAC: roles, permisos y usuarios' },
]

function renderPalette() {
  const onSelect = vi.fn()
  const onClose = vi.fn()
  render(<CommandPalette open items={ITEMS} onSelect={onSelect} onClose={onClose} />)
  return { onSelect, onClose, input: screen.getByRole('combobox', { name: 'Buscar o ejecutar…' }) }
}

const optionTitles = () => screen.queryAllByRole('option').map((o) => o.querySelector('.nm')?.textContent)

describe('CommandPalette', () => {
  beforeAll(() => setLang('es'))

  it('cerrada no pinta nada', () => {
    render(<CommandPalette open={false} items={ITEMS} onSelect={() => {}} onClose={() => {}} />)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('abierta: todos los ítems agrupados, con título y subtítulo; el foco en el buscador', () => {
    const { input } = renderPalette()
    expect(screen.getByRole('dialog', { name: 'Paleta de comandos' })).toBeInTheDocument()
    expect(input).toHaveFocus()
    const groups = screen.getAllByRole('group')
    expect(groups.map((g) => g.querySelector('.pgrp')?.textContent)).toEqual(['Operación', 'Análisis', 'Sistema'])
    expect(within(groups[0]).getAllByRole('option')).toHaveLength(2)
    expect(screen.getByText('armar y despachar rutas')).toBeInTheDocument()
    // el primero está activo
    expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true')
  })

  it('en StrictMode (desarrollo) el foco también queda en el buscador', () => {
    render(
      <StrictMode>
        <button type="button">antes</button>
        <CommandPalette open items={ITEMS} onSelect={() => {}} onClose={() => {}} />
      </StrictMode>,
    )
    expect(screen.getByRole('combobox')).toHaveFocus()
  })

  it('filtra por título y subtítulo, sin acentos ni mayúsculas', async () => {
    const user = userEvent.setup()
    const { input } = renderPalette()
    await user.type(input, 'GRAFICOS')
    expect(optionTitles()).toEqual(['Gráficos'])
    await user.clear(input)
    await user.type(input, 'despachar')
    expect(optionTitles()).toEqual(['Sala de despacho'])
    await user.clear(input)
    await user.type(input, 'usu')
    expect(optionTitles()).toEqual(['Roles y usuarios'])
    await user.clear(input)
    await user.type(input, 'zzz')
    expect(optionTitles()).toEqual([])
    expect(screen.getByText('Ninguna pantalla coincide con la búsqueda.')).toBeInTheDocument()
  })

  it('↓ ↓ Enter elige el tercero; ↑ vuelve; Enter con un filtro elige el primero que coincide', async () => {
    const user = userEvent.setup()
    const { input, onSelect } = renderPalette()
    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(screen.getAllByRole('option')[2]).toHaveAttribute('aria-selected', 'true')
    expect(input.getAttribute('aria-activedescendant')).toBe(screen.getAllByRole('option')[2].id)
    await user.keyboard('{ArrowUp}{Enter}')
    expect(onSelect).toHaveBeenLastCalledWith(ITEMS[1])
    await user.type(input, 'roles')
    await user.keyboard('{Enter}')
    expect(onSelect).toHaveBeenLastCalledWith(ITEMS[3])
  })

  it('las flechas no se salen de la lista', async () => {
    const user = userEvent.setup()
    renderPalette()
    await user.keyboard('{ArrowUp}')
    expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true')
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}{ArrowDown}{ArrowDown}')
    expect(screen.getAllByRole('option')[3]).toHaveAttribute('aria-selected', 'true')
  })

  it('clic en un ítem lo elige', async () => {
    const user = userEvent.setup()
    const { onSelect } = renderPalette()
    await user.click(screen.getByText('Gráficos'))
    expect(onSelect).toHaveBeenCalledWith(ITEMS[2])
  })

  it('Esc, el botón cerrar o el clic fuera cierran', async () => {
    const user = userEvent.setup()
    const { onClose } = renderPalette()
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('button', { name: 'Cerrar' }))
    expect(onClose).toHaveBeenCalledTimes(2)
    await user.pointer({ keys: '[MouseLeft]', target: document.querySelector('.scrim.cmdp')! })
    expect(onClose).toHaveBeenCalledTimes(3)
  })

  it('al cerrar, el foco vuelve a quien la abrió', async () => {
    const user = userEvent.setup()
    function Harness() {
      const [open, setOpen] = useState(false)
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            abrir
          </button>
          <CommandPalette open={open} items={ITEMS} onSelect={() => {}} onClose={() => setOpen(false)} />
        </>
      )
    }
    render(<Harness />)
    const opener = screen.getByRole('button', { name: 'abrir' })
    await user.click(opener)
    expect(screen.getByRole('combobox')).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(opener).toHaveFocus()
  })
})

describe('lógica pura y atajos', () => {
  afterEach(() => closeCommandPalette())

  it('filterCommands y groupCommands conservan el orden', () => {
    expect(filterCommands(ITEMS, 'sala').map((i) => i.id)).toEqual(['/ops/dispatch'])
    expect(filterCommands(ITEMS, '').length).toBe(4)
    expect(groupCommands(ITEMS).map((g) => [g.group, g.items.length])).toEqual([
      ['ops', 2],
      ['analytics', 1],
      ['system', 1],
    ])
  })

  it('isPaletteShortcut: "/" y Ctrl/⌘+K', () => {
    const k = (key: string, mods: Partial<Record<'ctrlKey' | 'metaKey' | 'altKey', boolean>> = {}) =>
      isPaletteShortcut({ key, ctrlKey: false, metaKey: false, altKey: false, ...mods })
    expect(k('/')).toBe(true)
    expect(k('k', { ctrlKey: true })).toBe(true)
    expect(k('K', { metaKey: true })).toBe(true)
    expect(k('k')).toBe(false)
    expect(k('/', { altKey: true })).toBe(false)
  })

  it('useCommandPaletteShortcut: "/" fuera de un campo abre; dentro de un campo se escribe', async () => {
    const user = userEvent.setup()
    function Harness() {
      useCommandPaletteShortcut()
      return <input aria-label="campo" />
    }
    render(<Harness />)
    await user.type(screen.getByLabelText('campo'), '/')
    expect(isCommandPaletteOpen()).toBe(false)
    expect(screen.getByLabelText('campo')).toHaveValue('/')
    act(() => (document.activeElement as HTMLElement).blur())
    await user.keyboard('/')
    expect(isCommandPaletteOpen()).toBe(true)
    closeCommandPalette()
    await user.keyboard('{Control>}k{/Control}')
    expect(isCommandPaletteOpen()).toBe(true)
  })
})

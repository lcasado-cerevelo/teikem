import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../access/AccessProvider'
import { setLang } from '../i18n/i18n'
import { DataTable, type DataColumn, type RowAction, type SortState } from './DataTable'

interface Row {
  id: number
  code: string
  qty: number | null
  active: boolean
}

const ROWS: Row[] = [
  { id: 1, code: 'B-02', qty: 5, active: true },
  { id: 2, code: 'A-10', qty: null, active: false },
  { id: 3, code: 'A-2', qty: 12, active: true },
  { id: 4, code: 'C-01', qty: 1, active: true },
  { id: 5, code: 'D-07', qty: 8, active: true },
]

const COLUMNS: DataColumn<Row>[] = [
  { id: 'code', header: 'Código', cell: (r) => r.code, sortValue: (r) => r.code },
  { id: 'qty', header: 'Cantidad', cell: (r) => r.qty ?? '—', sortValue: (r) => r.qty, align: 'end' },
]

beforeAll(() => setLang('es'))

/** Códigos en el orden en que se ven (primera celda de cada fila del cuerpo). */
function codes(): string[] {
  const body = screen.getAllByRole('rowgroup')[1]
  return within(body)
    .getAllByRole('row')
    .map((tr) => within(tr).getAllByRole('cell')[0].textContent ?? '')
}

/** Simula el modo tarjetas (bajo 720 px); devuelve la función que restaura matchMedia. */
function cardsMode(): () => void {
  const original = window.matchMedia
  window.matchMedia = ((query: string) => ({
    matches: query === '(max-width: 720px)',
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as typeof window.matchMedia
  return () => {
    window.matchMedia = original
  }
}

/** Códigos en el orden en que se ven las tarjetas (el código es el título de cada una). */
function cardCodes(): string[] {
  return within(screen.getByRole('list', { name: 'Artículos' }))
    .getAllByRole('listitem')
    .map((li) => li.querySelector('.ttl')?.textContent ?? '')
}

describe('DataTable', () => {
  it('ordena por columna al hacer clic (flecha y aria-sort); vacíos al final en ambos sentidos', async () => {
    const user = userEvent.setup()
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} />)
    expect(codes()).toEqual(['B-02', 'A-10', 'A-2', 'C-01', 'D-07'])

    await user.click(screen.getByRole('button', { name: 'Código' }))
    // orden natural: A-2 antes que A-10
    expect(codes()).toEqual(['A-2', 'A-10', 'B-02', 'C-01', 'D-07'])
    expect(screen.getByRole('columnheader', { name: /Código/ })).toHaveAttribute('aria-sort', 'ascending')
    expect(screen.getByText('▲')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Código/ }))
    expect(codes()).toEqual(['D-07', 'C-01', 'B-02', 'A-10', 'A-2'])
    expect(screen.getByText('▼')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Cantidad' }))
    expect(codes()).toEqual(['C-01', 'B-02', 'D-07', 'A-2', 'A-10'])
    await user.click(screen.getByRole('button', { name: /Cantidad/ }))
    expect(codes()).toEqual(['A-2', 'D-07', 'B-02', 'C-01', 'A-10'])
  })

  it('pagina localmente cuando la lista llega completa', async () => {
    const user = userEvent.setup()
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} pageSize={2} defaultSort={{ id: 'code', desc: false }} />)
    expect(codes()).toEqual(['A-2', 'A-10'])
    expect(screen.getByText('1–2 de 5')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(codes()).toEqual(['B-02', 'C-01'])
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(codes()).toEqual(['D-07'])
    expect(screen.getByText('Página 3 de 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Página siguiente' })).toBeDisabled()
  })

  it('con orden y paginación del servidor no reordena ni corta: avisa con onSort/onPage', async () => {
    const user = userEvent.setup()
    const onSort = vi.fn()
    const onPage = vi.fn()
    const page2 = ROWS.slice(2, 4)
    render(
      <DataTable
        columns={COLUMNS}
        rows={page2}
        rowKey={(r) => r.id}
        sort={{ id: 'qty', desc: true }}
        onSort={onSort}
        page={2}
        pageSize={2}
        total={5}
        onPage={onPage}
      />,
    )
    // se pinta tal cual llega (el servidor ya ordenó) y la flecha refleja el orden recibido
    expect(codes()).toEqual(['A-2', 'C-01'])
    expect(screen.getByRole('columnheader', { name: /Cantidad/ })).toHaveAttribute('aria-sort', 'descending')
    expect(screen.getByText('3–4 de 5')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Código' }))
    expect(onSort).toHaveBeenLastCalledWith({ id: 'code', desc: false })
    await user.click(screen.getByRole('button', { name: /Cantidad/ }))
    expect(onSort).toHaveBeenLastCalledWith({ id: 'qty', desc: false })

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(onPage).toHaveBeenLastCalledWith(3)
    await user.click(screen.getByRole('button', { name: 'Página anterior' }))
    expect(onPage).toHaveBeenLastCalledWith(1)
  })

  it('rowActions: sin permiso no se pintan; la guarda de estatus las oculta por fila', async () => {
    const user = userEvent.setup()
    const onDeactivate = vi.fn()
    const actions: RowAction<Row>[] = [
      { key: 'deactivate', label: 'Dar de baja', perm: 'items.update', visible: (r) => r.active, onClick: onDeactivate },
      { key: 'delete', label: 'Borrar', perm: 'items.delete', onClick: vi.fn() },
    ]
    render(
      <AccessProvider permissions={['items.update']} modules={[]}>
        <DataTable columns={COLUMNS} rows={ROWS.slice(0, 2)} rowKey={(r) => r.id} rowActions={actions} />
      </AccessProvider>,
    )
    expect(screen.queryByRole('button', { name: 'Borrar' })).toBeNull()
    const buttons = screen.getAllByRole('button', { name: 'Dar de baja' })
    expect(buttons).toHaveLength(1) // la fila inactiva no la ofrece
    await user.click(buttons[0])
    expect(onDeactivate).toHaveBeenCalledWith(ROWS[0])
  })

  it('bajo 720 px pinta tarjetas (título + etiqueta: valor) en lugar de la tabla', () => {
    const original = window.matchMedia
    window.matchMedia = ((query: string) => ({
      matches: query === '(max-width: 720px)',
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    })) as typeof window.matchMedia
    try {
      render(<DataTable columns={COLUMNS} rows={ROWS.slice(0, 2)} rowKey={(r) => r.id} label="Artículos" />)
      expect(screen.queryByRole('table')).toBeNull()
      const cards = within(screen.getByRole('list', { name: 'Artículos' })).getAllByRole('listitem')
      expect(cards).toHaveLength(2)
      expect(cards[0]).toHaveTextContent('B-02')
      expect(cards[0]).toHaveTextContent('Cantidad5')
    } finally {
      window.matchMedia = original
    }
  })

  it('en tarjetas ordena con "Ordenar por" y el botón ▲/▼ invierte el sentido (orden local)', async () => {
    const restore = cardsMode()
    try {
      const user = userEvent.setup()
      render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} label="Artículos" />)
      expect(cardCodes()).toEqual(['B-02', 'A-10', 'A-2', 'C-01', 'D-07'])
      expect(screen.queryByRole('button', { name: 'Ascendente' })).toBeNull()

      await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar por' }), 'code')
      expect(cardCodes()).toEqual(['A-2', 'A-10', 'B-02', 'C-01', 'D-07'])
      await user.click(screen.getByRole('button', { name: 'Ascendente' }))
      expect(cardCodes()).toEqual(['D-07', 'C-01', 'B-02', 'A-10', 'A-2'])
      expect(screen.getByRole('button', { name: 'Descendente' })).toHaveTextContent('▼')

      // otra columna vuelve a empezar en ascendente; vacíos al final
      await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar por' }), 'qty')
      expect(cardCodes()).toEqual(['C-01', 'B-02', 'D-07', 'A-2', 'A-10'])
      await user.click(screen.getByRole('button', { name: 'Ascendente' }))
      expect(cardCodes()).toEqual(['A-2', 'D-07', 'B-02', 'C-01', 'A-10'])

      // sin columna: vuelve al orden de llegada
      await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar por' }), '')
      expect(cardCodes()).toEqual(['B-02', 'A-10', 'A-2', 'C-01', 'D-07'])
    } finally {
      restore()
    }
  })

  it('en tarjetas con orden del servidor avisa con onSort (columna y sentido)', async () => {
    const restore = cardsMode()
    try {
      const user = userEvent.setup()
      const onSort = vi.fn()
      const serverCols: DataColumn<Row>[] = [
        { id: 'code', header: 'Código', cell: (r) => r.code, sortable: true },
        { id: 'qty', header: 'Cantidad', cell: (r) => r.qty ?? '—', sortable: true },
      ]
      function Host() {
        const [sort, setSort] = useState<SortState | null>(null)
        return (
          <DataTable
            columns={serverCols}
            rows={ROWS}
            rowKey={(r) => r.id}
            label="Artículos"
            sort={sort}
            onSort={(next) => {
              onSort(next)
              setSort(next)
            }}
          />
        )
      }
      render(<Host />)
      await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar por' }), 'qty')
      expect(onSort).toHaveBeenLastCalledWith({ id: 'qty', desc: false })
      // el servidor ordena: las tarjetas conservan el orden recibido
      expect(cardCodes()).toEqual(['B-02', 'A-10', 'A-2', 'C-01', 'D-07'])
      await user.click(screen.getByRole('button', { name: 'Ascendente' }))
      expect(onSort).toHaveBeenLastCalledWith({ id: 'qty', desc: true })
      expect(screen.getByRole('button', { name: 'Descendente' })).toBeInTheDocument()
    } finally {
      restore()
    }
  })

  it('sin filas muestra el estado vacío', () => {
    render(<DataTable columns={COLUMNS} rows={[]} rowKey={(r) => r.id} />)
    expect(screen.getByTestId('empty-state')).toHaveTextContent('Sin resultados')
  })

  it('sin scrollbar propio: con muchas columnas pasa sola a la variante compacta (.densetbl)', () => {
    const many: DataColumn<Row>[] = Array.from({ length: 8 }, (_, i) => ({ id: `c${i}`, header: `C${i}`, cell: (r) => r.qty, align: 'end' }))
    const { unmount } = render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} />)
    expect(screen.getByRole('table')).not.toHaveClass('densetbl')
    unmount()
    const second = render(<DataTable columns={many} rows={ROWS} rowKey={(r) => r.id} />)
    expect(screen.getByRole('table')).toHaveClass('lst', 'densetbl')
    second.unmount()
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} dense />)
    expect(screen.getByRole('table')).toHaveClass('densetbl')
  })
})

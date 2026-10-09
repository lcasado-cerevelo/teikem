import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../access/AccessProvider'
import { setLang } from '../i18n/i18n'
import { DataTable, type DataColumn, type RowAction, type SortState } from './DataTable'
import { exportTable } from './exportTable'
import { toast } from './toast'

// la descarga real (SheetJS/jsPDF/Blob) no corre en jsdom: se verifica qué filas recibe
vi.mock('./exportTable', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./exportTable')>()),
  exportTable: vi.fn(() => Promise.resolve()),
}))

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

  it('rowClassName agrega su clase a la fila y a la tarjeta (junto a la de clic)', () => {
    const dimInactive = (r: Row) => (r.active ? undefined : 'dim')
    const { unmount } = render(
      <DataTable columns={COLUMNS} rows={ROWS.slice(0, 2)} rowKey={(r) => r.id} onRowClick={vi.fn()} rowClassName={dimInactive} />,
    )
    const rows = within(screen.getAllByRole('rowgroup')[1]).getAllByRole('row')
    expect(rows[0]).toHaveAttribute('class', 'click')
    expect(rows[1]).toHaveAttribute('class', 'click dim')
    unmount()

    const restore = cardsMode()
    try {
      render(<DataTable columns={COLUMNS} rows={ROWS.slice(0, 2)} rowKey={(r) => r.id} label="Artículos" rowClassName={dimInactive} />)
      const cards = within(screen.getByRole('list', { name: 'Artículos' })).getAllByRole('listitem')
      expect(cards[0]).toHaveAttribute('class', 'dt-card')
      expect(cards[1]).toHaveAttribute('class', 'dt-card dim')
    } finally {
      restore()
    }
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

  it('forceCards: tarjetas aunque la ventana sea ancha (panel angosto), con el mismo pie; false = tabla', async () => {
    const user = userEvent.setup()
    const { rerender } = render(
      <DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} label="Artículos" pageSize={2} forceCards />,
    )
    expect(screen.queryByRole('table')).toBeNull()
    expect(cardCodes()).toEqual(['B-02', 'A-10'])
    // el pie no cambia: rango, selector, Exportar y ‹ ›
    expect(screen.getByText('1–2 de 5')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filas por página' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Exportar' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(cardCodes()).toEqual(['A-2', 'C-01'])
    // "Ordenar por" de las tarjetas
    expect(screen.getByRole('combobox', { name: 'Ordenar por' })).toBeInTheDocument()

    rerender(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} label="Artículos" pageSize={2} forceCards={false} />)
    expect(screen.getByRole('table')).toBeInTheDocument()
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

/** 30 filas: A-01 … A-30 (cantidad = número de fila). */
const MANY: Row[] = Array.from({ length: 30 }, (_, i) => ({
  id: i + 1,
  code: `A-${String(i + 1).padStart(2, '0')}`,
  qty: i + 1,
  active: true,
}))

/** Abre el menú Exportar y elige CSV. */
async function exportCsv(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Exportar' }))
  await user.click(screen.getByRole('menuitem', { name: 'CSV (.csv)' }))
}

describe('DataTable: pie (rango, filas por página, exportar)', () => {
  it('con una sola página muestra igual el rango/total y el selector, sin botones de página', () => {
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} />)
    expect(screen.getByText('1–5 de 5')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filas por página' })).toHaveValue('25')
    expect(screen.getByRole('button', { name: 'Exportar' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Página siguiente' })).toBeNull()
  })

  it('paginación local por defecto (25) y el selector cambia el tamaño volviendo a la página 1', async () => {
    const user = userEvent.setup()
    render(<DataTable columns={COLUMNS} rows={MANY} rowKey={(r) => r.id} />)
    expect(codes()).toHaveLength(25)
    expect(screen.getByText('1–25 de 30')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(codes()).toEqual(['A-26', 'A-27', 'A-28', 'A-29', 'A-30'])

    await user.selectOptions(screen.getByRole('combobox', { name: 'Filas por página' }), '10')
    expect(codes()).toHaveLength(10)
    expect(codes()[0]).toBe('A-01')
    expect(screen.getByText('1–10 de 30')).toBeInTheDocument()
    expect(screen.getByText('Página 1 de 3')).toBeInTheDocument()

    await user.selectOptions(screen.getByRole('combobox', { name: 'Filas por página' }), '50')
    expect(codes()).toHaveLength(30)
    expect(screen.queryByRole('button', { name: 'Página siguiente' })).toBeNull()
  })

  it('un pageSize inicial fuera de las opciones se agrega al selector', () => {
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} pageSize={2} />)
    const select = screen.getByRole('combobox', { name: 'Filas por página' })
    expect(select).toHaveValue('2')
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['2', '10', '25', '50', '100'])
  })

  it('paginación del servidor: el selector avisa con onPageSize; sin onPageSize no se muestra', async () => {
    const user = userEvent.setup()
    const onPageSize = vi.fn()
    const { unmount } = render(
      <DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} page={1} pageSize={25} total={551} onPage={vi.fn()} onPageSize={onPageSize} />,
    )
    expect(screen.getByText('1–25 de 551')).toBeInTheDocument()
    await user.selectOptions(screen.getByRole('combobox', { name: 'Filas por página' }), '100')
    expect(onPageSize).toHaveBeenCalledWith(100)
    // la pantalla es dueña del tamaño: sin nuevo pageSize, las filas no cambian
    expect(codes()).toHaveLength(5)
    unmount()

    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} page={1} pageSize={25} total={551} onPage={vi.fn()} />)
    expect(screen.getByText('1–25 de 551')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Filas por página' })).toBeNull()
  })

  it('pagination={false} muestra todas las filas sin rango ni selector', () => {
    render(<DataTable columns={COLUMNS} rows={MANY} rowKey={(r) => r.id} pagination={false} />)
    expect(codes()).toHaveLength(30)
    expect(screen.queryByText(/de 30/)).toBeNull()
    expect(screen.queryByRole('combobox', { name: 'Filas por página' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Página siguiente' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Exportar' })).toBeInTheDocument()
  })

  it('pagination={false} y exportable={false}: sin pie', () => {
    const { container } = render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} pagination={false} exportable={false} />)
    expect(container.querySelector('.dt-pager')).toBeNull()
  })

  it('sin exportRows exporta todas las filas cargadas en el orden actual (no solo la página)', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    render(<DataTable columns={COLUMNS} rows={MANY} rowKey={(r) => r.id} pageSize={10} defaultSort={{ id: 'qty', desc: true }} />)
    await user.click(screen.getByRole('button', { name: 'Exportar' }))
    expect(screen.getByText('Filas: 30')).toBeInTheDocument()
    await user.click(screen.getByRole('menuitem', { name: 'CSV (.csv)' }))
    expect(exportTable).toHaveBeenCalledTimes(1)
    const [format, , rows] = vi.mocked(exportTable).mock.calls[0]
    expect(format).toBe('csv')
    expect(rows).toHaveLength(30)
    expect((rows as Row[])[0].code).toBe('A-30')
  })

  it('con exportRows exporta lo que devuelve (reordenado si el orden es local) y la nota muestra el total', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    const exportRows = vi.fn(() => Promise.resolve({ items: ROWS, truncated: false }))
    render(
      <DataTable
        columns={COLUMNS}
        rows={ROWS.slice(0, 2)}
        rowKey={(r) => r.id}
        page={1}
        pageSize={2}
        total={551}
        onPage={vi.fn()}
        defaultSort={{ id: 'code', desc: false }}
        exportRows={exportRows}
      />,
    )
    // con orden local se lee toda la consulta para ordenar (5 filas): el total deja de ser el del servidor (551 de este mock)
    await screen.findByText('1–2 de 5')
    await user.click(screen.getByRole('button', { name: 'Exportar' }))
    expect(screen.getByText('Filas: 5')).toBeInTheDocument()
    await user.click(screen.getByRole('menuitem', { name: 'CSV (.csv)' }))
    expect(exportRows).toHaveBeenCalledTimes(1) // la misma lectura sirve para ordenar y para exportar
    const rows = vi.mocked(exportTable).mock.calls[0][2] as Row[]
    expect(rows.map((r) => r.code)).toEqual(['A-2', 'A-10', 'B-02', 'C-01', 'D-07'])
  })

  it('exportRows truncado avisa con un toast; un arreglo simple también se acepta', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    const info = vi.spyOn(toast, 'info').mockImplementation(() => {})
    try {
      const { unmount } = render(
        <DataTable
          columns={COLUMNS}
          rows={ROWS}
          rowKey={(r) => r.id}
          page={1}
          total={20000}
          onPage={vi.fn()}
          exportRows={() => Promise.resolve({ items: MANY, truncated: true })}
        />,
      )
      await exportCsv(user)
      expect(info).toHaveBeenCalledWith(expect.stringContaining('30'))
      expect(vi.mocked(exportTable).mock.calls[0][2]).toHaveLength(30)
      unmount()

      info.mockClear()
      render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} exportRows={() => Promise.resolve(MANY)} />)
      await exportCsv(user)
      expect(info).not.toHaveBeenCalled()
      expect(vi.mocked(exportTable).mock.calls[1][2]).toHaveLength(30)
    } finally {
      info.mockRestore()
    }
  })
})

describe('DataTable · onSortChange', () => {
  it('avisa el orden local al hacer clic en un encabezado (el mismo que muestra la tabla)', async () => {
    const user = userEvent.setup()
    const seen: Array<SortState | null> = []
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} onSortChange={(s) => seen.push(s)} />)
    expect(seen.at(-1)).toBeNull()
    await user.click(screen.getByRole('button', { name: /Código/ }))
    expect(seen.at(-1)).toEqual({ id: 'code', desc: false })
    await user.click(screen.getByRole('button', { name: /Código/ }))
    expect(seen.at(-1)).toEqual({ id: 'code', desc: true })
  })
})

describe('DataTable · ordenar todo lo filtrado (paginación del servidor)', () => {
  it('al ordenar lee la consulta completa (exportRows) y ordena y pagina todo, no solo la página visible', async () => {
    const user = userEvent.setup()
    const PAGE1 = ROWS.slice(0, 2) // el servidor solo mandó 2 de 5
    const exportRows = vi.fn(() => Promise.resolve({ items: ROWS, truncated: false }))
    const onPage = vi.fn()
    render(
      <DataTable columns={COLUMNS} rows={PAGE1} rowKey={(r) => r.id} page={1} pageSize={2} total={5} onPage={onPage} exportRows={exportRows} />,
    )
    expect(codes()).toEqual(['B-02', 'A-10'])
    await user.click(screen.getByRole('button', { name: /Código/ }))
    // las 5 filas del filtro, ordenadas, en páginas de 2
    expect(await screen.findByText('1–2 de 5')).toBeInTheDocument()
    expect(codes()).toEqual(['A-2', 'A-10'])
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(codes()).toEqual(['B-02', 'C-01'])
    expect(onPage).not.toHaveBeenCalled()
    expect(exportRows).toHaveBeenCalledTimes(1)
  })
})

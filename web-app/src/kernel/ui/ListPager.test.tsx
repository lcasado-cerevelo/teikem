import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import type { DataColumn } from './DataTable'
import { exportTable } from './exportTable'
import { ListPager } from './ListPager'
import { pageSizeOptions } from './pageSize'
import { Panel } from './Panel'
import { toast } from './toast'

// la descarga real (SheetJS/jsPDF/Blob) no corre en jsdom: se verifica qué recibe
vi.mock('./exportTable', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./exportTable')>()),
  exportTable: vi.fn(() => Promise.resolve()),
}))

interface Receipt {
  number: string
}
const COLS: DataColumn<Receipt>[] = [{ id: 'number', header: 'Número', cell: (r) => r.number }]

beforeAll(() => setLang('es'))

describe('ListPager', () => {
  it('rango y total, ‹ › con onPage (‹ deshabilitado en la primera, › en la última)', async () => {
    const user = userEvent.setup()
    const onPage = vi.fn()
    const { rerender } = render(<ListPager page={1} pageSize={25} total={552} onPage={onPage} />)
    expect(screen.getByText('1–25 de 552')).toBeInTheDocument()
    expect(screen.getByText('Página 1 de 23')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }))
    expect(onPage).toHaveBeenCalledWith(2)

    rerender(<ListPager page={23} pageSize={25} total={552} onPage={onPage} />)
    expect(screen.getByText('551–552 de 552')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Página siguiente' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Página anterior' }))
    expect(onPage).toHaveBeenLastCalledWith(22)
  })

  it('una sola página o sin onPage: sin ‹ ›, el rango se ve igual; sin onPageSize no hay selector; sin exportación no hay Exportar', () => {
    const { rerender } = render(<ListPager page={1} pageSize={25} total={7} onPage={vi.fn()} />)
    expect(screen.getByText('1–7 de 7')).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).toBeNull()
    rerender(<ListPager page={1} pageSize={25} total={100} />)
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Exportar' })).toBeNull()
  })

  it('total 0: no pinta nada', () => {
    const { container } = render(<ListPager page={1} pageSize={25} total={0} onPage={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('"Filas por página" avisa con onPageSize; el tamaño actual siempre está entre las opciones', async () => {
    const user = userEvent.setup()
    const onPageSize = vi.fn()
    render(<ListPager page={1} pageSize={25} total={552} onPage={vi.fn()} onPageSize={onPageSize} />)
    const select = screen.getByRole('combobox', { name: 'Filas por página' })
    expect(select).toHaveValue('25')
    await user.selectOptions(select, '50')
    expect(onPageSize).toHaveBeenCalledWith(50)
    expect(pageSizeOptions(25)).toEqual([10, 25, 50, 100])
    expect(pageSizeOptions(5)).toEqual([5, 10, 25, 50, 100])
  })

  it('Exportar con columnas y filas: exporta todo lo que devuelve exportRows con el título del Panel; nota = total', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    const exportRows = vi.fn(() => Promise.resolve({ items: [{ number: 'REC-1' }, { number: 'REC-2' }], truncated: false }))
    render(
      <Panel title="Recibos">
        <ListPager page={1} pageSize={25} total={552} onPage={vi.fn()} exportColumns={COLS} exportRows={exportRows} />
      </Panel>,
    )
    await user.click(screen.getByRole('button', { name: 'Exportar' }))
    expect(screen.getByText('Filas: 552')).toBeInTheDocument()
    await user.click(screen.getByRole('menuitem', { name: 'CSV (.csv)' }))
    expect(exportRows).toHaveBeenCalledTimes(1)
    const [format, cols, rows, opts] = vi.mocked(exportTable).mock.calls[0]
    expect(format).toBe('csv')
    expect(cols).toBe(COLS)
    expect(rows).toEqual([{ number: 'REC-1' }, { number: 'REC-2' }])
    expect(opts).toMatchObject({ locale: 'es', title: 'Recibos' })
  })

  it('exportRows truncado avisa con un toast; exportFileName manda sobre el título del Panel', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    const info = vi.spyOn(toast, 'info').mockImplementation(() => {})
    try {
      render(
        <Panel title="Recibos">
          <ListPager
            page={1}
            pageSize={25}
            total={20000}
            exportColumns={COLS}
            exportFileName="recibos-abiertos"
            exportRows={() => Promise.resolve({ items: [{ number: 'REC-1' }], truncated: true })}
          />
        </Panel>,
      )
      await user.click(screen.getByRole('button', { name: 'Exportar' }))
      await user.click(screen.getByRole('menuitem', { name: 'Excel (.xlsx)' }))
      expect(info).toHaveBeenCalledWith(expect.stringContaining('1'))
      expect(vi.mocked(exportTable).mock.calls[0][3]).toMatchObject({ title: 'recibos-abiertos' })
    } finally {
      info.mockRestore()
    }
  })

  it('onExport propio (el que usa DataTable) en vez de columnas y filas, con su propia nota', async () => {
    const user = userEvent.setup()
    const onExport = vi.fn()
    render(<ListPager page={1} pageSize={25} total={40} onExport={onExport} exportCount={25} showRange={false} />)
    expect(screen.queryByText(/de 40/)).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Exportar' }))
    expect(screen.getByText('Filas: 25')).toBeInTheDocument()
    await user.click(screen.getByRole('menuitem', { name: 'PDF (.pdf)' }))
    expect(onExport).toHaveBeenCalledWith('pdf')
  })
})
